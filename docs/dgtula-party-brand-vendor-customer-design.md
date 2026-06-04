# Dgtula / Dhanman Yarn Business Design

## Party, Customer, Vendor, Brand / Mill, Product and Settlement Design

This document explains how Dgtula should handle two important yarn business problems:

1. The same real-world person or business can be both a **Customer** and a **Vendor**.
2. A **Vendor**, **Brand / Mill**, and **Product** are different concepts and must be modeled separately.

The goal is to support practical yarn business behavior while keeping accounting, inventory, and microservice boundaries clean.

---

# Part 1: Same Party Can Be Both Customer and Vendor

## 1. Core Principle

A real-world business entity should be stored only once as a **Party**.

Customer and Vendor are not separate identities. They are **roles** of the same Party.

```text
One real-world entity
        |
        v
Common Service: parties
        |
        +----------------------+
        |                      |
        v                      v
Sales Service              Purchase Service
customers                  vendors
```

Example:

```text
Real-world entity: Shree Yarn Traders

Common Service
  parties
    party_id = P1
    name     = Shree Yarn Traders
    GSTIN    = 29ABCDE1234F1Z5

Sales Service
  customers
    customer_id = C1
    party_id    = P1

Purchase Service
  vendors
    vendor_id = V1
    party_id  = P1
```

This means:

```text
Same party_id = same real-world business
Different role records = different business behavior
```

---

## 2. Why This Is Required

In the yarn business:

```text
Scenario A:
You buy yarn from Vendor X.

Scenario B:
Later Vendor X urgently needs material and buys from you.

At that time:
Vendor X becomes your Customer.
```

If we maintain completely separate Customer and Vendor identities, we will face these problems:

```text
Duplicate GSTIN / PAN / mobile
Duplicate address and contact records
Difficult settlement
Wrong party-wise outstanding
Wrong common ledger
Hard reconciliation
Poor audit trail
```

So the correct design is:

```text
Party = identity
Customer = sales role
Vendor = purchase role
```

---

# 3. Recommended Table Ownership by Service

## 3.1 Common Service

The Common service owns the Party identity.

### Table: parties

```sql
parties
(
    id uuid primary key,
    organization_id uuid not null,
    party_name text not null,
    legal_name text null,
    gstin text null,
    pan text null,
    mobile_number text null,
    email text null,
    entity_type_id int null,
    is_active boolean not null default true,
    created_by uuid null,
    created_on_utc timestamp not null,
    modified_by uuid null,
    modified_on_utc timestamp null,
    deleted_by uuid null,
    deleted_on_utc timestamp null,
    is_deleted boolean not null default false
);
```

Suggested indexes:

```sql
create index ix_parties_organization_id on parties(organization_id);
create index ix_parties_gstin on parties(gstin);
create index ix_parties_pan on parties(pan);
create index ix_parties_mobile_number on parties(mobile_number);
```

Suggested duplicate check order:

```text
1. GSTIN exact match
2. PAN exact match
3. Mobile exact match
4. Email exact match
5. Party name fuzzy / normalized match
```

---

## 3.2 Sales Service

The Sales service owns Customer role behavior.

### Table: customers

```sql
customers
(
    id uuid primary key,
    party_id uuid not null,
    company_id uuid not null,
    customer_code text null,
    receivable_account_id uuid null,
    credit_limit numeric(15,2) null,
    credit_days int null,
    is_active boolean not null default true,
    created_by uuid null,
    created_on_utc timestamp not null,
    modified_by uuid null,
    modified_on_utc timestamp null,
    deleted_by uuid null,
    deleted_on_utc timestamp null,
    is_deleted boolean not null default false
);
```

Recommended uniqueness:

```sql
unique(company_id, party_id) where is_deleted = false
```

Meaning:

```text
The same party should not become duplicate customer for the same company.
```

---

## 3.3 Purchase Service

