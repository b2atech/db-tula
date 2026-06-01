import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { CheckCircle, XCircle, Clock, ChevronLeft, ChevronRight, Search } from 'lucide-react'
import { Card, CardContent } from '../components/ui/card'
import { Badge } from '../components/ui/badge'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { api } from '../api/client'
import type { RunStatus, Summary } from '../api/client'

const statusIcon = (status: RunStatus) => {
  if (status === 'Completed') return <CheckCircle className="h-4 w-4 text-green-500" />
  if (status === 'Failed') return <XCircle className="h-4 w-4 text-red-500" />
  if (status === 'Running') return <Clock className="h-4 w-4 text-blue-500 animate-spin" />
  return <Clock className="h-4 w-4 text-slate-400" />
}

const statusVariant = (status: RunStatus, hasDrift: boolean) => {
  if (status === 'Failed') return 'destructive' as const
  if (status === 'Running') return 'running' as const
  if (status === 'Pending') return 'secondary' as const
  return hasDrift ? 'warning' as const : 'success' as const
}

const DAY_RANGES = [
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 30 days', days: 30 },
  { label: 'All time', days: 0 },
]

export default function History() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [dayRange, setDayRange] = useState(0)

  const { data: runs = [], isLoading } = useQuery({
    queryKey: ['comparisons', page],
    queryFn: () => api.comparisons.list(page),
    refetchInterval: 5000,
  })

  const cutoff = dayRange > 0 ? Date.now() - dayRange * 86400000 : 0

  const filtered = runs.filter(r => {
    if (search && !`${r.profileName ?? ''} ${r.sourceDbName} ${r.targetDbName}`.toLowerCase().includes(search.toLowerCase())) return false
    if (statusFilter !== 'all' && r.status.toLowerCase() !== statusFilter) return false
    if (cutoff > 0 && new Date(r.startedAt).getTime() < cutoff) return false
    return true
  })

  const statuses = ['all', 'completed', 'failed', 'running', 'pending']

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900 dark:text-white">Run History</h1>
        <p className="text-slate-500 text-sm mt-1">All comparison runs, newest first</p>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-3 items-center">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-slate-400" />
          <Input
            placeholder="Search profiles, databases..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="pl-9 w-64"
          />
        </div>

        <div className="flex gap-1">
          {statuses.map(s => (
            <button key={s} onClick={() => setStatusFilter(s)}
              className={`px-3 py-1.5 rounded-lg text-xs font-medium capitalize transition-colors
                ${statusFilter === s ? 'bg-indigo-600 text-white' : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-50'}`}>
              {s}
            </button>
          ))}
        </div>

        <div className="flex gap-1 ml-auto">
          {DAY_RANGES.map(r => (
            <button key={r.days} onClick={() => setDayRange(r.days)}
              className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors
                ${dayRange === r.days ? 'bg-slate-700 text-white' : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-50'}`}>
              {r.label}
            </button>
          ))}
        </div>
      </div>

      <Card>
        <CardContent className="p-0">
          {isLoading && <p className="p-6 text-slate-400 text-sm">Loading...</p>}
          {!isLoading && filtered.length === 0 && (
            <p className="p-8 text-center text-slate-400 text-sm">No runs match your filters</p>
          )}
          {filtered.length > 0 && (
            <table className="w-full text-sm">
              <thead className="border-b border-slate-100 bg-slate-50">
                <tr>
                  {['', 'Profile / Databases', 'Status', 'Drift', 'Started', 'Duration', ''].map((h, i) => (
                    <th key={i} className="px-4 py-3 text-left text-xs font-medium text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map(r => {
                  const summary: Summary | null = r.summaryJson ? JSON.parse(r.summaryJson) : null
                  const drift = summary ? summary.mismatch + summary.missingInTarget + summary.missingInSource : 0
                  const hasDrift = drift > 0
                  const duration = r.completedAt
                    ? `${Math.round((new Date(r.completedAt).getTime() - new Date(r.startedAt).getTime()) / 1000)}s`
                    : '—'

                  return (
                    <tr key={r.id} className="hover:bg-slate-50 cursor-pointer"
                      onClick={() => r.status === 'Completed' && navigate(`/results/${r.id}`)}>
                      <td className="pl-4 py-3 w-8">{statusIcon(r.status)}</td>
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-900">{r.profileName ?? `${r.sourceDbName} → ${r.targetDbName}`}</p>
                        {r.profileName && <p className="text-xs text-slate-400">{r.sourceDbName} → {r.targetDbName}</p>}
                      </td>
                      <td className="px-4 py-3">
                        <Badge variant={statusVariant(r.status, hasDrift)}>{r.status}</Badge>
                      </td>
                      <td className="px-4 py-3">
                        {summary ? (hasDrift
                          ? <span className="text-amber-600 font-medium">{drift} issue{drift !== 1 ? 's' : ''}</span>
                          : <span className="text-green-600">Clean</span>
                        ) : '—'}
                      </td>
                      <td className="px-4 py-3 text-slate-500 text-xs">{new Date(r.startedAt).toLocaleString()}</td>
                      <td className="px-4 py-3 text-slate-400 text-xs">{duration}</td>
                      <td className="px-4 py-3">
                        {r.status === 'Completed' && (
                          <Button variant="ghost" size="sm" className="text-xs"
                            onClick={e => { e.stopPropagation(); navigate(`/results/${r.id}`) }}>
                            View →
                          </Button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      <div className="flex items-center justify-center gap-2">
        <Button variant="outline" size="sm" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}>
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <span className="text-sm text-slate-500">Page {page}</span>
        <Button variant="outline" size="sm" onClick={() => setPage(p => p + 1)} disabled={runs.length < 20}>
          <ChevronRight className="h-4 w-4" />
        </Button>
      </div>
    </div>
  )
}
