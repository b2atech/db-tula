# Data Tula — Verified Schema & Full Rule Catalog

> Companion to [data-tula-architecture-review.md](data-tula-architecture-review.md).
> Everything here was **verified against the live QA databases** (`db.qa.dgtula.com`, PostgreSQL 18.3)
> on 2026-06-04 by read-only inspection — not assumed. Column names, types, enum codes and the
> link model below are real. This document defines **what data-tula must check every day** and how.

---

## 1. The accounting architecture (verified)

Dgtula/Dhanman is a microservice system, **one PostgreSQL database per service**, all on the same
host `db.qa.dgtula.com` (QA). The **Common DB is the accounting hub** — every service posts its
business documents into a single general ledger that lives in Common.

```
   Sales DB            Purchase DB          Payroll DB            Inventory DB
 invoice_headers      bill_headers     payroll_transaction_h.   products / stock_ledgers
 invoice_payment_*    bill_payment_*   payment_headers          product_details
 customer_note_*      vendor_note_*    advance_payments         organization_products
 customers            vendors          employees                company_products
        \                  |                 |                       /
         \                 |                 |                      /
          v                v                 v                     v
                ┌───────────────────────────────────────────┐
                │  COMMON DB  (the ledger / accounting hub)   │
                │   transaction_headers  (bigint id)          │
                │   journal_entries      (D/C, positive amts) │
                │   chart_of_accounts / account_groups        │
                │   parties / customers / vendors / companies │
                └───────────────────────────────────────────┘
```

### 1.1 Reachable QA databases
`qa-dhanman-{common, sales, purchase, payroll, inventory, community, payment, einvoice, agent}`
(plus `test-dhanman-*`, `keycloak`, `dhanman_hangfire`). data-tula targets the service DBs;
**Common is the reconciliation counterpart for almost every financial rule.**