The Purchase service owns Vendor role behavior.

### Table: vendors

```sql
vendors
(
    id uuid primary key,
    party_id uuid not null,
    company_id uuid not null,
    vendor_code text null,
    payable_account_id uuid null,
    credit_days int null,
    legacy_mill_code int null,
    is_active boolean not null default true,
    created_by uuid null,
    created_on_utc timestamp not null,
    modified_by uuid null,
    modified_on_utc timestamp null,
    deleted_by uuid null,
    deleted_on_utc timestamp null,
    is_deleted boolean not null default false
);
```

Recommended uniqueness:

```sql
unique(company_id, party_id) where is_deleted = false
```

Meaning:

```text
The same party should not become duplicate vendor for the same company.
```

---

# 4. Event Flow 1: Create Customer Only

## Business Scenario

User creates a normal Customer. The customer is not treated as Vendor.

## ASCII Flow

```text
UI
 |
 | Create Customer form submitted
 | alsoCreateVendor = false
 v
Sales Service
 |
 | Check or create Party
 v
Common Service
 |
 | party exists?
 +--------------------------+
 |                          |
 | Yes                      | No
 v                          v
Return existing party_id    Create new party
 |                          |
 +------------+-------------+
              |
              v
Sales Service creates customer
              |
              v
Customer created with party_id
```

## Data Created

```text
Common Service
  parties
    created or reused

Sales Service
  customers
    created

Purchase Service
  vendors
    no entry
```

## Example Result

```text
parties
  id = P1
  name = ABC Textiles

customers
  id = C1
  party_id = P1
  company_id = CompanyA

vendors
  no row created
```

---

# 5. Event Flow 2: Create Customer and Also Treat as Vendor

## Business Scenario

At the time of creating Customer, user selects:

```text
[x] Also treat this party as Vendor
```

## Correct Behavior

Create the customer in Sales service and create the vendor role in Purchase service using the same `party_id`.

## ASCII Flow

```text
UI
 |
 | Create Customer form submitted
 | alsoCreateVendor = true
 v
Sales Service
 |
 | Check or create Party
 v
Common Service
 |
 | party exists?
 +--------------------------+
 |                          |
 | Yes                      | No
 v                          v
Return existing party_id    Create new party
 |                          |
 +------------+-------------+
              |
              v
Sales Service creates customer with party_id
              |
              v
UI or Sales Service calls Purchase Service
              |
              v
Purchase Service checks vendor by company_id + party_id
              |
              | vendor exists?
              +--------------------------+
              |                          |
              | Yes                      | No
              v                          v
              Return existing vendor      Create vendor using same party_id
              |                          |
              +------------+-------------+
                           |
                           v
Final result: same Party has Customer and Vendor roles
```

## Data Created

```text
Common Service
  parties
    P1 created or reused

Sales Service
  customers
    C1 created with party_id = P1

Purchase Service
  vendors
    V1 created with party_id = P1
```

## Example Result

```text
parties
  id = P1
  name = Shree Yarn Traders
  gstin = 29ABCDE1234F1Z5

customers
  id = C1
  party_id = P1
  company_id = CompanyA

vendors
  id = V1
  party_id = P1
  company_id = CompanyA
```

## Important Rule

```text
Do not create a second party.
Do not duplicate GSTIN/PAN/mobile.
Only create the missing role profile.
```

---

# 6. Event Flow 3: Create Vendor Only

## Business Scenario

User creates a normal Vendor. The vendor is not treated as Customer.

## ASCII Flow

```text
UI
 |
 | Create Vendor form submitted
 | alsoCreateCustomer = false
 v
Purchase Service
 |
 | Check or create Party
 v
Common Service
 |
 | party exists?
 +--------------------------+
 |                          |
 | Yes                      | No
 v                          v
Return existing party_id    Create new party
 |                          |
 +------------+-------------+
              |
              v
Purchase Service creates vendor
              |
              v
Vendor created with party_id
```

