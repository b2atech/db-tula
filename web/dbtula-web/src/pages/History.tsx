import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { CheckCircle, XCircle, Clock, ChevronLeft, ChevronRight } from 'lucide-react'
import { Card, CardContent } from '../components/ui/card'
import { Badge } from '../components/ui/badge'
import { Button } from '../components/ui/button'
import { api, type RunStatus, type Summary } from '../api/client'

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

export default function History() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)

  const { data: runs = [], isLoading } = useQuery({
    queryKey: ['comparisons', page],
    queryFn: () => api.comparisons.list(page),
    refetchInterval: 5000,
  })

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Run History</h1>
        <p className="text-slate-500 text-sm mt-1">All comparison runs, newest first</p>
      </div>

      <Card>
        <CardContent className="p-0">
          {isLoading && <p className="p-6 text-slate-400 text-sm">Loading...</p>}
          {!isLoading && runs.length === 0 && (
            <p className="p-8 text-center text-slate-400 text-sm">No runs yet</p>
          )}
          <table className="w-full text-sm">
            {runs.length > 0 && (
              <thead className="border-b border-slate-100 bg-slate-50">
                <tr>
                  {['', 'Profile / Databases', 'Status', 'Drift', 'Started', 'Duration', ''].map((h, i) => (
                    <th key={i} className="px-4 py-3 text-left text-xs font-medium text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
            )}
            <tbody className="divide-y divide-slate-100">
              {runs.map(r => {
                const summary: Summary | null = r.summaryJson ? JSON.parse(r.summaryJson) : null
                const drift = summary ? summary.mismatch + summary.missingInTarget + summary.missingInSource : 0
                const hasDrift = drift > 0
                const duration = r.completedAt
                  ? `${Math.round((new Date(r.completedAt).getTime() - new Date(r.startedAt).getTime()) / 1000)}s`
                  : '—'

                return (
                  <tr
                    key={r.id}
                    className="hover:bg-slate-50 cursor-pointer"
                    onClick={() => r.status === 'Completed' && navigate(`/results/${r.id}`)}
                  >
                    <td className="pl-4 py-3 w-8">{statusIcon(r.status)}</td>
                    <td className="px-4 py-3">
                      {r.profileName
                        ? <p className="font-medium text-slate-900">{r.profileName}</p>
                        : <p className="font-medium text-slate-900">{r.sourceDbName} → {r.targetDbName}</p>
                      }
                      {r.profileName && (
                        <p className="text-xs text-slate-400">{r.sourceDbName} → {r.targetDbName}</p>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant={statusVariant(r.status, hasDrift)}>
                        {r.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      {summary ? (
                        hasDrift
                          ? <span className="text-amber-600 font-medium">{drift} issue{drift !== 1 ? 's' : ''}</span>
                          : <span className="text-green-600">Clean</span>
                      ) : '—'}
                    </td>
                    <td className="px-4 py-3 text-slate-500 text-xs">{new Date(r.startedAt).toLocaleString()}</td>
                    <td className="px-4 py-3 text-slate-400 text-xs">{duration}</td>
                    <td className="px-4 py-3">
                      {r.status === 'Completed' && (
                        <Button variant="ghost" size="sm" className="text-xs" onClick={e => { e.stopPropagation(); navigate(`/results/${r.id}`) }}>
                          View →
                        </Button>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
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