### 1.2 Common DB — ledger tables (verified columns)
- **`transaction_headers`** — PK `id **bigint**`; `company_id uuid`, `transaction_date`,
  `status_id int`, `document_number text`, `description`, `customer_id uuid?`, `vendor_id uuid?`,
  `employee_id uuid?`, **`document_id uuid?`** (= the source document's id), **`transaction_source_type int`**,
  **`is_reversed bool`**, `is_deleted bool`, audit cols. *(No `organization_id` here; tenant scoping is `company_id`.)*
- **`journal_entries`** — PK `id bigint`; `transaction_id bigint`, `transaction_date date`,
  **`amount numeric` (always ≥ 0 — verified: 0 negatives)**, **`entry_type char` ('D' / 'C')**,
  `account_id uuid?`, `entry_source_id int` (1=AUTO, 2=MANUAL), `journal_narration_id`, `dynamic_data jsonb`, `is_deleted`.
- **`chart_of_accounts`** — `id uuid`, `account_number`, `name`, `organization_id`, `account_type_id`,
  `account_group_code int`, `current_balance`, `opening_balance`, `is_default_account`, `is_pg_bank_account`.
- **`account_groups`** (`code`, `group_code`, `top_group_code`, `schedule`), **`account_types`** (`classification`, `account_category_id`).
- **`parties`** — `id uuid`, `organization_id`, `party_name`, `legal_name`, `gstin`, `pan`, `entity_type_id`, `is_active`.
- **`customers`** (Common) — `id uuid`, `company_id`, **`party_id uuid?`** (nullable). **`vendors`** (Common) — same shape.
- **`companies`** — `id uuid`, `organization_id`, `account_id`, `gstin`, `pan`. **`organizations`** — top tenant.
- Existing recon precedent: **`bank_reconciliations`** (`ledger_entry_id`, `matched_amount`, `reconciliation_status_id`).

### 1.3 `transaction_source_type` codes (verified from `transaction_source_types`)
| code | meaning | source DB / document |
|---|---|---|
| 1 | **Invoice** | Sales `invoice_headers` |
| 2 | **Bill** | Purchase `bill_headers` |
| 3 | **Invoice Payment** | Sales `invoice_payment_headers` |
| 4 | **Bill Payment** | Purchase `bill_payment_headers` |
| 5 | **Salary Posting** | Payroll `payroll_transaction_headers` |
| 6 | **Salary Payment** | Payroll `payment_headers` |
| 7 | Manual Journal | (Common, manual) |
| 8 / 9 | Vendor Credit / Debit Note | Purchase `vendor_note_headers` |
| 10 / 11 | Customer Debit / Credit Note | Sales `customer_note_headers` |
| 12–17 | Vendor / Customer / Employee Advance & Settlement | Purchase / Sales / Payroll |
| 18 | Opening Balance | (Common) |
| 19 | Bank Transfer | (Common / bank) |

### 1.4 THE LINK MODEL (the most important verified fact)
Every service document header carries `is_posted bool`, `transaction_id bigint?`,
`debit_account_id`/`credit_account_id`, `company_id`, a status id, totals and `tds_amount`.
There are **two links** between a document and its Common transaction:

- **Reverse (authoritative):** `transaction_headers.document_id = <document>.id` **AND**
  `transaction_headers.transaction_source_type = <code>`.
- **Forward (unreliable):** `<document>.transaction_id = transaction_headers.id`.

**Live evidence (Sales invoices, QA, 2026-06-04):**
| metric | count |
|---|---|
| invoices (not deleted) | 23,433 |
| Common sales-invoice txns (source_type=1, unique `document_id`) | 23,211 |
| invoices matched to a Common txn via `document_id` | 23,211 |
| …of those with `invoice.transaction_id` **NULL** (**broken back-link**) | **21,089** |
| posted-status invoices with **no** Common txn (**genuinely unposted**) | **222** |
| Common txns whose `document_id` is not an invoice (**orphan**) | 0 |
| duplicate `document_id` among sales-invoice txns (**duplicate posting**) | **150** |

**Conclusion that drives every reconciliation rule:** join documents to the ledger on
**`transaction_headers.document_id` + `transaction_source_type`**, *never* on
`<document>.transaction_id`. The forward link being null is itself a (high-volume) finding, not a join condition.

---

## 2. The universal Document → Ledger reconciliation template

Because every module shares the same shape, **one cross-DB rule template** covers Sales, Purchase
and Payroll. Each concrete rule just plugs in the service DB, table, source-type code, amount column
and party column.

**Inputs per instance:** `ServiceDb`, `documentTable`, `sourceTypeCode`, `amountColumn`,
`partyColumn` (customer_id / vendor_id / employee_id), `postedStatusIds`, `arApAccountColumn`
(`debit_account_id` for sales, `credit_account_id` for purchase).

**Algorithm (keyset-batched, read-only, `@ExecutionTimeUtc` cutoff + grace window):**
```
1. Stream posted-status documents from ServiceDb in id batches of 5000:
     id, document_number, company_id, <partyColumn>, <amountColumn>, transaction_id,
     debit_account_id, credit_account_id, is_posted, created_on_utc (<= cutoff - grace)
2. For the batch's ids, fetch from Common:
     transaction_headers WHERE transaction_source_type=<code> AND document_id = ANY(@ids) AND is_deleted=false
       -> group by document_id  (detect 0, 1, >1)
     journal_entries (joined by transaction_id) -> per txn: sum(D), sum(C), and sum on the doc's AR/AP account
3. Compare in C# and emit findings:
     R-01 existence : 0 txns for a posted doc            -> Critical  (missing posting)
     R-02 duplicate : >1 non-reversed txns for one doc   -> Critical  (duplicate posting)
     R-03 amount    : |doc.amount - AR/AP-leg amount| > tol -> Critical (amount mismatch)
     R-04 party     : txn.<party> <> doc.<party>          -> Critical (party drift)
     R-05 company   : txn.company_id <> doc.company_id     -> Critical (company drift)
     R-06 backlink  : txn exists but doc.transaction_id IS NULL or <> txn.id -> High (broken back-link)
     R-07 posted-flag: doc.is_posted=false but txn exists (or true but none) -> Medium (flag drift)
     R-08 reversal  : doc active but its only txn is_reversed=true           -> High (orphaned reversal)
```
Same template, three source modules → ~24 rules from one engine.

---

## 3. Rule catalog

Severity: **Critical** = ledger integrity broken; **High** = traceability/settlement broken;
**Medium** = referential/flag inconsistency; **Low** = informational.
Type: **SQL** = same-DB SQL-file rule; **C#** = cross-DB batched rule.

### 3.1 ACC — Common ledger integrity (same-DB SQL, Common only)
| code | check | flags when | sev | type |
|---|---|---|---|---|
| ACC-001 | Each transaction's journal is balanced | per `transaction_id`: `SUM(amount) FILTER(D) <> SUM(amount) FILTER(C)` (non-deleted, non-reversed) | Critical | SQL |
| ACC-002 | No orphan journal entries | `journal_entries.transaction_id` has no matching `transaction_headers.id` | Critical | SQL |
| ACC-003 | Journal entry has an account | `journal_entries.account_id IS NULL` (non-deleted) | High | SQL |
| ACC-004 | Transaction has journal entries | posted `transaction_headers` with zero non-deleted `journal_entries` | Critical | SQL |
| ACC-005 | Source type is known | `transaction_source_type` not in `transaction_source_types` | Medium | SQL |
| ACC-006 | Reversed txn is fully reversed | `is_reversed=true` but no compensating contra entries / net ≠ 0 | High | SQL |
| ACC-007 | Account belongs to txn's org/company | entry `account_id`'s `chart_of_accounts.organization_id` ≠ txn company's org | Medium | C# (Common only, but cross-row) |

### 3.2 SAL — Sales invoice & payment ↔ ledger (C# cross-DB: Sales + Common)
| code | check | source / target | sev | type |
|---|---|---|---|---|
| SAL-001 | Approved/posted invoice has exactly one transaction | `invoice_headers`(status∈{3,4,5}) ↔ `transaction_headers`(source_type=1, `document_id`) | Critical | C# |
| SAL-002 | Invoice total matches AR ledger leg | `invoice_headers.total_amount` vs `SUM(journal D on invoice.debit_account_id)` | Critical | C# |
| SAL-003 | Invoice customer matches transaction | `invoice_headers.customer_id` vs `transaction_headers.customer_id` | Critical | C# |
| SAL-004 | Invoice company matches transaction | `invoice_headers.company_id` vs `transaction_headers.company_id` | Critical | C# |
| SAL-005 | Settled amount matches payments | `invoice_headers.settled_amount` vs `SUM(invoice_payment_details.received_amount)` (+`tds_amount`) | High | **SQL (Sales only)** |
| SAL-006 | Invoice payment posted to ledger | `invoice_payment_headers`(is_posted) ↔ `transaction_headers`(source_type=3, `document_id`) | Critical | C# |
| SAL-007 | No duplicate invoice posting | >1 non-reversed source_type=1 txn for one `document_id` | Critical | C# |
| SAL-008 | Invoice back-link populated | invoice has Common txn but `invoice.transaction_id IS NULL` | High | C# |
| SAL-009 | Customer credit/debit note posted | `customer_note_headers` ↔ `transaction_headers`(source_type 10/11) | High | C# |
| SAL-010 | Tax components add up | `taxable_amount + sgst + cgst + igst + round_off + fees - discount = total_amount` | High | SQL (Sales only) |
| SAL-011 | Payment detail rolls up to header | `SUM(invoice_payment_details.received_amount)` per header = `invoice_payment_headers.received_amount` | Medium | SQL (Sales only) |

### 3.3 PUR — Purchase bill & payment ↔ ledger (C# cross-DB: Purchase + Common)
| code | check | source / target | sev | type |
|---|---|---|---|---|
| PUR-001 | Posted bill has exactly one transaction | `bill_headers`(posted) ↔ `transaction_headers`(source_type=2, `document_id`) | Critical | C# |
| PUR-002 | Bill total matches AP ledger leg | `bill_headers.total_amount` vs `SUM(journal C on bill.credit_account_id)` | Critical | C# |
| PUR-003 | Bill vendor matches transaction | `bill_headers.vendor_id` vs `transaction_headers.vendor_id` | Critical | C# |
| PUR-004 | Bill company matches transaction | `bill_headers.company_id` vs `transaction_headers.company_id` | Critical | C# |
| PUR-005 | Bill paid amount matches payments | `bill_headers.total_paid_amount` vs `SUM(bill_payment_details.paid_amount)` | High | SQL (Purchase only) |
| PUR-006 | Bill payment posted to ledger | `bill_payment_headers`(is_posted) ↔ `transaction_headers`(source_type=4) | Critical | C# |
| PUR-007 | No duplicate bill posting | >1 non-reversed source_type=2 txn for one `document_id` | Critical | C# |
| PUR-008 | Bill back-link populated | bill has Common txn but `bill.transaction_id IS NULL` | High | C# |
| PUR-009 | Vendor credit/debit note posted | `vendor_note_headers`(is_debit_note) ↔ `transaction_headers`(source_type 8/9) | High | C# |
| PUR-010 | TDS posting consistency | `bill_headers.tds_amount` > 0 ⇒ matching TDS payable ledger entry exists | High | C# |
| PUR-011 | Vendor advance posted & settled | `vendor_advances`/`vendor_advance_settlements` ↔ txn (source_type 12/15); `utilized_amount ≤ amount` | High | C# |

### 3.4 PAY — Payroll ↔ ledger (C# cross-DB: Payroll + Common)
| code | check | source / target | sev | type |
|---|---|---|---|---|
| PAY-001 | Posted salary run has transaction | `payroll_transaction_headers`(posted) ↔ `transaction_headers`(source_type=5, `document_id`) | Critical | C# |
| PAY-002 | Salary expense matches ledger | `payroll_transaction_headers.net_pay` vs salary-expense ledger posting | Critical | C# |
| PAY-003 | net_pay = basic + allowances − deductions | header arithmetic, and `SUM(payroll_transaction_details.amount)` consistency | High | SQL (Payroll only) |
| PAY-004 | Salary payment posted | `payment_headers`(is_posted) ↔ `transaction_headers`(source_type=6) | Critical | C# |
| PAY-005 | Payment detail rolls up | `SUM(payment_details.paid_amount)` = `payment_headers.paid_amount` | Medium | SQL (Payroll only) |
| PAY-006 | Salary payable cleared | posted salary minus paid = outstanding payable matches ledger balance | High | C# |
| PAY-007 | Employee advance posted/recovered | `advance_payments` ↔ txn (source_type 14/17) | Medium | C# |
| PAY-008 | Payroll back-link populated | salary run has Common txn but `transaction_id IS NULL` | High | C# |

### 3.5 COM — Party / master-data consistency (C# cross-DB)
| code | check | source / target | sev | type |
|---|---|---|---|---|
| COM-001 | Sales customer maps to a party | Common `customers.party_id IS NULL` (or Sales customer with no Common customer) | Medium | C# |
| COM-002 | Purchase vendor maps to a party | Common `vendors.party_id IS NULL` | Medium | C# |
| COM-003 | One GSTIN → one party | `parties` rows sharing `gstin` within an organization (dedup) | High | SQL (Common only) |
| COM-004 | Same GSTIN as customer & vendor → same party | a GSTIN that is both a customer and a vendor must resolve to the same `party_id` | High | C# |
| COM-005 | Document party exists in Common | `invoice_headers.customer_id` / `bill_headers.vendor_id` exists in Common `customers`/`vendors` | High | C# |
| COM-006 | Document company exists in Common | `*.company_id` exists in Common `companies` (and company has `account_id`) | High | C# |
| COM-007 | GSTIN format valid | `parties.gstin` / `companies.gstin` fails 15-char GSTIN regex when `has_gstin`/non-null | Low | SQL (Common only) |

### 3.6 INV — Product hierarchy & stock (same-DB SQL, Inventory only)
Hierarchy: `products` → `product_details` → `organization_products` → `company_products`.
| code | check | flags when | sev | type |
|---|---|---|---|---|
| INV-001 | Product has product_details | `products` with no `product_details` row | High | SQL |
| INV-002 | Product has organization_products | active product with no `organization_products` | High | SQL |
| INV-003 | Product has company_products | product used by a company with no `company_products` | High | SQL |
| INV-004 | Product has tax category | `products.tax_category_id` / `company_products.tax_category_id` null on GST item | Medium | SQL |
| INV-005 | Stock ledger references resolve | `stock_ledgers.reference_document_id` / `inventory_stock_ledger.source_id` with no parent doc | Medium | SQL/C# |
| INV-006 | Stock balance is non-negative | `stock_ledgers.balance_qty < 0` where `allow_negative_stock=false` | High | SQL |
| INV-007 | Vendor/customer-product mapping exists | mill-linked product with no `vendor_products` (`company_products.is_vendor_tracked`) | Medium | SQL |
| INV-008 | HSN/SAC present | stock item missing both `hsn_code` and `sac` | Low | SQL |

### 3.7 BNK — Bank & cross-cutting (C# cross-DB / Common)
| code | check | sev | type |
|---|---|---|---|
| BNK-001 | Bank transfer balanced | source_type=19 txn has equal debit/credit bank legs | Critical | SQL (Common only) |
| BNK-002 | Reconciliation matched amount integrity | `bank_reconciliations.matched_amount` matches its `ledger_entry_id` journal amount | High | C# |
| BNK-003 | Payment-gateway result ↔ ledger | `qa-dhanman-payment.payment_results` settled txn has a Common posting | High | C# |

> **Total: ~55 rules across 7 modules.** v0.1 ships the **bold** SAL + ACC set; the rest land in the
> phased plan (§5). Every "C#" rule above is an instance of the §2 template or a small variant of it.

---

## 4. Representative SQL (verified columns)

### ACC-001 — balanced journal per transaction (Common DB, same-DB SQL)
```sql
-- rules/accounting/ACC-001_journal_must_balance.sql
WITH bal AS (
  SELECT je.transaction_id,
         SUM(je.amount) FILTER (WHERE je.entry_type='D') AS dr,
         SUM(je.amount) FILTER (WHERE je.entry_type='C') AS cr
  FROM public.journal_entries je
  JOIN public.transaction_headers th ON th.id = je.transaction_id
  WHERE je.is_deleted=false AND th.is_deleted=false AND th.is_reversed=false
    AND th.transaction_date <= @ExecutionTimeUtc
    AND (@CompanyId::uuid IS NULL OR th.company_id = @CompanyId::uuid)
  GROUP BY je.transaction_id
)
SELECT 'ACC-001' rule_code, 'Accounting' module_name, 'LedgerIntegrity' category, 'Critical' severity,
       NULL::uuid organization_id, NULL::uuid company_id,
       'Transaction' source_type, transaction_id::text source_id, NULL::text source_number,
       'JournalEntry' related_type, NULL::text related_id, NULL::text related_number,
       'Journal entries for a transaction must have equal debit and credit totals.' message,
       dr::text expected_value, cr::text actual_value, (dr-cr) difference_value
FROM bal WHERE dr <> cr;
```

### SAL-005 — settled amount vs payments (Sales DB, same-DB SQL)
```sql
-- rules/sales/SAL-005_settled_amount_matches_payments.sql
WITH p AS (
  SELECT ih.id invoice_id, ih.invoice_number, ih.company_id, ih.settled_amount,
         COALESCE(SUM(ipd.received_amount),0) paid
  FROM public.invoice_headers ih
  LEFT JOIN public.invoice_payment_details ipd
         ON ipd.invoice_header_id = ih.id AND ipd.is_deleted=false
  WHERE ih.is_deleted=false AND ih.invoice_status_id IN (4,5)
    AND ih.created_on_utc <= @ExecutionTimeUtc
    AND (@CompanyId::uuid IS NULL OR ih.company_id=@CompanyId::uuid)
  GROUP BY ih.id, ih.invoice_number, ih.company_id, ih.settled_amount
)
SELECT 'SAL-005','Sales','SalesInvoiceLedger','High',
       NULL::uuid, company_id,
       'SalesInvoice', invoice_id::text, invoice_number,
       'InvoicePayment', NULL::text, NULL::text,
       'Invoice settled_amount must equal sum of payment detail received_amount.',
       settled_amount::text, paid::text, settled_amount - paid
FROM p WHERE settled_amount <> paid;
```

### Document→Ledger join shape used by all C# cross-DB rules
```sql
-- Common side, parameterized by source-type code and the batch's document ids
SELECT th.document_id, th.id AS txn_id, th.company_id, th.customer_id, th.vendor_id,
       th.is_reversed,
       SUM(je.amount) FILTER (WHERE je.entry_type='D') AS dr,
       SUM(je.amount) FILTER (WHERE je.entry_type='C') AS cr
FROM public.transaction_headers th
LEFT JOIN public.journal_entries je ON je.transaction_id = th.id AND je.is_deleted=false
WHERE th.transaction_source_type = @SourceType
  AND th.is_deleted = false
  AND th.document_id = ANY(@DocumentIds)      -- the keyset batch from the service DB
GROUP BY th.document_id, th.id, th.company_id, th.customer_id, th.vendor_id, th.is_reversed;
```

---

## 5. Phased rollout (rules)

- **v0.1** — engine + ACC-001..004, SAL-001..008, SAL-010/011. Proves the cross-DB template + same-DB SQL on the highest-value module. *(SAL-008 broken-back-link alone surfaces ~21k real records today.)*
- **v0.2** — Jenkins nightly, read-replica support, report publishing; add COM-001..006.
- **v0.3** — PUR-001..011 (bills/payments/notes/advances).
- **v0.4** — PAY-001..008 (payroll); INV-001..008 (product hierarchy & stock).
- **v0.5** — BNK-001..003; cross-service party dedup (COM-003/004); advance settlements.
- **v1.0** — Data Integrity Center UI: review / acknowledge / ignore / resolve; repair suggestions with manual approval (never auto-fix).

---

## 6. Connection & safety (verified context)
- Host `db.qa.dgtula.com:5432`, user `dhanmanqa` (QA). **The DB is NOT a replica** (`pg_is_in_recovery()=false`),
  so production-safety guards matter: read-only user, `statement_timeout`, `Application Name=DataTula`,
  keyset batching (5000), `cost_class`-driven night scheduling. (Prod handover notes a streaming replica exists on the new server — point `*ReadDb` there in prod.)
- Connection strings live in **Vault** at `secret/shared/databases` (KV v2 → `secret/data/shared/databases`),
  keys `ConnectionStrings__SalesDb`, `__CommonDb`, etc. AppRole `auth/approle/login`. The stored strings
  use `Server=127.0.0.1` (loopback on the DB host); from elsewhere substitute the real host.
- Diagnostics output goes only to the separate **`dhanman_diagnostics_qa` / `_prod`** DB (the only writable connection).