## Data Created

```text
Common Service
  parties
    created or reused

Purchase Service
  vendors
    created

Sales Service
  customers
    no entry
```

---

# 7. Event Flow 4: Create Vendor and Also Treat as Customer

## Business Scenario

At the time of creating Vendor, user selects:

```text
[x] Also treat this party as Customer
```

## ASCII Flow

```text
UI
 |
 | Create Vendor form submitted
 | alsoCreateCustomer = true
 v
Purchase Service
 |
 | Check or create Party
 v
Common Service
 |
 | party exists?
 +--------------------------+
 |                          |
 | Yes                      | No
 v                          v
Return existing party_id    Create new party
 |                          |
 +------------+-------------+
              |
              v
Purchase Service creates vendor with party_id
              |
              v
UI or Purchase Service calls Sales Service
              |
              v
Sales Service checks customer by company_id + party_id
              |
              | customer exists?
              +--------------------------+
              |                          |
              | Yes                      | No
              v                          v
              Return existing customer    Create customer using same party_id
              |                          |
              +------------+-------------+
                           |
                           v
Final result: same Party has Vendor and Customer roles
```

---

# 8. Event Flow 5: Existing Customer Later Becomes Vendor

## Business Scenario

Customer already exists. Later user wants to purchase from same party.

## ASCII Flow

```text
UI: Customer Details Screen
 |
 | User clicks: Also use as Vendor
 v
Sales Service reads customer
 |
 | customer.party_id = P1
 v
Purchase Service receives party_id = P1
 |
 | Check vendor by company_id + party_id
 +--------------------------+
 |                          |
 | Vendor exists            | Vendor not found
 v                          v
Return existing vendor      Create vendor with party_id = P1
 |                          |
 +------------+-------------+
              |
              v
Same party now has both roles
```

## Columns Used

```text
customers.party_id -> source identity
vendors.party_id   -> same identity
```

## API Suggestion

```http
POST /api/v1/vendors/from-party
```

Payload:

```json
{
  "companyId": "company-id",
  "partyId": "party-id",
  "vendorCode": "optional",
  "payableAccountId": "optional"
}
```

Response:

```json
{
  "vendorId": "vendor-id",
  "partyId": "party-id",
  "wasExisting": false
}
```

---

# 9. Event Flow 6: Existing Vendor Later Becomes Customer

## Business Scenario

Vendor already exists. Later user sells to same party.

## ASCII Flow

```text
UI: Vendor Details Screen
 |
 | User clicks: Also use as Customer
 v
Purchase Service reads vendor
 |
 | vendor.party_id = P1
 v
Sales Service receives party_id = P1
 |
 | Check customer by company_id + party_id
 +--------------------------+
 |                          |
 | Customer exists          | Customer not found
 v                          v
Return existing customer    Create customer with party_id = P1
 |                          |
 +------------+-------------+
              |
              v
Same party now has both roles
```

## API Suggestion

```http
POST /api/v1/customers/from-party
```

Payload:

```json
{
  "companyId": "company-id",
  "partyId": "party-id",
  "customerCode": "optional",
  "receivableAccountId": "optional"
}
```

---

# 10. Party Settlement / Net-off

## Business Scenario

The same Party is both Vendor and Customer.

```text
You bought from Party X:
  Purchase Bill payable = 100,000

You sold to Party X:
  Sales Invoice receivable = 70,000

Net payable after settlement = 30,000
```

## Correct Accounting

Original bill and invoice should remain unchanged.

A settlement voucher should adjust open balances.

```text
Dr Accounts Payable - Party X      70,000
    Cr Accounts Receivable - Party X      70,000
```

## ASCII Flow

