import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import type { Database, RegisterDatabaseRequest, DbKind, DbEnvironment } from '../api/client';

function DatabaseForm({
  initial,
  onSave,
  onCancel,
}: {
  initial?: Database;
  onSave: (req: RegisterDatabaseRequest) => Promise<void>;
  onCancel: () => void;
}) {
  const { data: dbs = [] } = useQuery({ queryKey: ['databases'], queryFn: api.databases.list });
  const [form, setForm] = useState<RegisterDatabaseRequest>({
    name: initial?.name ?? '',
    dbType: initial?.dbType ?? 'Postgres',
    environment: initial?.environment ?? 'QA',
    connectionString: '',
    isWriteAccount: initial?.isWriteAccount ?? false,
    readAccountId: initial?.readAccountId ?? null,
  });
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState('');

  const set = (k: keyof RegisterDatabaseRequest, v: unknown) =>
    setForm(f => ({ ...f, [k]: v }));

  return (
    <div className="space-y-4">
      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
        <input className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm" value={form.name} onChange={e => set('name', e.target.value)} />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">DB Type</label>
          <select className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm" value={form.dbType} onChange={e => set('dbType', e.target.value as DbKind)}>
            <option>Postgres</option><option>MySql</option>
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Environment</label>
          <select className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm" value={form.environment} onChange={e => set('environment', e.target.value as DbEnvironment)}>
            <option>QA</option><option>UAT</option><option>Prod</option><option>Other</option>
          </select>
        </div>
      </div>
      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">
          Connection String {initial && '(leave blank to keep existing)'}
        </label>
        <input
          type="password"
          className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm font-mono"
          placeholder="Host=...;Port=5432;Database=...;User Id=...;Password=...;"
          value={form.connectionString}
          onChange={e => set('connectionString', e.target.value)}
        />
      </div>
      <div className="flex items-center gap-2">
        <input type="checkbox" id="write" checked={form.isWriteAccount} onChange={e => set('isWriteAccount', e.target.checked)} />
        <label htmlFor="write" className="text-sm text-gray-700">This is a write account (used for sync apply only)</label>
      </div>
      {form.isWriteAccount && (
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Paired Read Account</label>
          <select className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm" value={form.readAccountId ?? ''} onChange={e => set('readAccountId', e.target.value || null)}>
            <option value="">Select read account...</option>
            {dbs.filter(d => !d.isWriteAccount).map(d => (
              <option key={d.id} value={d.id}>{d.name} ({d.environment})</option>
            ))}
          </select>
        </div>
      )}
      {err && <p className="text-red-600 text-sm">{err}</p>}
      <div className="flex gap-3">
        <button onClick={onCancel} className="flex-1 border border-gray-300 rounded-lg py-2 text-sm">Cancel</button>
        <button
          onClick={async () => {
            if (!form.name || (!initial && !form.connectionString)) return setErr('Name and connection string are required');
            setLoading(true); setErr('');
            try { await onSave(form); } catch (e: unknown) { setErr(e instanceof Error ? e.message : 'Error'); setLoading(false); }
          }}
          disabled={loading}
          className="flex-1 bg-brand-orange text-white rounded-lg py-2 text-sm font-medium hover:bg-brand-orange-dark disabled:opacity-50"
        >
          {loading ? 'Saving...' : 'Save'}
        </button>
      </div>
    </div>
  );
}

export default function Databases() {
  const qc = useQueryClient();
  const { data: dbs = [], isLoading } = useQuery({ queryKey: ['databases'], queryFn: api.databases.list });
  const [editing, setEditing] = useState<Database | null | 'new'>(null);
  const [testResult, setTestResult] = useState<Record<string, { success: boolean; tableCount?: number; error?: string }>>({});

  const deleteMutation = useMutation({
    mutationFn: api.databases.delete,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['databases'] }),
  });

  const handleSave = async (req: RegisterDatabaseRequest) => {
    if (editing === 'new') {
      await api.databases.create(req);
    } else if (editing) {
      await api.databases.update(editing.id, req);
    }
    qc.invalidateQueries({ queryKey: ['databases'] });
    setEditing(null);
  };

  const testConnection = async (id: string) => {
    const r = await api.databases.test(id);
    setTestResult(prev => ({ ...prev, [id]: r }));
  };

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold text-gray-900">Registered Databases</h1>
        <button onClick={() => setEditing('new')} className="bg-brand-orange text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-brand-orange-dark">
          + Register Database
        </button>
      </div>

      {editing && (
        <div className="bg-white dark:bg-bg-card rounded-xl border border-gray-200 p-6 mb-6">
          <h2 className="text-lg font-medium text-gray-900 mb-4">{editing === 'new' ? 'Register Database' : 'Edit Database'}</h2>
          <DatabaseForm
            initial={editing !== 'new' ? editing : undefined}
            onSave={handleSave}
            onCancel={() => setEditing(null)}
          />
        </div>
      )}

      {isLoading && <p className="text-gray-500">Loading...</p>}

      <div className="bg-white dark:bg-bg-card rounded-xl border border-gray-200 overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 border-b">
            <tr>
              {['Name', 'Type', 'Env', 'Account', 'Test', ''].map(h => (
                <th key={h} className="px-4 py-3 text-left text-xs font-medium text-gray-500">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {dbs.map(d => (
              <tr key={d.id} className="hover:bg-gray-50">
                <td className="px-4 py-3 font-medium text-gray-900">{d.name}</td>
                <td className="px-4 py-3 text-gray-500">{d.dbType}</td>
                <td className="px-4 py-3">
                  <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                    d.environment === 'Prod' ? 'bg-red-100 text-red-700' :
                    d.environment === 'QA' ? 'bg-blue-100 text-blue-700' :
                    'bg-gray-100 text-gray-600'
                  }`}>{d.environment}</span>
                </td>
                <td className="px-4 py-3 text-xs text-gray-500">{d.isWriteAccount ? '✏️ Write' : '👁 Read'}</td>
                <td className="px-4 py-3">
                  <button onClick={() => testConnection(d.id)} className="text-brand-orange text-xs hover:underline">Test</button>
                  {testResult[d.id] && (
                    <span className={`ml-2 text-xs ${testResult[d.id].success ? 'text-green-600' : 'text-red-600'}`}>
                      {testResult[d.id].success ? `✅ ${testResult[d.id].tableCount} tables` : `❌ ${testResult[d.id].error}`}
                    </span>
                  )}
                </td>
                <td className="px-4 py-3 flex gap-2">
                  <button onClick={() => setEditing(d)} className="text-gray-400 hover:text-gray-700 text-xs">Edit</button>
                  <button onClick={() => { if (confirm('Delete this database?')) deleteMutation.mutate(d.id); }} className="text-red-400 hover:text-red-600 text-xs">Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {dbs.length === 0 && !isLoading && (
          <p className="text-center text-gray-400 py-8">No databases registered yet.</p>
        )}
      </div>
    </div>
  );
}
