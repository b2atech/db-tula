import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Trash2, Plus, ShieldCheck, Users, FileText } from 'lucide-react'
import { Card, CardContent } from '../components/ui/card'
import { Button } from '../components/ui/button'
import { Input } from '../components/ui/input'
import { Badge } from '../components/ui/badge'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '../components/ui/tabs'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select'
import { api } from '../api/client'
import type { UserRole } from '../api/client'

export default function Admin() {
  const qc = useQueryClient()

  // Users
  const { data: users = [] } = useQuery({ queryKey: ['admin-users'], queryFn: api.admin.users })
  const updateRole = useMutation({
    mutationFn: ({ id, role }: { id: string; role: UserRole }) => api.admin.updateRole(id, role),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-users'] }),
  })

  // Allowed Emails
  const { data: allowedEmails = [] } = useQuery({ queryKey: ['allowed-emails'], queryFn: api.admin.allowedEmails })
  const [newEmail, setNewEmail] = useState('')
  const [emailErr, setEmailErr] = useState('')
  const addEmail = useMutation({
    mutationFn: (email: string) => api.admin.addAllowedEmail(email),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['allowed-emails'] }); setNewEmail('') },
    onError: (e: unknown) => setEmailErr(e instanceof Error ? e.message : 'Error'),
  })
  const removeEmail = useMutation({
    mutationFn: (id: string) => api.admin.removeAllowedEmail(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['allowed-emails'] }),
  })

  // Audit log
  const { data: logs = [] } = useQuery({ queryKey: ['audit-log'], queryFn: () => api.admin.auditLog() })

  const roles: UserRole[] = ['Viewer', 'Operator', 'Admin']

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Admin</h1>
        <p className="text-slate-500 text-sm mt-1">Manage users, access, and audit history</p>
      </div>

      <Tabs defaultValue="users">
        <TabsList>
          <TabsTrigger value="users" className="gap-1.5"><Users className="h-3.5 w-3.5" />Users</TabsTrigger>
          <TabsTrigger value="emails" className="gap-1.5"><ShieldCheck className="h-3.5 w-3.5" />Allowed Emails</TabsTrigger>
          <TabsTrigger value="audit" className="gap-1.5"><FileText className="h-3.5 w-3.5" />Audit Log</TabsTrigger>
        </TabsList>

        {/* ── Users ─────────────────────────────────────────────────────────── */}
        <TabsContent value="users">
          <Card>
            <CardContent className="p-0">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 border-b">
                  <tr>
                    {['Name', 'Email', 'Role', 'Joined'].map(h => (
                      <th key={h} className="px-4 py-2.5 text-left text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {users.map(u => (
                    <tr key={u.id} className="hover:bg-slate-50">
                      <td className="px-4 py-3 font-medium text-slate-900">{u.name}</td>
                      <td className="px-4 py-3 text-slate-500">{u.email}</td>
                      <td className="px-4 py-3">
                        <Select value={u.role} onValueChange={v => updateRole.mutate({ id: u.id, role: v as UserRole })}>
                          <SelectTrigger className="w-32 h-7 text-xs">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {roles.map(r => <SelectItem key={r} value={r}>{r}</SelectItem>)}
                          </SelectContent>
                        </Select>
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-400">
                        {new Date(u.id.substring(0, 8) === '00000000' ? Date.now() : Date.now()).toLocaleDateString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </CardContent>
          </Card>
        </TabsContent>

        {/* ── Allowed Emails ────────────────────────────────────────────────── */}
        <TabsContent value="emails">
          <div className="space-y-4">
            <Card>
              <CardContent className="p-4">
                <p className="text-sm text-slate-600 mb-3">
                  Only users whose email is in this list can sign in.
                  <span className="text-slate-400 ml-1">(Empty list = anyone with Google can sign in)</span>
                </p>
                <div className="flex gap-2">
                  <Input
                    placeholder="colleague@company.com"
                    value={newEmail}
                    onChange={e => { setNewEmail(e.target.value); setEmailErr('') }}
                    onKeyDown={e => e.key === 'Enter' && newEmail && addEmail.mutate(newEmail.trim())}
                    className="max-w-sm"
                  />
                  <Button
                    onClick={() => { if (newEmail) addEmail.mutate(newEmail.trim()) }}
                    disabled={!newEmail || addEmail.isPending}
                    className="gap-1.5"
                  >
                    <Plus className="h-4 w-4" /> Add
                  </Button>
                </div>
                {emailErr && <p className="text-red-600 text-xs mt-2">{emailErr}</p>}
              </CardContent>
            </Card>

            <Card>
              <CardContent className="p-0">
                {allowedEmails.length === 0 ? (
                  <p className="px-4 py-8 text-center text-slate-400 text-sm">
                    No restrictions — anyone can sign in with Google.
                  </p>
                ) : (
                  <table className="w-full text-sm">
                    <thead className="bg-slate-50 border-b">
                      <tr>
                        {['Email', 'Added By', 'Added At', ''].map(h => (
                          <th key={h} className="px-4 py-2.5 text-left text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {allowedEmails.map(e => (
                        <tr key={e.id} className="hover:bg-slate-50">
                          <td className="px-4 py-2.5 font-medium text-slate-900">{e.email}</td>
                          <td className="px-4 py-2.5 text-slate-500">{e.addedBy}</td>
                          <td className="px-4 py-2.5 text-xs text-slate-400">{new Date(e.addedAt).toLocaleString()}</td>
                          <td className="px-4 py-2.5 text-right">
                            <Button
                              variant="ghost" size="sm"
                              onClick={() => { if (confirm(`Remove ${e.email}?`)) removeEmail.mutate(e.id) }}
                              className="text-red-500 hover:text-red-700 hover:bg-red-50 h-7"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        {/* ── Audit Log ─────────────────────────────────────────────────────── */}
        <TabsContent value="audit">
          <Card>
            <CardContent className="p-0">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 border-b">
                  <tr>
                    {['Applied By', 'Target DB', 'Applied At', 'Success', 'Failed', 'Errors'].map(h => (
                      <th key={h} className="px-4 py-2.5 text-left text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {logs.map(l => (
                    <tr key={l.id} className="hover:bg-slate-50">
                      <td className="px-4 py-2.5 font-medium text-slate-900">{l.appliedByName}</td>
                      <td className="px-4 py-2.5 text-slate-600">{l.targetDbName}</td>
                      <td className="px-4 py-2.5 text-xs text-slate-400">{new Date(l.appliedAt).toLocaleString()}</td>
                      <td className="px-4 py-2.5"><Badge variant="success">{l.successCount}</Badge></td>
                      <td className="px-4 py-2.5">{l.failureCount > 0 && <Badge variant="destructive">{l.failureCount}</Badge>}</td>
                      <td className="px-4 py-2.5 text-xs text-red-500 max-w-xs truncate">{l.errorDetails ?? '—'}</td>
                    </tr>
                  ))}
                  {logs.length === 0 && (
                    <tr><td colSpan={6} className="py-8 text-center text-slate-400 text-sm">No sync apply actions yet.</td></tr>
                  )}
                </tbody>
              </table>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  )
}