```text
UI: Party Settlement Screen
 |
 | Select Company
 | Select Party
 v
System finds roles
 |
 +---------------------+
 |                     |
 v                     v
Vendor role found      Customer role found
 |
 v
Fetch open purchase bills
 |
 v
Fetch open sales invoices
 |
 v
User selects bills/invoices for adjustment
 |
 v
Create Settlement Voucher
 |
 v
Post journal entry
 |
 v
Update settlement allocations
 |
 v
Outstanding balances reduced
```

## Suggested Tables

### settlement_vouchers

```sql
settlement_vouchers
(
    id uuid primary key,
    company_id uuid not null,
    party_id uuid not null,
    vendor_id uuid null,
    customer_id uuid null,
    settlement_date date not null,
    voucher_number text not null,
    total_settlement_amount numeric(15,2) not null,
    narration text null,
    transaction_id bigint null,
    created_by uuid null,
    created_on_utc timestamp not null,
    is_deleted boolean not null default false
);
```

### settlement_allocations

```sql
settlement_allocations
(
    id uuid primary key,
    settlement_voucher_id uuid not null,
    source_document_type text not null,
    source_document_id uuid not null,
    source_role text not null,
    allocated_amount numeric(15,2) not null,
    created_on_utc timestamp not null
);
```

Example source roles:

```text
VENDOR_PAYABLE
CUSTOMER_RECEIVABLE
```

Example source document types:

```text
PURCHASE_BILL
SALES_INVOICE
```

---

# 11. Common Party Ledger

## Requirement

For a party that is both Customer and Vendor, user should see one combined ledger.

```text
Party: Shree Yarn Traders

+------------+----------------+----------------+----------+----------+
| Date       | Document       | Role           | Debit    | Credit   |
+------------+----------------+----------------+----------+----------+
| 01-Jun     | Purchase Bill  | Vendor         |          | 100,000  |
| 05-Jun     | Sales Invoice  | Customer       | 70,000   |          |
| 07-Jun     | Settlement     | Net-off        |          | 70,000   |
+------------+----------------+----------------+----------+----------+

Net Balance: 30,000 payable
```

## Service Responsibility

```text
Accounting / Reports Service
  uses party_id as common identity
  joins / fetches customer and vendor role records
  prepares common party ledger
```

---

# 12. Recommended API Design for Problem 1

## 12.1 Common Service

### Search or create party

```http
POST /api/v1/parties/resolve
```

Payload:

```json
{
  "organizationId": "organization-id",
  "partyName": "Shree Yarn Traders",
  "legalName": "Shree Yarn Traders Pvt Ltd",
  "gstin": "29ABCDE1234F1Z5",
  "pan": "ABCDE1234F",
  "mobileNumber": "9876543210",
  "email": "info@example.com"
}
```

Response:

```json
{
  "partyId": "party-id",
  "wasCreated": true,
  "matchedBy": "GSTIN"
}
```

---

## 12.2 Sales Service

### Create customer

```http
POST /api/v1/customers
```

Payload:

```json
{
  "companyId": "company-id",
  "partyId": "party-id",
  "customerCode": "optional",
  "creditLimit": 100000,
  "alsoCreateVendor": true
}
```

Better microservice-safe approach:

```text
Sales Service creates customer only.
UI or orchestration layer calls Purchase Service separately if alsoCreateVendor = true.
```

---

## 12.3 Purchase Service

### Create vendor from party

```http
POST /api/v1/vendors/from-party
```

Payload:

```json
{
  "companyId": "company-id",
  "partyId": "party-id",
  "vendorCode": "optional",
  "creditDays": 30,
  "payableAccountId": "optional"
}
```

---

# 13. Development Rules for Problem 1

```text
Rule 1:
Never create duplicate Party for the same real-world entity.

Rule 2:
Customer and Vendor are role profiles, not independent identities.

Rule 3:
Same real-world entity should share the same party_id across services.

Rule 4:
Before creating customer, check company_id + party_id.

Rule 5:
Before creating vendor, check company_id + party_id.

Rule 6:
Settlement should be through a voucher, not silent adjustment.

Rule 7:
Reports should support both role-wise ledger and common party ledger.
```

