import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from 'recharts'
import { Database, GitCompare, Play, CheckCircle, AlertTriangle, XCircle, Clock, Activity } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '../components/ui/card'
import { Badge } from '../components/ui/badge'
import { Button } from '../components/ui/button'
import { api, type DbHealth, type Summary, type ProfileDriftSeries } from '../api/client'

// 10 distinct colours for up to 10 service lines
const LINE_COLORS = ['#6366f1','#f59e0b','#10b981','#ef4444','#3b82f6','#8b5cf6','#ec4899','#14b8a6','#f97316','#84cc16']

function ProfileDriftChart({ series }: { series: ProfileDriftSeries[] }) {
  // Merge all series into one flat array keyed by date
  const allDates = [...new Set(series.flatMap(s => s.points.map(p => p.date)))].sort()
  const data = allDates.map(date => {
    const row: Record<string, string | number> = { date }
    series.forEach(s => {
      const pt = s.points.find(p => p.date === date)
      // Strip "QA vs PROD · " prefix for shorter legend labels
      const label = s.profile.replace('QA vs PROD · ', '')
      row[label] = pt?.drift ?? 0
    })
    return row
  })
  const labels = series.map(s => s.profile.replace('QA vs PROD · ', ''))

  return (
    <ResponsiveContainer width="100%" height={220}>
      <LineChart data={data} margin={{ top: 4, right: 4, left: -20, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
        <XAxis dataKey="date" tick={{ fontSize: 10, fill: '#94a3b8' }} tickFormatter={d => d.slice(5)} />
        <YAxis tick={{ fontSize: 10, fill: '#94a3b8' }} allowDecimals={false} />
        <Tooltip
          contentStyle={{ borderRadius: 8, border: '1px solid #e2e8f0', fontSize: 11 }}
          labelFormatter={l => `Date: ${l}`}
        />
        <Legend iconSize={8} wrapperStyle={{ fontSize: 11 }} />
        {labels.map((label, i) => (
          <Line
            key={label}
            type="monotone"
            dataKey={label}
            stroke={LINE_COLORS[i % LINE_COLORS.length]}
            strokeWidth={1.5}
            dot={false}
            activeDot={{ r: 3 }}
          />
        ))}
      </LineChart>
    </ResponsiveContainer>
  )
}

function StatCard({ label, value, sub, icon: Icon, color }: {
  label: string; value: string | number; sub?: string;
  icon: React.ElementType; color: string
}) {
  return (
    <Card>
      <CardContent className="p-6">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-sm font-medium text-slate-500">{label}</p>
            <p className="text-3xl font-bold text-slate-900 mt-1">{value}</p>
            {sub && <p className="text-xs text-slate-400 mt-1">{sub}</p>}
          </div>
          <div className={`rounded-lg p-2.5 ${color}`}>
            <Icon className="h-5 w-5 text-white" />
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function HealthCard({ health, onRun }: { health: DbHealth; onRun: () => void }) {
  const statusMap = {
    Healthy: { badge: 'success' as const, dot: 'bg-green-500', text: 'No drift' },
    Drift: { badge: 'destructive' as const, dot: 'bg-red-500', text: `${health.totalDrift} issue${health.totalDrift !== 1 ? 's' : ''}` },
    Unknown: { badge: 'secondary' as const, dot: 'bg-slate-400', text: 'Never run' },
  }
  const s = statusMap[health.status]

  return (
    <Card className="hover:shadow-md transition-shadow">
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-2 mb-3">
          <div className="flex-1 min-w-0">
            <p className="font-medium text-slate-900 text-sm truncate">{health.profileName}</p>
            <p className="text-xs text-slate-400 mt-0.5 truncate">{health.sourceDb} → {health.targetDb}</p>
          </div>
          <div className="flex items-center gap-1.5 shrink-0">
            <div className={`h-2 w-2 rounded-full ${s.dot}`} />
            <Badge variant={s.badge} className="text-xs">{s.text}</Badge>
          </div>
        </div>
        <div className="flex items-center justify-between">
          <p className="text-xs text-slate-400">
            {health.lastRunAt ? `Last run ${new Date(health.lastRunAt).toLocaleDateString()}` : 'No runs yet'}
          </p>
          <Button size="sm" variant="outline" onClick={onRun} className="h-7 text-xs gap-1">
            <Play className="h-3 w-3" /> Run
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

export default function Dashboard() {
  const navigate = useNavigate()
  const qc = useQueryClient()

  const { data: summary } = useQuery({ queryKey: ['metrics-summary'], queryFn: api.metrics.summary })
  const { data: profileTrend = [] } = useQuery({ queryKey: ['drift-trend-by-profile'], queryFn: () => api.metrics.driftTrendByProfile(30) })
  const { data: health = [] } = useQuery({
    queryKey: ['db-health'], queryFn: api.metrics.dbHealth, refetchInterval: 30000
  })
  const { data: recentRuns = [] } = useQuery({
    queryKey: ['comparisons'], queryFn: () => api.comparisons.list(1), refetchInterval: 5000
  })

  const runProfile = async (profileId: string) => {
    const { runId } = await api.profiles.run(profileId)
    qc.invalidateQueries({ queryKey: ['comparisons'] })
    navigate(`/results/${runId}`)
  }

  const statusBadgeVariant = (status: string) => {
    if (status === 'Completed') return 'success' as const
    if (status === 'Failed') return 'destructive' as const
    if (status === 'Running') return 'running' as const
    return 'secondary' as const
  }

  const driftRate = summary && summary.totalRuns30d > 0
    ? Math.round((summary.driftRuns30d / summary.totalRuns30d) * 100)
    : 0

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Dashboard</h1>
        <p className="text-slate-500 text-sm mt-1">Schema drift monitoring across all environments</p>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-4 gap-4">
        <StatCard label="Runs (30 days)" value={summary?.totalRuns30d ?? '—'} icon={Activity} color="bg-indigo-500" />
        <StatCard label="Drift Rate" value={`${driftRate}%`} sub={`${summary?.driftRuns30d ?? 0} runs with drift`} icon={AlertTriangle} color="bg-amber-500" />
        <StatCard label="Statements Applied" value={summary?.statementsApplied ?? '—'} icon={CheckCircle} color="bg-green-500" />
        <StatCard label="Databases Registered" value={summary?.dbsRegistered ?? '—'} icon={Database} color="bg-slate-600" />
      </div>

      <div className="grid grid-cols-3 gap-6">
        {/* Per-Profile Drift Trend Chart */}
        <Card className="col-span-2">
          <CardHeader>
            <CardTitle className="text-base">Drift Trend by Service</CardTitle>
            <CardDescription>Daily drift count per Dhanman service — last 30 days</CardDescription>
          </CardHeader>
          <CardContent>
            {profileTrend.length === 0 ? (
              <div className="h-48 flex items-center justify-center text-slate-400 text-sm">
                No data yet — run some comparisons first
              </div>
            ) : (
              <ProfileDriftChart series={profileTrend} />
            )}
          </CardContent>
        </Card>

        {/* Recent Activity */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Recent Runs</CardTitle>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y divide-slate-100">
              {recentRuns.slice(0, 6).map(r => {
                const summary: Summary | null = r.summaryJson ? JSON.parse(r.summaryJson) : null
                const hasDrift = summary && (summary.mismatch + summary.missingInTarget + summary.missingInSource > 0)
                return (
                  <div
                    key={r.id}
                    className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50 cursor-pointer"
                    onClick={() => r.status === 'Completed' && navigate(`/results/${r.id}`)}
                  >
                    {r.status === 'Completed' && !hasDrift && <CheckCircle className="h-4 w-4 text-green-500 shrink-0" />}
                    {r.status === 'Completed' && hasDrift && <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0" />}
                    {r.status === 'Failed' && <XCircle className="h-4 w-4 text-red-500 shrink-0" />}
                    {(r.status === 'Pending' || r.status === 'Running') && <Clock className="h-4 w-4 text-blue-500 shrink-0 animate-spin" />}
                    <div className="flex-1 min-w-0">
                      <p className="text-xs font-medium text-slate-800 truncate">
                        {r.profileName ?? `${r.sourceDbName} → ${r.targetDbName}`}
                      </p>
                      <p className="text-xs text-slate-400">{new Date(r.startedAt).toLocaleString()}</p>
                    </div>
                    <Badge variant={statusBadgeVariant(r.status)} className="text-xs shrink-0">{r.status}</Badge>
                  </div>
                )
              })}
              {recentRuns.length === 0 && (
                <p className="px-4 py-8 text-center text-sm text-slate-400">No runs yet</p>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* DB Health Grid */}
      {health.length > 0 && (
        <div>
          <div className="flex items-center gap-2 mb-3">
            <GitCompare className="h-4 w-4 text-slate-500" />
            <h2 className="text-sm font-semibold text-slate-700">Database Health</h2>
            <span className="text-xs text-slate-400">({health.length} profiles)</span>
          </div>
          <div className="grid grid-cols-3 gap-3">
            {health.map(h => (
              <HealthCard
                key={h.profileId}
                health={h}
                onRun={() => runProfile(h.profileId)}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
