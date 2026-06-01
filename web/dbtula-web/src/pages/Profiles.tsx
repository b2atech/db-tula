import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { Plus, Play, Pencil, Trash2, Clock } from 'lucide-react'
import { Card, CardContent } from '../components/ui/card'
import { Button } from '../components/ui/button'
import { Badge } from '../components/ui/badge'
import { Input } from '../components/ui/input'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '../components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select'
import { api, type Profile, type CreateProfileRequest } from '../api/client'

function ProfileForm({
  initial, onSave, onClose
}: {
  initial?: Profile
  onSave: (req: CreateProfileRequest) => Promise<void>
  onClose: () => void
}) {
  const { data: dbs = [] } = useQuery({ queryKey: ['databases'], queryFn: api.databases.list })
  const readDbs = dbs.filter(d => !d.isWriteAccount)
  const [form, setForm] = useState<CreateProfileRequest>({
    name: initial?.name ?? '',
    description: initial?.description ?? null,
    sourceDbId: initial?.sourceDbId ?? '',
    targetDbId: initial?.targetDbId ?? '',
    ignoreOwnership: initial?.ignoreOwnership ?? true,
    cronExpression: initial?.cronExpression ?? null,
  })
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState('')

  return (
    <div className="space-y-4">
      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">Profile Name</label>
        <Input placeholder="QA → PROD · Common" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} />
      </div>
      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">Description (optional)</label>
        <Input placeholder="Nightly schema drift check" value={form.description ?? ''} onChange={e => setForm(f => ({ ...f, description: e.target.value || null }))} />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Source Database</label>
          <Select value={form.sourceDbId} onValueChange={v => setForm(f => ({ ...f, sourceDbId: v }))}>
            <SelectTrigger><SelectValue placeholder="Select source..." /></SelectTrigger>
            <SelectContent>
              {readDbs.map(d => <SelectItem key={d.id} value={d.id}>{d.name} ({d.environment})</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Target Database</label>
          <Select value={form.targetDbId} onValueChange={v => setForm(f => ({ ...f, targetDbId: v }))}>
            <SelectTrigger><SelectValue placeholder="Select target..." /></SelectTrigger>
            <SelectContent>
              {readDbs.map(d => <SelectItem key={d.id} value={d.id}>{d.name} ({d.environment})</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
      </div>
      {err && <p className="text-red-600 text-xs">{err}</p>}
      <DialogFooter>
        <Button variant="outline" onClick={onClose}>Cancel</Button>
        <Button disabled={loading} onClick={async () => {
          if (!form.name || !form.sourceDbId || !form.targetDbId) return setErr('All fields required')
          setLoading(true); setErr('')
          try { await onSave(form); onClose() }
          catch (e: unknown) { setErr(e instanceof Error ? e.message : 'Error'); setLoading(false) }
        }}>
          {loading ? 'Saving...' : 'Save Profile'}
        </Button>
      </DialogFooter>
    </div>
  )
}

function LastRunBadge({ profile }: { profile: Profile }) {
  if (!profile.lastRunStatus) return <Badge variant="secondary">Never run</Badge>
  if (profile.lastRunStatus === 'Completed') {
    const s = profile.lastRunSummary ? JSON.parse(profile.lastRunSummary) : null
    const drift = s ? s.mismatch + s.missingInTarget + s.missingInSource : 0
    return drift === 0
      ? <Badge variant="success">Clean</Badge>
      : <Badge variant="destructive">{drift} drifts</Badge>
  }
  if (profile.lastRunStatus === 'Running') return <Badge variant="running">Running</Badge>
  return <Badge variant="destructive">Failed</Badge>
}

export default function Profiles() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data: profiles = [], isLoading } = useQuery({ queryKey: ['profiles'], queryFn: api.profiles.list })
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<Profile | null>(null)
  const [runningAll, setRunningAll] = useState(false)

  const [activeBatch, setActiveBatch] = useState<{ id: string; total: number; done: number } | null>(null)

  const runAll = async () => {
    if (!confirm(`Run all ${profiles.length} profiles? This will queue ${profiles.length} comparisons.`)) return
    setRunningAll(true)
    const { batchRunId, totalRuns } = await api.batchRuns.runAll()
    setActiveBatch({ id: batchRunId, total: totalRuns, done: 0 })
    qc.invalidateQueries({ queryKey: ['comparisons'] })
    qc.invalidateQueries({ queryKey: ['profiles'] })
    setRunningAll(false)
    // Poll batch status
    const poll = setInterval(async () => {
      const status = await api.batchRuns.get(batchRunId)
      setActiveBatch({ id: batchRunId, total: status.totalRuns, done: status.completedRuns + status.failedRuns })
      if (status.isComplete) { clearInterval(poll); setTimeout(() => setActiveBatch(null), 5000) }
    }, 3000)
  }

  const deleteMutation = useMutation({
    mutationFn: api.profiles.delete,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['profiles'] }),
  })

  const runProfile = async (id: string) => {
    const { runId } = await api.profiles.run(id)
    qc.invalidateQueries({ queryKey: ['comparisons'] })
    navigate(`/results/${runId}`)
  }

  const handleSave = async (req: CreateProfileRequest) => {
    if (editing) {
      await api.profiles.update(editing.id, req)
    } else {
      await api.profiles.create(req)
    }
    qc.invalidateQueries({ queryKey: ['profiles'] })
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Comparison Profiles</h1>
          <p className="text-slate-500 text-sm mt-1">Saved database comparison configurations</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={runAll} disabled={runningAll} className="gap-2">
            {runningAll ? 'Running...' : '▶ Run All'}
          </Button>
          <a href="/api/databases/export-batch-config" download>
            <Button variant="outline" className="gap-2">↓ Export Config</Button>
          </a>
          <Button onClick={() => { setEditing(null); setShowForm(true) }} className="gap-2">
            <Plus className="h-4 w-4" /> New Profile
          </Button>
        </div>
      </div>

      {/* Batch progress banner */}
      {activeBatch && (
        <div className="bg-indigo-50 border border-indigo-200 rounded-xl px-4 py-3 flex items-center gap-4">
          <div className="flex-1">
            <p className="text-sm font-medium text-indigo-800">Running all profiles…</p>
            <div className="mt-1 h-2 bg-indigo-100 rounded-full overflow-hidden">
              <div
                className="h-full bg-indigo-500 transition-all"
                style={{ width: `${(activeBatch.done / activeBatch.total) * 100}%` }}
              />
            </div>
          </div>
          <span className="text-indigo-700 font-semibold text-sm">{activeBatch.done}/{activeBatch.total}</span>
        </div>
      )}

      {isLoading && <p className="text-slate-400 text-sm">Loading...</p>}

      {profiles.length === 0 && !isLoading && (
        <Card>
          <CardContent className="py-16 text-center">
            <p className="text-slate-400">No profiles yet. Create one to get started.</p>
          </CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 gap-3">
        {profiles.map(p => (
          <Card key={p.id} className="hover:shadow-md transition-shadow">
            <CardContent className="p-4">
              <div className="flex items-center gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-0.5">
                    <p className="font-semibold text-slate-900">{p.name}</p>
                    <LastRunBadge profile={p} />
                  </div>
                  <p className="text-sm text-slate-500">
                    <span className="font-medium text-slate-700">{p.sourceDbName}</span>
                    <span className="mx-2 text-slate-300">→</span>
                    <span className="font-medium text-slate-700">{p.targetDbName}</span>
                  </p>
                  {p.description && <p className="text-xs text-slate-400 mt-0.5">{p.description}</p>}
                  {p.lastRunAt && (
                    <p className="text-xs text-slate-400 mt-1 flex items-center gap-1">
                      <Clock className="h-3 w-3" />
                      Last run {new Date(p.lastRunAt).toLocaleString()}
                    </p>
                  )}
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  {p.lastRunId && p.lastRunStatus === 'Completed' && (
                    <Button variant="ghost" size="sm" onClick={() => navigate(`/results/${p.lastRunId}`)}>
                      View last
                    </Button>
                  )}
                  <Button variant="outline" size="sm" onClick={() => { setEditing(p); setShowForm(true) }}>
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => { if (confirm('Delete this profile?')) deleteMutation.mutate(p.id) }}>
                    <Trash2 className="h-3.5 w-3.5 text-red-500" />
                  </Button>
                  <Button size="sm" onClick={() => runProfile(p.id)} className="gap-1.5">
                    <Play className="h-3.5 w-3.5" /> Run
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      <Dialog open={showForm} onOpenChange={setShowForm}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? 'Edit Profile' : 'New Profile'}</DialogTitle>
          </DialogHeader>
          <ProfileForm
            initial={editing ?? undefined}
            onSave={handleSave}
            onClose={() => setShowForm(false)}
          />
        </DialogContent>
      </Dialog>
    </div>
  )
}