---

# Part 2: Brand / Mill, Vendor, Product and Inventory Relationship

# 14. Core Principle

Vendor, Brand / Mill, and Product are separate concepts.

```text
Vendor        = from whom we buy
Brand / Mill  = whose material it is / material origin
Product       = what item is traded
Party         = real-world business identity
```

Example:

```text
Vendor      : Shree Yarn Traders
Brand / Mill: Raymond
Product     : 40s Cotton Yarn
```

Another example:

```text
Vendor      : Raymond Ltd
Brand / Mill: Raymond
Product     : 40s Cotton Yarn
```

So sometimes Vendor and Brand point to the same Party, but they should still be separate role/master records.

---

# 15. Concept Model

```text
Common Service
  parties
     |
     | party_id
     v
Purchase Service
  vendors
     |
     | vendor_id
     v
Inventory Service
  vendor_products  <-------------+
     ^                            |
     |                            |
     | brand_id                   | product_id
     |                            |
Inventory Service          Inventory Service
  brands / mills             products
```

More complete:

```text
                        Common Service
                        parties
                           |
        +------------------+------------------+
        |                                     |
        v                                     v
Purchase Service                     Inventory Service
vendors                              brands / mills
vendor_id                            brand_id
party_id                             party_id nullable
        |                                     |
        |                                     |
        +------------------+------------------+
                           |
                           v
                  Inventory Service
                  vendor_products
                  vendor_id
                  product_id
                  brand_id
                           ^
                           |
                           |
                  Inventory Service
                  products
```

---

# 16. Brand / Mill Table

## Why Brand / Mill Should Be Separate

Do not add only this flag in vendor:

```text
vendors.is_brand = true
```

That is weak because:

```text
A brand may not be a vendor.
A vendor may sell many brands.
A brand may be supplied by many vendors.
Brand is needed in stock, purchase, sales and margin reports.
Brand may have legacy mill code.
Brand may have quality or manufacturer-specific details.
```

## Recommended Table: brands

```sql
brands
(
    id uuid primary key,
    organization_id uuid not null,
    party_id uuid null,
    brand_name text not null,
    brand_code text null,
    legacy_mill_code int null,
    brand_type_id int null,
    is_active boolean not null default true,
    created_by uuid null,
    created_on_utc timestamp not null,
    modified_by uuid null,
    modified_on_utc timestamp null,
    deleted_by uuid null,
    deleted_on_utc timestamp null,
    is_deleted boolean not null default false
);
```

Suggested brand types:

```text
1 = Brand
2 = Mill
3 = Manufacturer
4 = Trader Brand
```

For UI, label can be:

```text
Mill / Brand
```

---

# 17. Product Table

Product should represent the item identity.

```sql
products
(
    id uuid primary key,
    product_name text not null,
    product_code text null,
    hsn_code text null,
    base_unit_id uuid null,
    purchase_unit_id uuid null,
    sales_unit_id uuid null,
    product_type_id int null,
    is_stock_item boolean not null default true,
    is_active boolean not null default true,
    created_by uuid null,
    created_on_utc timestamp not null,
    modified_by uuid null,
    modified_on_utc timestamp null,
    is_deleted boolean not null default false
);
```

For yarn-specific attributes, avoid overloading products table too much. Prefer product details.

### product_details

```sql
product_details
(
    id uuid primary key,
    product_id uuid not null,
    count text null,
    ply text null,
    blend text null,
    shade text null,
    yarn_type text null,
    packing_type text null,
    cone_weight numeric(15,3) null,
    default_brand_id uuid null,
    created_on_utc timestamp not null
);
```

---

# 18. Vendor Product Mapping

Vendor product mapping is very important for yarn business and legacy migration.

## Why This Is Needed

One vendor can supply many products.

One vendor can supply multiple brands.

