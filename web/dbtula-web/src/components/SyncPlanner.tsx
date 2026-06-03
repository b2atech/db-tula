import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Zap, ChevronDown, ChevronRight, Eye, Copy, X } from 'lucide-react'
import { tokenStore } from '../api/client'

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
import { Badge } from './ui/badge'
import { Button } from './ui/button'
import { Checkbox } from './ui/checkbox'
import { api } from '../api/client'
import type { ApplySafeResult, SyncStatementItem } from '../api/client'

function StatementRow({
  stmt, selectable, checked, onToggle
}: {
  stmt: SyncStatementItem
  selectable: boolean
  checked: boolean
  onToggle?: (id: string, checked: boolean) => void
}) {
  const [expanded, setExpanded] = useState(false)
  return (
    <div className={`border-b border-slate-100 last:border-0 ${stmt.isApplied ? 'bg-green-50/40' : ''}`}>
      <div className="flex items-center gap-3 px-4 py-2 hover:bg-slate-50">
        {selectable && (
          <Checkbox
            checked={checked}
            onCheckedChange={v => onToggle?.(stmt.id, !!v)}
            disabled={stmt.isApplied}
          />
        )}
        <button className="shrink-0 text-slate-400" onClick={() => setExpanded(!expanded)}>
          {expanded ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
        </button>
        <span className="text-[11px] bg-slate-100 text-slate-600 px-1.5 py-0.5 rounded font-mono">{stmt.objectType}</span>
        <span className="text-xs font-medium text-slate-800 font-mono">{stmt.objectName}</span>
        <span className="text-[11px] text-slate-400 flex-1 truncate">{stmt.comment}</span>
        {stmt.isApplied && <Badge variant="success" className="text-[10px]">Applied</Badge>}
      </div>
      {expanded && (
        <pre className="mx-4 mb-2 px-3 py-2 bg-slate-900 text-emerald-300 text-[11px] rounded-lg overflow-x-auto">{stmt.sql.trim()}</pre>
      )}
    </div>
  )
}

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

  const { data: statements = [] } = useQuery({
    queryKey: ['statements', runId],
    queryFn: () => api.comparisons.statements(runId),
    enabled: hasSafe || hasRisky || hasDestructive,
  })

  // Pre-check all Safe statements on first load
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

  const [showPreview, setShowPreview] = useState(false)

  const safeStmts = statements.filter(s => s.category === 'Safe')
  const riskyStmts = statements.filter(s => s.category === 'Risky')
  const destructiveStmts = statements.filter(s => s.category === 'Destructive')
  const selectedCount = safeStmts.filter(s => checked.has(s.id)).length
  const appliedCount = safeStmts.filter(s => s.isApplied).length

  return (
    <div className="rounded-xl border border-slate-200 bg-white overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b bg-slate-50">
        <div className="flex items-center gap-3">
          <span className="font-semibold text-slate-800 text-sm">Sync Planner</span>
          {appliedCount > 0 && <Badge variant="success">{appliedCount} applied</Badge>}
        </div>
        <div className="flex items-center gap-2">
          {hasSafe && <Button variant="outline" size="sm" onClick={() => downloadScript(runId, 'safe')} className="gap-1 text-green-700 border-green-200 hover:bg-green-50 h-7 text-xs"><Download className="h-3 w-3" />Safe</Button>}
          {hasRisky && <Button variant="outline" size="sm" onClick={() => downloadScript(runId, 'risky')} className="gap-1 text-amber-700 border-amber-200 hover:bg-amber-50 h-7 text-xs"><Download className="h-3 w-3" />Risky</Button>}
          {hasDestructive && <Button variant="outline" size="sm" onClick={() => downloadScript(runId, 'destructive')} className="gap-1 text-red-700 border-red-200 hover:bg-red-50 h-7 text-xs"><Download className="h-3 w-3" />Destructive</Button>}
          {selectedCount > 0 && (
            <>
              <Button size="sm" variant="outline" onClick={() => setShowPreview(true)} className="gap-1 h-7 text-xs">
                <Eye className="h-3 w-3" /> Preview
              </Button>
              <Button size="sm" onClick={applySelected} disabled={applying} className="gap-1 h-7 text-xs bg-green-600 hover:bg-green-700">
                <Zap className="h-3 w-3" /> Apply {selectedCount} Safe
              </Button>
            </>
          )}
        </div>
      </div>

      {result && (
        <div className="px-4 py-2 bg-green-50 border-b text-xs text-green-700">
          ✅ {result.successCount} applied{result.failureCount > 0 && `, ❌ ${result.failureCount} failed`}
        </div>
      )}

      {/* Safe section */}
      {safeStmts.length > 0 && (
        <div>
          <div className="flex items-center gap-2 px-4 py-2 bg-green-50/60 border-b border-green-100">
            <Badge variant="success">SAFE</Badge>
            <span className="text-xs text-green-700">{safeStmts.length} statements — tick to include in apply</span>
            <button className="ml-auto text-xs text-slate-400 hover:text-slate-600"
              onClick={() => setChecked(prev => {
                const n = new Set(prev)
                const allChecked = safeStmts.filter(s => !s.isApplied).every(s => n.has(s.id))
                safeStmts.filter(s => !s.isApplied).forEach(s => allChecked ? n.delete(s.id) : n.add(s.id))
                return n
              })}>
              toggle all
            </button>
          </div>
          {safeStmts.map(s => (
            <StatementRow key={s.id} stmt={s} selectable checked={checked.has(s.id)} onToggle={toggle} />
          ))}
        </div>
      )}

      {/* Risky section — download only */}
      {riskyStmts.length > 0 && (
        <div>
          <div className="flex items-center gap-2 px-4 py-2 bg-amber-50/60 border-b border-t border-amber-100">
            <Badge variant="warning">RISKY</Badge>
            <span className="text-xs text-amber-700">{riskyStmts.length} statements — download only, test on a copy first</span>
          </div>
          {riskyStmts.map(s => (
            <StatementRow key={s.id} stmt={s} selectable={false} checked={false} />
          ))}
        </div>
      )}

      {/* Destructive section — download only */}
      {destructiveStmts.length > 0 && (
        <div>
          <div className="flex items-center gap-2 px-4 py-2 bg-red-50/60 border-b border-t border-red-100">
            <Badge variant="destructive">DESTRUCTIVE</Badge>
            <span className="text-xs text-red-700">{destructiveStmts.length} DROP statements — download only</span>
          </div>
          {destructiveStmts.map(s => (
            <StatementRow key={s.id} stmt={s} selectable={false} checked={false} />
          ))}
        </div>
      )}

      {statements.length === 0 && (hasSafe || hasRisky || hasDestructive) && (
        <p className="px-4 py-6 text-center text-slate-400 text-xs">Loading statements...</p>
      )}

      {!hasSafe && !hasRisky && !hasDestructive && (
        <p className="px-4 py-6 text-center text-green-600 text-sm font-medium">✅ No changes needed — schemas are in sync</p>
      )}

      {/* Preview modal */}
      {showPreview && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" onClick={() => setShowPreview(false)}>
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-3xl max-h-[80vh] flex flex-col" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between px-6 py-4 border-b">
              <h3 className="font-semibold text-slate-900">SQL Preview — {selectedCount} selected statements</h3>
              <div className="flex gap-2">
                <button
                  onClick={() => {
                    const sql = safeStmts.filter(s => checked.has(s.id)).map(s => s.sql).join('\n\n')
                    navigator.clipboard.writeText(sql)
                  }}
                  className="flex items-center gap-1 text-xs text-slate-500 hover:text-slate-800 border border-slate-200 rounded px-2 py-1"
                >
                  <Copy className="h-3 w-3" /> Copy
                </button>
                <button onClick={() => setShowPreview(false)} className="text-slate-400 hover:text-slate-700">
                  <X className="h-5 w-5" />
                </button>
              </div>
            </div>
            <pre className="flex-1 overflow-auto p-6 text-xs font-mono bg-slate-900 text-emerald-300 rounded-b-xl">
              {safeStmts.filter(s => checked.has(s.id)).map(s => `-- [${s.objectType}: ${s.objectName}] ${s.comment}\n${s.sql.trim()}`).join('\n\n')}
            </pre>
          </div>
        </div>
      )}
    </div>
  )
}
