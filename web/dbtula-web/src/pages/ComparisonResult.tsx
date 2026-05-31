import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import * as signalR from '@microsoft/signalr'
import { X, ChevronDown, ChevronRight } from 'lucide-react'
import { Card, CardContent } from '../components/ui/card'
import { Badge } from '../components/ui/badge'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '../components/ui/tabs'
import { api } from '../api/client'
import type { ComparisonResultItem, Summary } from '../api/client'
import { useAuth } from '../hooks/useAuth'
import { SyncPlanner } from '../components/SyncPlanner'

// ── Status helpers ────────────────────────────────────────────────────────────

const STATUS = {
  match:           { label: 'Match',            badge: 'success'     as const, dot: 'bg-green-500'  },
  mismatch:        { label: 'Mismatch',          badge: 'warning'     as const, dot: 'bg-amber-500'  },
  missingintarget: { label: 'Missing in Target', badge: 'destructive' as const, dot: 'bg-red-500'    },
  missinginsource: { label: 'Missing in Source', badge: 'secondary'   as const, dot: 'bg-orange-400' },
}

function statusKey(s: string) { return s.toLowerCase().replace(/\s/g, '') as keyof typeof STATUS }
function statusInfo(s: string) { return STATUS[statusKey(s)] ?? STATUS.mismatch }

// ── Object type grouping ──────────────────────────────────────────────────────

const TYPE_ORDER = ['table','function','procedure','view','trigger','sequence','enumType']
const TYPE_LABELS: Record<string, string> = {
  table: 'Tables', function: 'Functions', procedure: 'Procedures',
  view: 'Views', trigger: 'Triggers', sequence: 'Sequences', enumType: 'Enums'
}

// ── Diff Modal ────────────────────────────────────────────────────────────────

