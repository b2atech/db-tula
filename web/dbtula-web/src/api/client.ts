const TOKEN_KEY = 'dbtula_token';

export const tokenStore = {
  get: () => sessionStorage.getItem(TOKEN_KEY),
  set: (t: string) => sessionStorage.setItem(TOKEN_KEY, t),
  clear: () => sessionStorage.removeItem(TOKEN_KEY),
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = tokenStore.get();
  const res = await fetch(path, {
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(init?.headers ?? {}),
    },
    ...init,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `HTTP ${res.status}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

export const api = {
  auth: {
    google: async (idToken: string) => {
      const res = await request<{ token: string; user: User }>(
        '/api/auth/google', { method: 'POST', body: JSON.stringify({ idToken }) }
      );
      tokenStore.set(res.token);
      return res.user;
    },
    me: () => request<User>('/api/auth/me'),
    logout: () => { tokenStore.clear(); return Promise.resolve(); },
  },
  databases: {
    list: () => request<Database[]>('/api/databases'),
    create: (body: RegisterDatabaseRequest) =>
      request<Database>('/api/databases', { method: 'POST', body: JSON.stringify(body) }),
    update: (id: string, body: RegisterDatabaseRequest) =>
      request<Database>(`/api/databases/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
    delete: (id: string) => request<void>(`/api/databases/${id}`, { method: 'DELETE' }),
    test: (id: string) =>
      request<{ success: boolean; tableCount?: number; error?: string }>(`/api/databases/${id}/test`, { method: 'POST' }),
  },
  profiles: {
    list: () => request<Profile[]>('/api/profiles'),
    create: (body: CreateProfileRequest) =>
      request<string>('/api/profiles', { method: 'POST', body: JSON.stringify(body) }),
    update: (id: string, body: CreateProfileRequest) =>
      request<void>(`/api/profiles/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
    delete: (id: string) => request<void>(`/api/profiles/${id}`, { method: 'DELETE' }),
    run: (id: string) =>
      request<{ runId: string }>(`/api/profiles/${id}/run`, { method: 'POST' }),
  },
  comparisons: {
    list: (page = 1) => request<ComparisonRun[]>(`/api/comparisons?page=${page}`),
    start: (sourceDbId: string, targetDbId: string) =>
      request<{ runId: string }>('/api/comparisons', { method: 'POST', body: JSON.stringify({ sourceDbId, targetDbId }) }),
    get: (id: string) => request<ComparisonRunDetail>(`/api/comparisons/${id}`),
    syncScriptUrl: (id: string, category: 'safe' | 'risky' | 'destructive') =>
      `/api/comparisons/${id}/sync-script?category=${category}`,
    statements: (id: string, category?: string) =>
      request<SyncStatementItem[]>(`/api/comparisons/${id}/statements${category ? `?category=${category}` : ''}`),
    toggleStatement: (id: string, sid: string, isApproved: boolean) =>
      request<void>(`/api/comparisons/${id}/statements/${sid}`, { method: 'PATCH', body: JSON.stringify({ isApproved }) }),
    applyApproved: (id: string) =>
      request<ApplySafeResult>(`/api/comparisons/${id}/apply-approved`, { method: 'POST' }),
  },
  metrics: {
    summary: () => request<MetricsSummary>('/api/metrics/summary'),
    driftTrend: (days = 30) => request<DriftTrendPoint[]>(`/api/metrics/drift-trend?days=${days}`),
    driftTrendByProfile: (days = 30) => request<ProfileDriftSeries[]>(`/api/metrics/drift-trend-by-profile?days=${days}`),
    dbHealth: () => request<DbHealth[]>('/api/metrics/db-health'),
  },
  admin: {
    users: () => request<User[]>('/api/admin/users'),
    updateRole: (id: string, role: UserRole) =>
      request<User>(`/api/admin/users/${id}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
    auditLog: (page = 1) => request<AuditLog[]>(`/api/admin/audit-log?page=${page}`),
    allowedEmails: () => request<AllowedEmailEntry[]>('/api/admin/allowed-emails'),
    addAllowedEmail: (email: string) =>
      request<void>('/api/admin/allowed-emails', { method: 'POST', body: JSON.stringify(email) }),
    removeAllowedEmail: (id: string) =>
      request<void>(`/api/admin/allowed-emails/${id}`, { method: 'DELETE' }),
  },
};

// ── Types ──────────────────────────────────────────────────────────────────────

export type UserRole = 'Viewer' | 'Operator' | 'Admin';
export type DbKind = 'Postgres' | 'MySql';
export type DbEnvironment = 'QA' | 'UAT' | 'Prod' | 'Other';
export type RunStatus = 'Pending' | 'Running' | 'Completed' | 'Failed';
export type HealthStatus = 'Healthy' | 'Drift' | 'Unknown';

export interface User { id: string; email: string; name: string; role: UserRole; }

export interface Database {
  id: string; name: string; dbType: DbKind; environment: DbEnvironment;
  isWriteAccount: boolean; readAccountId: string | null; createdAt: string;
}

export interface RegisterDatabaseRequest {
  name: string; dbType: DbKind; environment: DbEnvironment;
  connectionString: string; isWriteAccount: boolean; readAccountId: string | null;
}

export interface Profile {
  id: string; name: string; description: string | null;
  sourceDbId: string; sourceDbName: string;
  targetDbId: string; targetDbName: string;
  ignoreOwnership: boolean; createdAt: string;
  lastRunId: string | null; lastRunStatus: string | null;
  lastRunAt: string | null; lastRunSummary: string | null;
}

export interface CreateProfileRequest {
  name: string; description: string | null;
  sourceDbId: string; targetDbId: string; ignoreOwnership: boolean;
}

export interface ComparisonRun {
  id: string; profileId: string | null; profileName: string | null;
  sourceDbId: string; sourceDbName: string;
  targetDbId: string; targetDbName: string;
  status: RunStatus; startedAt: string; completedAt: string | null;
  summaryJson: string | null; errorMessage: string | null;
}

export interface ComparisonRunDetail extends ComparisonRun {
  resultJson: string | null;
  hasSafeScript: boolean; hasRiskyScript: boolean; hasDestructiveScript: boolean;
}

export interface ComparisonResultItem {
  objectType: string; name: string; status: string; details: string;
  sourceScript: string | null; targetScript: string | null;
  sideBySideDiffHtml: string | null; lintCode: string | null;
  lintMessage: string | null; subResults: SubResult[];
}

export interface SubResult {
  component: string; status: string; details: string; createScript: string | null;
}

export interface SyncStatementItem {
  id: string; category: string; objectType: string; objectName: string;
  sql: string; comment: string; orderIndex: number;
  isApproved: boolean; isApplied: boolean; appliedAt: string | null;
}

export interface ApplySafeResult { successCount: number; failureCount: number; errors: string[]; }

export interface AuditLog {
  id: string; comparisonRunId: string; appliedByName: string; appliedAt: string;
  targetDbName: string; successCount: number; failureCount: number; errorDetails: string | null;
}

export interface AllowedEmailEntry { id: string; email: string; addedBy: string; addedAt: string; }

export interface MetricsSummary {
  totalRuns30d: number; driftRuns30d: number;
  statementsApplied: number; dbsRegistered: number;
}

export interface DriftTrendPoint { date: string; mismatch: number; missingInTarget: number; }
export interface ProfileDriftPoint { date: string; drift: number; }
export interface ProfileDriftSeries { profile: string; points: ProfileDriftPoint[]; }

export interface DbHealth {
  profileId: string; profileName: string;
  sourceDb: string; targetDb: string;
  status: HealthStatus; totalDrift: number;
  lastRunAt: string | null; lastRunId: string | null;
}

export interface Summary {
  match: number; mismatch: number; missingInTarget: number; missingInSource: number;
}
