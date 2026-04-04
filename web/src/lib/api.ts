import type { TestSession, PaginatedResponse, DiagSession, HierarchySnapshot, RunSummary } from './types';

const BASE_URL = import.meta.env.VITE_API_URL || '';

function getHeaders(): Record<string, string> {
  const headers: Record<string, string> = {};
  const token = localStorage.getItem('auth_token');
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const projectId = localStorage.getItem('active_project_id');
  if (projectId) headers['X-Project-Id'] = projectId;
  return headers;
}

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(`${BASE_URL}${url}`, { headers: getHeaders() });
  if (res.status === 401) {
    localStorage.removeItem('auth_token');
    window.location.href = '/login';
    throw new Error('Unauthorized');
  }
  if (!res.ok) throw new Error(`HTTP ${res.status}: ${res.statusText}`);
  return res.json();
}

/** List sessions with optional filters */
export async function getSessions(params: {
  testName?: string;
  result?: string;
  project?: string;
  branch?: string;
  appVersion?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
  sort?: string;
  order?: string;
} = {}): Promise<PaginatedResponse<TestSession>> {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== '') qs.set(k, String(v));
  });
  if (!qs.has('page')) qs.set('page', '1');
  if (!qs.has('pageSize')) qs.set('pageSize', '50');
  return fetchJson(`/api/sessions?${qs}`);
}

/** Get single session metadata */
export async function getSession(id: string): Promise<TestSession> {
  return fetchJson(`/api/sessions/${id}`);
}

/** Get full session.json from the zip */
export async function getSessionJson(id: string): Promise<DiagSession> {
  return fetchJson(`/api/sessions/${id}/session.json`);
}

/** Get list of files in the session zip */
export async function getSessionFiles(id: string): Promise<string[]> {
  return fetchJson(`/api/sessions/${id}/files`);
}

/** Build URL for a file inside the session zip (with auth token for <img>/<video> src) */
export function sessionFileUrl(id: string, fileName: string): string {
  const token = localStorage.getItem('auth_token');
  const tokenParam = token ? `?token=${encodeURIComponent(token)}` : '';
  return `${BASE_URL}/api/sessions/${id}/files/${encodeURIComponent(fileName)}${tokenParam}`;
}

/** Build URL for the session video (with auth token for <video> src) */
export function sessionVideoUrl(id: string): string {
  const token = localStorage.getItem('auth_token');
  const tokenParam = token ? `?token=${encodeURIComponent(token)}` : '';
  return `${BASE_URL}/api/sessions/${id}/video${tokenParam}`;
}

/** Fetch a hierarchy JSON file from the session */
export async function getHierarchy(id: string, fileName: string): Promise<HierarchySnapshot> {
  return fetchJson(`/api/sessions/${id}/files/${encodeURIComponent(fileName)}`);
}

/** Fetch the video timestamp sidecar CSV. Returns null if not available. */
export async function getVideoTimestamps(id: string, fileName: string): Promise<Float32Array | null> {
  try {
    const res = await fetch(`${BASE_URL}/api/sessions/${id}/files/${encodeURIComponent(fileName)}`, {
      headers: getHeaders(),
    });
    if (!res.ok) return null;
    const text = await res.text();
    const lines = text.trim().split('\n');
    const times = new Float32Array(lines.length);
    for (let i = 0; i < lines.length; i++) {
      const comma = lines[i].indexOf(',');
      times[i] = comma >= 0 ? parseFloat(lines[i].substring(comma + 1)) : 0;
    }
    return times;
  } catch {
    return null;
  }
}

/** Get aggregate stats */
export async function getStats(): Promise<{
  total: number;
  passed: number;
  failed: number;
  warned: number;
  byProject: { project: string; count: number; failures: number }[];
}> {
  return fetchJson('/api/stats');
}

/** Get distinct projects */
export async function getProjects(): Promise<string[]> {
  return fetchJson('/api/projects');
}

/** Get distinct versions */
export async function getVersions(project?: string): Promise<string[]> {
  const qs = project ? `?project=${encodeURIComponent(project)}` : '';
  return fetchJson(`/api/versions${qs}`);
}

/** Get distinct branches for a project */
export async function getBranches(project?: string): Promise<string[]> {
  const qs = project ? `?project=${encodeURIComponent(project)}` : '';
  return fetchJson(`/api/branches${qs}`);
}

/** Delete a session */
export async function deleteSession(id: string): Promise<void> {
  const res = await fetch(`${BASE_URL}/api/sessions/${id}`, {
    method: 'DELETE',
    headers: getHeaders(),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}

/** Upload a zip file */
export async function uploadSession(file: File): Promise<TestSession> {
  const form = new FormData();
  form.append('file', file);
  const headers = getHeaders();
  // Don't set Content-Type for FormData — browser sets it with boundary
  const res = await fetch(`${BASE_URL}/api/sessions/upload`, {
    method: 'POST',
    body: form,
    headers,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
  return res.json();
}

/** Bulk delete sessions matching filters */
export async function bulkDeleteSessions(params: {
  olderThan?: string;
  project?: string;
  result?: string;
}): Promise<{ deleted: number }> {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== '') qs.set(k, String(v));
  });
  const res = await fetch(`${BASE_URL}/api/sessions/bulk?${qs}`, {
    method: 'DELETE',
    headers: getHeaders(),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

/** Get storage usage stats */
export async function getStorage(): Promise<{
  totalBytes: number;
  totalMB: number;
  totalGB: number;
  fileCount: number;
}> {
  return fetchJson('/api/storage');
}

/** List test runs (grouped sessions) */
export async function getRuns(params: {
  project?: string;
  branch?: string;
  appVersion?: string;
  result?: string;
  page?: number;
  pageSize?: number;
} = {}): Promise<PaginatedResponse<RunSummary>> {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== '') qs.set(k, String(v));
  });
  if (!qs.has('page')) qs.set('page', '1');
  if (!qs.has('pageSize')) qs.set('pageSize', '50');
  return fetchJson(`/api/runs?${qs}`);
}

/** Get all sessions in a run */
export async function getRunSessions(runId: string): Promise<TestSession[]> {
  return fetchJson(`/api/runs/${encodeURIComponent(runId)}`);
}

/** Delete a run and all its sessions */
export async function deleteRun(runId: string): Promise<void> {
  const res = await fetch(`${BASE_URL}/api/runs/${encodeURIComponent(runId)}`, {
    method: 'DELETE',
    headers: getHeaders(),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}