One brand can be supplied by multiple vendors.

The same product can come from different mills/brands.

```text
Vendor A sells Raymond 40s Cotton Yarn
Vendor B also sells Raymond 40s Cotton Yarn
Vendor A also sells Siyaram 40s Cotton Yarn
```

## Table: vendor_products

```sql
vendor_products
(
    id uuid primary key,
    company_id uuid not null,
    vendor_id uuid not null,
    product_id uuid not null,
    brand_id uuid null,
    vendor_product_name text null,
    vendor_item_code text null,
    legacy_item_code int null,
    legacy_mill_code int null,
    last_purchase_rate numeric(15,2) null,
    preferred_purchase_rate numeric(15,2) null,
    purchase_unit_id uuid null,
    packing_notes text null,
    quality_notes text null,
    is_preferred boolean not null default false,
    is_active boolean not null default true,
    created_by uuid null,
    created_on_utc timestamp not null,
    modified_by uuid null,
    modified_on_utc timestamp null,
    is_deleted boolean not null default false
);
```

Suggested uniqueness:

```sql
unique(company_id, vendor_id, product_id, brand_id) where is_deleted = false
```

If `brand_id` is nullable, handle uniqueness carefully in PostgreSQL. You may need either:

```text
1. Partial unique indexes
2. Use a default unknown brand
3. Make brand_id mandatory for yarn products
```

For yarn business, recommended:

```text
brand_id should be mandatory for yarn products when brand/mill is known.
```

---

# 19. Event Flow 7: Create Independent Brand / Mill

## Business Scenario

User creates a brand/mill that is not necessarily a vendor.

Example:

```text
Brand / Mill: Raymond
```

## ASCII Flow

```text
UI: Brand / Mill Master
 |
 | User enters brand name
 v
Inventory Service
 |
 | Is this brand also a real business party?
 +--------------------------+
 |                          |
 | No                       | Yes
 v                          v
Create brand                Search or create Party
party_id = null             in Common Service
 |                          |
 |                          v
 |                          Create brand with party_id
 |                          |
 +------------+-------------+
              |
              v
Brand / Mill created
```

## Data Created

### If brand is independent

```text
brands
  id = B1
  brand_name = Raymond
  party_id = null
```

### If brand is also a real business entity

```text
parties
  id = P1
  name = Raymond Ltd

brands
  id = B1
  brand_name = Raymond
  party_id = P1
```

---

# 20. Event Flow 8: Vendor Is Also Brand / Mill

## Business Scenario

Raymond Ltd is both:

```text
Vendor from whom we buy
Brand / Mill whose material it is
```

## ASCII Flow

```text
UI
 |
 | Create Vendor: Raymond Ltd
 | Also create as Brand / Mill = true
 v
Purchase Service
 |
 | Resolve Party
 v
Common Service
 |
 | Create or reuse party P1
 v
Purchase Service
 |
 | Create vendor V1 with party_id = P1
 v
Inventory Service
 |
 | Create brand B1 with party_id = P1
 v
Final Result
 |
 +--> Party P1
      +--> Vendor V1
      +--> Brand B1
```

## Correct Data

```text
parties
  id = P1
  name = Raymond Ltd

vendors
  id = V1
  party_id = P1

brands
  id = B1
  party_id = P1
  brand_name = Raymond
```

## Important Rule

```text
Do not store this only as vendors.is_brand.
Create a proper brand/mill row and optionally link it to party_id.
```

---

# 21. Event Flow 9: Vendor Supplies Product of a Brand

## Business Scenario

Vendor sells a product that belongs to a specific brand/mill.

Example:

```text
Vendor      : Shree Yarn Traders
Brand / Mill: Raymond
Product     : 40s Cotton Yarn
```

## ASCII Flow

