import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Zap, ChevronDown, ChevronRight, Copy, X, Download, CheckSquare, Square } from 'lucide-react'
import { Badge } from './ui/badge'
import { Button } from './ui/button'
import { Checkbox } from './ui/checkbox'
import { api, tokenStore } from '../api/client'
import type { ApplySafeResult, SyncStatementItem } from '../api/client'

// ── Authenticated file download ───────────────────────────────────────────────
async function downloadScript(runId: string, category: string) {
  const token = tokenStore.get()
  const res = await fetch(`/api/comparisons/${runId}/sync-script?category=${category}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {}
  })
  if (!res.ok) return
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `sync-${category}-${runId.slice(0, 8)}.sql`
  a.click()
  URL.revokeObjectURL(url)
}

// ── Individual statement row ──────────────────────────────────────────────────
function StatementRow({ stmt, checked, onToggle }: {
  stmt: SyncStatementItem
  checked: boolean
  onToggle: (id: string, val: boolean) => void
}) {
  const [expanded, setExpanded] = useState(false)

  return (
    <div className={`border-b border-slate-100 last:border-0 ${stmt.isApplied ? 'bg-green-50/50' : ''}`}>
      <div className="flex items-center gap-2 px-3 py-1.5 hover:bg-slate-50">
        <Checkbox
          checked={checked}
          onCheckedChange={v => onToggle(stmt.id, !!v)}
          disabled={stmt.isApplied}
          className="shrink-0"
        />
        <button className="shrink-0 text-slate-300 hover:text-slate-500"
          onClick={() => setExpanded(!expanded)}>
          {expanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
        </button>
        <div className="flex-1 min-w-0">
          <span className="text-[10px] bg-slate-100 text-slate-500 px-1 rounded mr-1">{stmt.objectType}</span>
          <span className="text-[11px] font-mono font-medium text-slate-800 truncate">{stmt.objectName}</span>
        </div>
        {stmt.isApplied && <span className="text-[9px] text-green-600 font-semibold">✓</span>}
      </div>
      {expanded && (
        <pre className="mx-3 mb-2 px-2 py-1.5 bg-slate-900 text-emerald-300 text-[10px] rounded overflow-x-auto">
          {stmt.sql.trim()}
        </pre>
      )}
    </div>
  )
}

// ── Preview modal ─────────────────────────────────────────────────────────────
function PreviewModal({ stmts, checked, onClose }: {
  stmts: SyncStatementItem[]
  checked: Set<string>
  onClose: () => void
}) {
  const selected = stmts.filter(s => checked.has(s.id))
  const sql = selected.map(s => `-- [${s.objectType}: ${s.objectName}]\n${s.sql.trim()}`).join('\n\n')

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" onClick={onClose}>
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[80vh] flex flex-col" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-3 border-b">
          <h3 className="font-semibold text-slate-900 text-sm">{selected.length} Safe statements to apply</h3>
          <div className="flex gap-2">
            <button onClick={() => navigator.clipboard.writeText(sql)}
              className="flex items-center gap-1 text-xs text-slate-500 hover:text-slate-800 border border-slate-200 rounded px-2 py-1">
              <Copy className="h-3 w-3" /> Copy
            </button>
            <button onClick={onClose} className="text-slate-400 hover:text-slate-700">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
        <pre className="flex-1 overflow-auto p-4 text-[11px] font-mono bg-slate-900 text-emerald-300 rounded-b-xl">
          {sql}
        </pre>
      </div>
    </div>
  )
}

// ── Main SyncPlanner sidebar ──────────────────────────────────────────────────
export function SyncPlanner({ runId, hasSafe, hasRisky, hasDestructive }: {
  runId: string
  hasSafe: boolean
  hasRisky: boolean
  hasDestructive: boolean
}) {
  const qc = useQueryClient()
  const [applying, setApplying] = useState(false)
  const [result, setResult] = useState<ApplySafeResult | null>(null)
  const [checked, setChecked] = useState<Set<string>>(new Set())
  const [initialised, setInitialised] = useState(false)
  const [showPreview, setShowPreview] = useState(false)

  const { data: statements = [] } = useQuery({
    queryKey: ['statements', runId],
    queryFn: () => api.comparisons.statements(runId),
    enabled: hasSafe || hasRisky || hasDestructive,
  })

  if (!initialised && statements.length > 0) {
    setChecked(new Set(statements.filter(s => s.category === 'Safe' && !s.isApplied).map(s => s.id)))
    setInitialised(true)
  }

  const toggle = async (id: string, val: boolean) => {
    setChecked(prev => { const n = new Set(prev); val ? n.add(id) : n.delete(id); return n })
    await api.comparisons.toggleStatement(runId, id, val)
  }

  const applySelected = async () => {
    setApplying(true)
    try {
      const r = await api.comparisons.applyApproved(runId)
      setResult(r)
      qc.invalidateQueries({ queryKey: ['statements', runId] })
    } finally { setApplying(false) }
  }

  const safeStmts = statements.filter(s => s.category === 'Safe')
  const selectedCount = safeStmts.filter(s => checked.has(s.id)).length
  const appliedCount = safeStmts.filter(s => s.isApplied).length
  const allSelected = safeStmts.filter(s => !s.isApplied).every(s => checked.has(s.id))

  const toggleAll = () => {
    const unapplied = safeStmts.filter(s => !s.isApplied)
    setChecked(prev => {
      const n = new Set(prev)
      allSelected ? unapplied.forEach(s => n.delete(s.id)) : unapplied.forEach(s => n.add(s.id))
      return n
    })
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-white overflow-hidden sticky top-6">
      {/* Header */}
      <div className="px-4 py-3 border-b bg-slate-50 flex items-center justify-between">
        <span className="font-semibold text-slate-800 text-sm">Sync Changes</span>
        {appliedCount > 0 && <Badge variant="success" className="text-xs">{appliedCount} applied</Badge>}
      </div>

      {/* ── SAFE section ── */}
      {hasSafe && (
        <div>
          {/* Safe header */}
          <div className="flex items-center justify-between px-3 py-2 bg-green-50 border-b border-green-100">
            <div className="flex items-center gap-1.5">
              <span className="text-xs font-semibold text-green-700">SAFE</span>
              <span className="text-[10px] text-green-600">({safeStmts.length} statements)</span>
            </div>
            <button onClick={toggleAll} className="text-[10px] text-green-600 hover:text-green-800 flex items-center gap-0.5">
              {allSelected ? <CheckSquare className="h-3 w-3" /> : <Square className="h-3 w-3" />}
              {allSelected ? 'deselect all' : 'select all'}
            </button>
          </div>

          {/* Statement list — scrollable */}
          <div className="max-h-64 overflow-y-auto">
            {safeStmts.map(s => (
              <StatementRow key={s.id} stmt={s} checked={checked.has(s.id)} onToggle={toggle} />
            ))}
            {safeStmts.length === 0 && (
              <p className="px-3 py-4 text-center text-slate-400 text-xs">Loading...</p>
            )}
          </div>

          {/* Apply result */}
          {result && (
            <div className="px-3 py-2 bg-green-50 border-t text-xs text-green-700">
              ✅ {result.successCount} applied
              {result.failureCount > 0 && <span className="text-red-600 ml-2">❌ {result.failureCount} failed</span>}
            </div>
          )}

          {/* Safe actions */}
          <div className="px-3 py-2.5 border-t flex gap-2">
            <Button size="sm" variant="outline" onClick={() => setShowPreview(true)}
              disabled={selectedCount === 0}
              className="flex-1 text-xs h-7 gap-1">
              Preview {selectedCount > 0 ? selectedCount : ''}
            </Button>
            <Button size="sm" onClick={applySelected}
              disabled={applying || selectedCount === 0}
              className="flex-1 text-xs h-7 gap-1 bg-green-600 hover:bg-green-700">
              <Zap className="h-3 w-3" />
              {applying ? 'Applying...' : `Apply ${selectedCount}`}
            </Button>
          </div>
        </div>
      )}

      {/* ── RISKY section — download only ── */}
      {hasRisky && (
        <div className="border-t">
          <div className="px-3 py-2 bg-amber-50 border-b border-amber-100 flex items-center justify-between">
            <div>
              <span className="text-xs font-semibold text-amber-700">RISKY</span>
              <p className="text-[10px] text-amber-600 mt-0.5">Test on a copy first</p>
            </div>
            <Button size="sm" variant="outline" onClick={() => downloadScript(runId, 'risky')}
              className="h-7 text-xs gap-1 text-amber-700 border-amber-300 hover:bg-amber-50">
              <Download className="h-3 w-3" /> Download
            </Button>
          </div>
        </div>
      )}

      {/* ── DESTRUCTIVE section — download only ── */}
      {hasDestructive && (
        <div className="border-t">
          <div className="px-3 py-2 bg-red-50 border-b border-red-100 flex items-center justify-between">
            <div>
              <span className="text-xs font-semibold text-red-700">DESTRUCTIVE</span>
              <p className="text-[10px] text-red-600 mt-0.5">Irreversible — back up first</p>
            </div>
            <Button size="sm" variant="outline" onClick={() => downloadScript(runId, 'destructive')}
              className="h-7 text-xs gap-1 text-red-700 border-red-300 hover:bg-red-50">
              <Download className="h-3 w-3" /> Download
            </Button>
          </div>
        </div>
      )}

      {!hasSafe && !hasRisky && !hasDestructive && (
        <p className="px-4 py-6 text-center text-green-600 text-xs font-medium">
          ✅ Schemas are in sync — no changes needed
        </p>
      )}

      {showPreview && (
        <PreviewModal stmts={safeStmts} checked={checked} onClose={() => setShowPreview(false)} />
      )}
    </div>
  )
}