function DiffModal({ item, onClose }: { item: ComparisonResultItem; onClose: () => void }) {
  const [expanded, setExpanded] = useState<string | null>(null)
  const info = statusInfo(item.status)
  const hasSideBySide = !!item.sideBySideDiffHtml

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" onClick={onClose}>
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-5xl max-h-[90vh] flex flex-col" onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b">
          <div className="flex items-center gap-3">
            <span className="text-xs font-medium text-slate-500 uppercase">{item.objectType}</span>
            <span className="font-semibold text-slate-900 font-mono">{item.name}</span>
            <Badge variant={info.badge}>{info.label}</Badge>
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-700 rounded-lg p-1 hover:bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex-1 overflow-auto p-6 space-y-4">
          {/* Details */}
          {item.details && (
            <p className="text-sm text-slate-600 bg-slate-50 rounded-lg px-4 py-2">{item.details}</p>
          )}

          {/* Sub-results (for tables: columns, PKs, FKs, indexes) */}
          {item.subResults && item.subResults.length > 0 && (
            <div className="space-y-2">
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">Sub-component drift</p>
              {item.subResults.map((sub, i) => {
                const si = statusInfo(sub.status)
                return (
                  <div key={i} className="border border-slate-200 rounded-lg overflow-hidden">
                    <button
                      className="w-full flex items-center gap-3 px-4 py-2.5 hover:bg-slate-50 text-left"
                      onClick={() => setExpanded(expanded === `sub-${i}` ? null : `sub-${i}`)}
                    >
                      <div className={`h-2 w-2 rounded-full ${si.dot}`} />
                      <span className="text-sm font-medium text-slate-700 flex-1">{sub.component}</span>
                      <Badge variant={si.badge} className="text-xs">{si.label}</Badge>
                      {sub.details && (expanded === `sub-${i}` ? <ChevronDown className="h-3 w-3 text-slate-400" /> : <ChevronRight className="h-3 w-3 text-slate-400" />)}
                    </button>
                    {expanded === `sub-${i}` && sub.details && (
                      <div className="px-4 py-3 bg-slate-50 border-t text-xs font-mono text-slate-700 whitespace-pre-wrap">{sub.details}</div>
                    )}
                    {expanded === `sub-${i}` && sub.createScript && (
                      <pre className="px-4 py-3 bg-slate-900 text-green-400 text-xs overflow-auto">{sub.createScript}</pre>
                    )}
                  </div>
                )
              })}
            </div>
          )}

          {/* Side-by-side diff (functions, views, procedures) */}
          {hasSideBySide && (
            <div>
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">SQL diff</p>
              <div className="rounded-lg overflow-hidden border border-slate-200"
                dangerouslySetInnerHTML={{ __html: item.sideBySideDiffHtml! }} />
            </div>
          )}

          {/* Plain source/target (fallback) */}
          {!hasSideBySide && !item.subResults?.length && (item.sourceScript || item.targetScript) && (
            <div className="grid grid-cols-2 gap-4">
              {[['SOURCE', item.sourceScript], ['TARGET', item.targetScript]].map(([label, sql]) => (
                <div key={label as string}>
                  <p className="text-xs font-medium text-slate-500 mb-1">{label as string}</p>
                  <pre className="text-xs bg-slate-900 text-slate-100 p-3 rounded-lg overflow-auto max-h-60">{sql || '(not present)'}</pre>
                </div>
              ))}
            </div>
          )}

          {!hasSideBySide && !item.subResults?.length && !item.sourceScript && !item.targetScript && (
            <p className="text-slate-400 text-sm text-center py-4">No diff detail available for this object.</p>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Apply Safe Modal ──────────────────────────────────────────────────────────

// ── Object Type Results Table ─────────────────────────────────────────────────

function ResultTable({ items, onSelect }: { items: ComparisonResultItem[]; onSelect: (item: ComparisonResultItem) => void }) {
  const [filter, setFilter] = useState<string>('all')

  const filtered = filter === 'all' ? items : items.filter(r => statusKey(r.status) === filter)

  const counts = {
    all: items.length,
    match: items.filter(r => statusKey(r.status) === 'match').length,
    mismatch: items.filter(r => statusKey(r.status) === 'mismatch').length,
    missingintarget: items.filter(r => statusKey(r.status) === 'missingintarget').length,
    missinginsource: items.filter(r => statusKey(r.status) === 'missinginsource').length,
  }

  const chips = [
    { key: 'all',             label: `All (${counts.all})`,                       cls: 'bg-slate-100 text-slate-700' },
    { key: 'match',           label: `✅ Match (${counts.match})`,                 cls: 'bg-green-100 text-green-700' },
    { key: 'mismatch',        label: `⚠️ Mismatch (${counts.mismatch})`,           cls: 'bg-amber-100 text-amber-700' },
    { key: 'missingintarget', label: `❌ Missing in Target (${counts.missingintarget})`, cls: 'bg-red-100 text-red-700' },
    { key: 'missinginsource', label: `Missing in Source (${counts.missinginsource})`,    cls: 'bg-orange-100 text-orange-700' },
  ]

  return (
    <div>
      <div className="flex flex-wrap gap-2 mb-4">
        {chips.map(c => (
          <button key={c.key} onClick={() => setFilter(c.key)}
            className={`px-3 py-1 rounded-full text-xs font-medium transition-all ${c.cls} ${filter === c.key ? 'ring-2 ring-offset-1 ring-slate-400' : 'opacity-70 hover:opacity-100'}`}>
            {c.label}
          </button>
        ))}
      </div>

      <div className="rounded-xl border border-slate-200 overflow-hidden bg-white">
        <table className="w-full">
          <colgroup>
            <col style={{ width: '260px' }} />
            <col style={{ width: '130px' }} />
            <col style={{ width: '420px' }} />
            <col style={{ width: '60px'  }} />
            <col />  {/* spacer — eats remaining space */}
          </colgroup>
          <thead className="bg-slate-50 border-b border-slate-200">
            <tr>
              <th className="px-3 py-2 text-left text-[11px] font-semibold text-slate-500 uppercase tracking-wide">Name</th>
              <th className="px-3 py-2 text-left text-[11px] font-semibold text-slate-500 uppercase tracking-wide">Status</th>
              <th className="px-3 py-2 text-left text-[11px] font-semibold text-slate-500 uppercase tracking-wide">Details</th>
              <th className="px-3 py-2"></th>
              <th></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {filtered.map((r, i) => {
              const si = statusInfo(r.status)
              const sk = statusKey(r.status)
              return (
                <tr key={i}
                  className={`${sk !== 'match' ? 'cursor-pointer hover:bg-indigo-50/40' : 'hover:bg-slate-50/60'}`}
                  onClick={() => sk !== 'match' && onSelect(r)}>
                  <td className="px-3 py-2 font-mono text-xs font-medium text-slate-800">{r.name}</td>
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-1.5">
                      <div className={`h-1.5 w-1.5 rounded-full shrink-0 ${si.dot}`} />
                      <span className={`text-xs font-medium ${
                        sk === 'match'           ? 'text-green-600' :
                        sk === 'mismatch'        ? 'text-amber-600' :
                        sk === 'missingintarget' ? 'text-red-600'   : 'text-orange-600'
                      }`}>{si.label}</span>
                    </div>
                  </td>
                  <td className="px-3 py-2 text-xs text-slate-400 truncate">{r.details}</td>
                  <td className="px-3 py-2">
                    {sk !== 'match' && (
                      <button onClick={e => { e.stopPropagation(); onSelect(r) }}
                        className="text-[11px] text-indigo-500 hover:text-indigo-700 font-medium hover:underline whitespace-nowrap">
                        Diff →
                      </button>
                    )}
                  </td>
                  <td /> {/* spacer */}
                </tr>
              )
            })}
            {filtered.length === 0 && (
              <tr><td colSpan={5} className="py-6 text-center text-slate-400 text-xs">No items</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function ComparisonResult() {
  const { id } = useParams<{ id: string }>()
  const { user } = useAuth()
  const qc = useQueryClient()
  const [logs, setLogs] = useState<string[]>([])
  const [selectedItem, setSelectedItem] = useState<ComparisonResultItem | null>(null)
  const logsRef = useRef<HTMLDivElement>(null)

  const { data: run } = useQuery({
    queryKey: ['comparison', id],
    queryFn: () => api.comparisons.get(id!),
    refetchInterval: q => {
      const s = q.state.data?.status
      return s === 'Pending' || s === 'Running' ? 2000 : false
    },
  })

  useEffect(() => {
    if (!id || run?.status === 'Completed' || run?.status === 'Failed') return
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/comparison')
      .withAutomaticReconnect()
      .build()
    conn.start().then(() => conn.invoke('JoinRun', id)).catch(() => {})
    conn.on('progress', (msg: { message: string }) => {
      setLogs(prev => [...prev, msg.message])
      setTimeout(() => logsRef.current?.scrollTo(0, 99999), 50)
    })
    conn.on('done', () => {
      qc.invalidateQueries({ queryKey: ['comparison', id] })
      qc.invalidateQueries({ queryKey: ['comparisons'] })
    })
    return () => { conn.stop() }
  }, [id, run?.status, qc])

  const results: ComparisonResultItem[] = run?.resultJson ? JSON.parse(run.resultJson) : []
  const summary: Summary | null = run?.summaryJson ? JSON.parse(run.summaryJson) : null
  const isRunning = run?.status === 'Pending' || run?.status === 'Running'

  // Group by object type
  const byType: Record<string, ComparisonResultItem[]> = {}
  for (const r of results) {
    const t = r.objectType?.toLowerCase() ?? 'unknown'
    if (!byType[t]) byType[t] = []
    byType[t].push(r)
  }
  const types = TYPE_ORDER.filter(t => byType[t]?.length > 0)

  const summaryCards = [
    { label: 'Match',             value: summary?.match ?? 0,             cls: 'text-green-600 bg-green-50 border-green-200' },
    { label: 'Mismatch',          value: summary?.mismatch ?? 0,          cls: 'text-amber-600 bg-amber-50 border-amber-200' },
    { label: 'Missing in Target', value: summary?.missingInTarget ?? 0,   cls: 'text-red-600 bg-red-50 border-red-200' },
    { label: 'Missing in Source', value: summary?.missingInSource ?? 0,   cls: 'text-orange-600 bg-orange-50 border-orange-200' },
  ]

  return (
    <div className="space-y-5">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-slate-900">
          {run ? `${run.sourceDbName} → ${run.targetDbName}` : 'Loading...'}
        </h1>
        {run && (
          <p className="text-slate-500 text-sm mt-0.5">
            {run.profileName && <span className="mr-2 text-indigo-600 font-medium">{run.profileName}</span>}
            Started {new Date(run.startedAt).toLocaleString()}
            {run.completedAt && ` · ${Math.round((new Date(run.completedAt).getTime() - new Date(run.startedAt).getTime()) / 1000)}s`}
          </p>
        )}
      </div>

      {/* Live log */}
      {(isRunning || logs.length > 0) && (
        <div ref={logsRef} className="bg-slate-900 text-emerald-400 rounded-xl p-4 font-mono text-xs h-44 overflow-auto">
          {logs.map((l, i) => <div key={i}>{l}</div>)}
          {isRunning && <div className="animate-pulse mt-1">▋</div>}
        </div>
      )}

      {/* Summary cards */}
      {summary && (
        <div className="flex items-center gap-3 flex-wrap">
          {summaryCards.map(c => (
            <div key={c.label} className={`rounded-xl border px-5 py-3 text-center min-w-[120px] ${c.cls}`}>
              <div className="text-2xl font-bold">{c.value}</div>
              <div className="text-xs mt-0.5 font-medium">{c.label}</div>
            </div>
          ))}
        </div>
      )}

      {/* Tabs by object type */}
      {types.length > 0 && (
        <Tabs defaultValue={types[0]}>
          <TabsList className="flex-wrap h-auto gap-1">
            {types.map(t => {
              const items = byType[t]
              const hasDrift = items.some(r => statusKey(r.status) !== 'match')
              return (
                <TabsTrigger key={t} value={t} className="gap-1.5">
                  {TYPE_LABELS[t] ?? t}
                  <span className={`ml-1 rounded-full px-1.5 py-0.5 text-[10px] font-bold ${hasDrift ? 'bg-red-100 text-red-600' : 'bg-green-100 text-green-600'}`}>
                    {items.length}
                  </span>
                </TabsTrigger>
              )
            })}
          </TabsList>
          {types.map(t => (
            <TabsContent key={t} value={t}>
              <ResultTable items={byType[t]} onSelect={setSelectedItem} />
            </TabsContent>
          ))}
        </Tabs>
      )}

      {/* Sync Planner — statement-level approve/skip */}
      {run?.status === 'Completed' && user?.role === 'Admin' && (
        <SyncPlanner
          runId={id!}
          hasSafe={!!run.hasSafeScript}
          hasRisky={!!run.hasRiskyScript}
          hasDestructive={!!run.hasDestructiveScript}
          syncScriptUrl={api.comparisons.syncScriptUrl}
        />
      )}

      {run?.status === 'Failed' && (
        <Card>
          <CardContent className="p-6 text-red-600">
            <p className="font-medium">Comparison failed</p>
            <p className="text-sm mt-1">{run.errorMessage}</p>
          </CardContent>
        </Card>
      )}

      {selectedItem && <DiffModal item={selectedItem} onClose={() => setSelectedItem(null)} />}
    </div>
  )
}