```text
UI: Vendor Product Mapping Screen
 |
 | Select Vendor
 v
Purchase / Inventory lookup
 |
 | Select Product
 v
Inventory Service
 |
 | Select Brand / Mill
 v
Inventory Service
 |
 | Check vendor_products mapping
 +------------------------------+
 |                              |
 | Mapping exists               | Mapping not found
 v                              v
Update mapping                  Create mapping
 |                              |
 +--------------+---------------+
                |
                v
Vendor-product-brand relationship saved
```

## Data Created

```text
vendor_products
  vendor_id = V1
  product_id = PR1
  brand_id = B1
  legacy_item_code = 101
  legacy_mill_code = 5001
  last_purchase_rate = 240.00
```

---

# 22. Purchase Entry Flow with Brand / Mill

## Business Scenario

User creates a purchase bill for yarn.

## Header Fields

```text
Vendor
Bill Date
Bill Number
Purchase Account
Warehouse / Godown
Source of Supply
Transport
LR No
```

## Line Item Fields

```text
Product
Brand / Mill
Quantity
Unit
Rate
Amount
Lot / Bale / Cone details if applicable
Tax
```

## ASCII Flow

```text
UI: Purchase Bill
 |
 | Select Vendor
 | Select Product
 | Select Brand / Mill
 | Select Warehouse
 | Enter Qty and Rate
 v
Purchase Service
 |
 | Save purchase bill header and lines
 v
Inventory Service
 |
 | Create stock-in entry
 | product_id
 | brand_id
 | warehouse_id
 | quantity
 | rate
 v
Accounting
 |
 | Post journal entry
 v
Final Result
```

## Accounting Entry

```text
Dr Inventory / Purchase
Dr Input GST
    Cr Vendor Payable
```

## Inventory Entry

```text
stock_ledger
  product_id
  brand_id
  warehouse_id
  quantity_in
  rate
  source_document_type = PURCHASE_BILL
  source_document_id
```

---

# 23. Sales Entry Flow with Brand / Mill

## Business Scenario

User sells yarn to customer.

## Header Fields

```text
Customer
Invoice Date
Invoice Number
Sales Account
Warehouse / Godown
Transport
LR No
```

## Line Item Fields

```text
Product
Brand / Mill
Quantity
Unit
Rate
Amount
Lot / Bale / Cone details if applicable
Tax
```

## ASCII Flow

```text
UI: Sales Invoice
 |
 | Select Customer
 | Select Product
 | Select Brand / Mill
 | Select Warehouse
 | Enter Qty and Rate
 v
Sales Service
 |
 | Save sales invoice header and lines
 v
Inventory Service
 |
 | Create stock-out entry
 | product_id
 | brand_id
 | warehouse_id
 | quantity
 | cost reference
 v
Accounting
 |
 | Post journal entry
 v
Final Result
```

## Accounting Entry

```text
Dr Customer Receivable
    Cr Sales
    Cr Output GST
```

## Inventory Entry

```text
stock_ledger
  product_id
  brand_id
  warehouse_id
  quantity_out
  source_document_type = SALES_INVOICE
  source_document_id
```

---

# 24. Suggested Stock Ledger Columns

```sql
stock_ledger
(
    id uuid primary key,
    company_id uuid not null,
    organization_id uuid not null,
    product_id uuid not null,
    brand_id uuid null,
    warehouse_id uuid not null,
    transaction_date date not null,
    source_document_type text not null,
    source_document_id uuid not null,
    source_document_line_id uuid null,
    quantity_in numeric(15,3) not null default 0,
    quantity_out numeric(15,3) not null default 0,
    unit_id uuid null,
    rate numeric(15,2) null,
    amount numeric(15,2) null,
    lot_no text null,
    bale_no text null,
    cone_count numeric(15,3) null,
    created_by uuid null,
    created_on_utc timestamp not null
);
```

For yarn, `brand_id` should be captured in stock movement when known.

---

# 25. Reporting Requirements for Brand / Mill

The system should support:

