import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

export default function NewComparison() {
  const navigate = useNavigate();
  const { data: dbs = [] } = useQuery({ queryKey: ['databases'], queryFn: api.databases.list });
  const [sourceId, setSourceId] = useState('');
  const [targetId, setTargetId] = useState('');
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState('');

  const readDbs = dbs.filter(d => !d.isWriteAccount);

  const handleStart = async () => {
    if (!sourceId || !targetId) return setErr('Select both source and target');
    if (sourceId === targetId) return setErr('Source and target must be different');
    setErr('');
    setLoading(true);
    try {
      const { runId } = await api.comparisons.start(sourceId, targetId);
      navigate(`/results/${runId}`);
    } catch (e: unknown) {
      setErr(e instanceof Error ? e.message : 'Failed to start comparison');
      setLoading(false);
    }
  };

  return (
    <div className="max-w-xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 mb-6">New Comparison</h1>
      <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-5">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Source Database</label>
          <select
            className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            value={sourceId}
            onChange={e => setSourceId(e.target.value)}
          >
            <option value="">Select source...</option>
            {readDbs.map(d => (
              <option key={d.id} value={d.id}>{d.name} ({d.environment})</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Target Database</label>
          <select
            className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            value={targetId}
            onChange={e => setTargetId(e.target.value)}
          >
            <option value="">Select target...</option>
            {readDbs.map(d => (
              <option key={d.id} value={d.id}>{d.name} ({d.environment})</option>
            ))}
          </select>
        </div>
        {err && <p className="text-red-600 text-sm">{err}</p>}
        <button
          onClick={handleStart}
          disabled={loading}
          className="w-full bg-indigo-600 text-white py-2 rounded-lg font-medium hover:bg-indigo-700 disabled:opacity-50"
        >
          {loading ? 'Starting...' : 'Run Comparison'}
        </button>
      </div>
    </div>
  );
}