```text
Brand-wise stock
Brand-wise purchase
Brand-wise sales
Brand-wise margin
Vendor-wise product mapping
Product-wise brand availability
Warehouse-wise brand stock
Lot-wise stock by brand
```

Example report:

```text
Brand-wise Stock

+------------+----------------+-----------+----------+
| Brand/Mill | Product        | Warehouse | Quantity |
+------------+----------------+-----------+----------+
| Raymond    | 40s Cotton Yarn| Main WH   | 500 KG   |
| Siyaram    | 40s Cotton Yarn| Main WH   | 300 KG   |
+------------+----------------+-----------+----------+
```

---

# 26. Development Rules for Problem 2

```text
Rule 1:
Do not model Brand / Mill only as a flag in vendors.

Rule 2:
Brand / Mill should be a separate inventory master.

Rule 3:
Brand may optionally link to common party using party_id.

Rule 4:
Vendor and Brand can point to the same party, but they are not the same table.

Rule 5:
Vendor-product-brand mapping should be stored in vendor_products.

Rule 6:
For yarn products, brand_id should be captured in purchase, sales and stock ledger.

Rule 7:
Legacy MillCode should not be forced only into products.

Rule 8:
Store legacy_item_code and legacy_mill_code in vendor_products where the legacy relationship exists.

Rule 9:
Reports must support brand-wise and vendor-wise analysis.
```

---

# 27. Summary of Final Recommended Design

## Problem 1: Same Party as Customer and Vendor

```text
Common Service
  parties
    party_id

Sales Service
  customers
    customer_id
    party_id

Purchase Service
  vendors
    vendor_id
    party_id
```

Correct behavior:

```text
If Customer is also Vendor:
  create vendor row with same party_id

If Vendor is also Customer:
  create customer row with same party_id
```

---

## Problem 2: Brand / Mill and Product Mapping

```text
Common Service
  parties

Purchase Service
  vendors
    party_id

Inventory Service
  brands
    party_id nullable

Inventory Service
  products

Inventory Service
  vendor_products
    vendor_id
    product_id
    brand_id
    legacy_item_code
    legacy_mill_code
```

Correct behavior:

```text
Vendor is from whom we buy.
Brand / Mill is whose material it is.
Product is what item is traded.
Party is the real-world identity.
```

---

# 28. Simple Developer Checklist

## For Party / Customer / Vendor

```text
[ ] Create parties table in Common service
[ ] Add party_id to customers
[ ] Add party_id to vendors
[ ] Add unique index on customers(company_id, party_id)
[ ] Add unique index on vendors(company_id, party_id)
[ ] Create /parties/resolve API
[ ] Create /vendors/from-party API
[ ] Create /customers/from-party API
[ ] Add checkbox: Also treat as Vendor
[ ] Add checkbox: Also treat as Customer
[ ] Add party settlement voucher
[ ] Add common party ledger report
```

## For Brand / Mill / Product

```text
[ ] Create brands / mills table
[ ] Add optional party_id to brands
[ ] Create vendor_products table
[ ] Add brand_id to purchase bill line where needed
[ ] Add brand_id to sales invoice line where needed
[ ] Add brand_id to stock ledger
[ ] Store legacy_item_code in vendor_products
[ ] Store legacy_mill_code in vendor_products
[ ] Build brand-wise stock report
[ ] Build brand-wise purchase report
[ ] Build brand-wise sales report
[ ] Build vendor-product-brand mapping screen
```

---

# 29. Final Rule to Remember

```text
Party = who the business is
Customer = sales role
Vendor = purchase role
Brand / Mill = material origin
Product = traded item
Vendor Product = commercial supply mapping
Stock Ledger = actual movement of product + brand + warehouse
Settlement Voucher = auditable net-off between payable and receivable
```

This design keeps Dgtula practical for yarn business and clean for accounting, inventory, reporting and future ERP expansion.
