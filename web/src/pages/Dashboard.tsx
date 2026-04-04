import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { getProjects, getBranches, getVersions, getStats, getStorage, bulkDeleteSessions, uploadSession, getRuns, getRunSessions, deleteRun } from '../lib/api';
import type { TestSession, RunSummary } from '../lib/types';
import { UserMenu } from '../components/UserMenu';
import { useAuth } from '../lib/auth';

const resultBadge = (result: string) => {
  const colors: Record<string, string> = {
    pass: 'bg-green-900/50 text-green-400 border-green-700',
    fail: 'bg-red-900/50 text-red-400 border-red-700',
    warn: 'bg-orange-900/50 text-orange-400 border-orange-700',
    running: 'bg-blue-900/50 text-blue-400 border-blue-700',
  };
  return colors[result] || 'bg-gray-800 text-gray-400 border-gray-600';
};

function timeAgo(dateStr: string): string {
  const d = new Date(dateStr);
  const now = new Date();
  const diff = (now.getTime() - d.getTime()) / 1000;
  if (diff < 60) return `${Math.floor(diff)}s ago`;
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
  return `${Math.floor(diff / 86400)}d ago`;
}

function formatDuration(seconds: number): string {
  if (seconds < 1) return `${Math.round(seconds * 1000)}ms`;
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  return `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;
}

function runTestSummary(run: RunSummary): string {
  const parts: string[] = [];
  if (run.passed > 0) parts.push(`${run.passed} pass`);
  if (run.failed > 0) parts.push(`${run.failed} fail`);
  if (run.warned > 0) parts.push(`${run.warned} warn`);
  return `${run.totalTests} test${run.totalTests !== 1 ? 's' : ''}: ${parts.join(', ')}`;
}

export function Dashboard() {
  const navigate = useNavigate();
  const { activeProject, projects: authProjects, setActiveProject } = useAuth();
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);

  // Runs state
  const [runs, setRuns] = useState<RunSummary[]>([]);
  const [expandedRunId, setExpandedRunId] = useState<string | null>(null);
  const [expandedSessions, setExpandedSessions] = useState<TestSession[]>([]);
  const [loadingExpanded, setLoadingExpanded] = useState(false);

  // Filters
  const [result, setResult] = useState('');
  const [project, setProject] = useState('');
  const [branch, setBranch] = useState('');
  const [version, setVersion] = useState('');
  const [projects, setProjects] = useState<string[]>([]);
  const [branches, setBranches] = useState<string[]>([]);
  const [versions, setVersions] = useState<string[]>([]);

  // Stats
  const [stats, setStats] = useState<{ total: number; passed: number; failed: number; warned: number } | null>(null);
  const [storage, setStorage] = useState<{ totalMB: number; totalGB: number; fileCount: number } | null>(null);

  // Bulk delete
  const [showBulkDelete, setShowBulkDelete] = useState(false);
  const [bulkAge, setBulkAge] = useState('30');
  const [bulkProject, setBulkProject] = useState('');
  const [bulkResult, setBulkResult] = useState('');
  const [bulkDeleting, setBulkDeleting] = useState(false);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getRuns({ project, branch, appVersion: version || undefined, result, page, pageSize: 50 });
      setRuns(data.items);
      setTotal(data.total);
    } catch (e) {
      console.error('Failed to load data:', e);
    }
    setLoading(false);
  }, [result, project, branch, version, page, activeProject]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const fetchMeta = useCallback(() => {
    getProjects().then(setProjects).catch(() => {});
    getVersions().then(setVersions).catch(() => {});
    getStats().then(setStats).catch(() => {});
    getStorage().then(setStorage).catch(() => {});
  }, []);

  useEffect(() => { fetchMeta(); }, [fetchMeta]);

  useEffect(() => {
    if (project) getBranches(project).then(setBranches).catch(() => {});
    else setBranches([]);
  }, [project]);

  const handleUpload = async () => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.zip';
    input.multiple = true;
    input.onchange = async () => {
      if (!input.files) return;
      for (const file of Array.from(input.files)) {
        try {
          await uploadSession(file);
        } catch (e) {
          console.error('Upload failed:', e);
        }
      }
      fetchData();
    };
    input.click();
  };

  const handleBulkDelete = async () => {
    const params: { olderThan?: string; project?: string; result?: string } = {};
    if (bulkAge) {
      const d = new Date();
      d.setDate(d.getDate() - parseInt(bulkAge));
      params.olderThan = d.toISOString();
    }
    if (bulkProject) params.project = bulkProject;
    if (bulkResult) params.result = bulkResult;

    const desc = [
      bulkAge ? `older than ${bulkAge} days` : '',
      bulkProject ? `project "${bulkProject}"` : '',
      bulkResult ? `result "${bulkResult}"` : '',
    ].filter(Boolean).join(', ');

    if (!confirm(`Delete all sessions ${desc || '(no filters — this deletes EVERYTHING)'}?`)) return;

    setBulkDeleting(true);
    try {
      const { deleted } = await bulkDeleteSessions(params);
      alert(`Deleted ${deleted} session(s)`);
      setShowBulkDelete(false);
      fetchData();
      fetchMeta();
    } catch (e) {
      console.error('Bulk delete failed:', e);
      alert('Bulk delete failed');
    }
    setBulkDeleting(false);
  };

  const handleDeleteRun = async (runId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!confirm('Delete this run and all its sessions?')) return;
    await deleteRun(runId);
    fetchData();
    fetchMeta();
  };

  const handleExpandRun = async (runId: string) => {
    if (expandedRunId === runId) {
      setExpandedRunId(null);
      setExpandedSessions([]);
      return;
    }
    setExpandedRunId(runId);
    setLoadingExpanded(true);
    try {
      const sessions = await getRunSessions(runId);
      setExpandedSessions(sessions);
    } catch (e) {
      console.error('Failed to load run sessions:', e);
      setExpandedSessions([]);
    }
    setLoadingExpanded(false);
  };

  const totalPages = Math.ceil(total / 50);

  return (
    <div className="min-h-screen" style={{ background: 'var(--bg-primary)' }}>
      {/* Header */}
      <div className="border-b px-6 py-3 flex items-center justify-between" style={{ borderColor: 'var(--border-color)', background: 'var(--bg-secondary)' }}>
        <div className="flex items-center gap-4">
          <h1 className="text-lg font-semibold" style={{ color: 'var(--text-primary)' }}>
            UIAutomation Test Results
          </h1>
          {authProjects.length > 0 && (
            <select
              value={activeProject?.id || ''}
              onChange={e => {
                const proj = authProjects.find(p => p.id === e.target.value) || null;
                setActiveProject(proj);
              }}
              className="px-2 py-1 rounded text-sm border outline-none"
              style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
            >
              <option value="">All Projects</option>
              {authProjects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          )}
        </div>
        <div className="flex items-center gap-2">
          {storage && (
            <span className="text-xs font-mono px-2 py-1 rounded" style={{ color: 'var(--text-muted)', background: 'var(--bg-tertiary)' }}>
              {storage.totalGB >= 1 ? `${storage.totalGB} GB` : `${storage.totalMB} MB`} ({storage.fileCount} zips)
            </span>
          )}
          <button
            onClick={() => setShowBulkDelete(!showBulkDelete)}
            className="px-3 py-1.5 rounded text-sm font-medium border"
            style={{ borderColor: 'var(--accent-red)', color: 'var(--accent-red)', background: 'transparent' }}
          >
            Bulk Delete
          </button>
          <button
            onClick={handleUpload}
            className="px-3 py-1.5 rounded text-sm font-medium border"
            style={{ borderColor: 'var(--accent-blue)', color: 'var(--accent-blue)', background: 'transparent' }}
          >
            Upload .zip
          </button>
          <UserMenu />
        </div>
      </div>

      {/* Bulk delete panel */}
      {showBulkDelete && (
        <div className="mx-6 mt-4 p-4 rounded border" style={{ background: 'var(--bg-secondary)', borderColor: 'var(--accent-red)' }}>
          <div className="flex items-center gap-2 mb-3">
            <span className="text-sm font-medium" style={{ color: 'var(--accent-red)' }}>Bulk Delete</span>
            <button
              onClick={() => setShowBulkDelete(false)}
              className="ml-auto text-xs opacity-50 hover:opacity-100"
              style={{ color: 'var(--text-secondary)' }}
            >
              Close
            </button>
          </div>
          <div className="flex gap-3 items-end flex-wrap">
            <div>
              <label className="block text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Older than</label>
              <select
                value={bulkAge}
                onChange={e => setBulkAge(e.target.value)}
                className="px-2 py-1 rounded text-sm border outline-none"
                style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
              >
                <option value="">Any age</option>
                <option value="7">7 days</option>
                <option value="14">14 days</option>
                <option value="30">30 days</option>
                <option value="60">60 days</option>
                <option value="90">90 days</option>
                <option value="180">180 days</option>
                <option value="365">1 year</option>
              </select>
            </div>
            <div>
              <label className="block text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Project</label>
              <select
                value={bulkProject}
                onChange={e => setBulkProject(e.target.value)}
                className="px-2 py-1 rounded text-sm border outline-none"
                style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
              >
                <option value="">All Projects</option>
                {projects.map(p => <option key={p} value={p}>{p}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Result</label>
              <select
                value={bulkResult}
                onChange={e => setBulkResult(e.target.value)}
                className="px-2 py-1 rounded text-sm border outline-none"
                style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
              >
                <option value="">All Results</option>
                <option value="pass">Pass only</option>
                <option value="fail">Fail only</option>
                <option value="warn">Warn only</option>
              </select>
            </div>
            <button
              onClick={handleBulkDelete}
              disabled={bulkDeleting}
              className="px-4 py-1 rounded text-sm font-medium border disabled:opacity-50"
              style={{ borderColor: 'var(--accent-red)', color: '#fff', background: 'rgba(255, 77, 77, 0.3)' }}
            >
              {bulkDeleting ? 'Deleting...' : 'Delete Matching Sessions'}
            </button>
          </div>
          <p className="text-xs mt-2" style={{ color: 'var(--text-muted)' }}>
            Tip: Delete old passing tests to save disk space. Failed tests are usually worth keeping longer. Runs with no remaining sessions are automatically removed.
          </p>
        </div>
      )}

      {/* Stats cards */}
      {stats && (
        <div className="grid grid-cols-4 gap-4 px-6 py-4">
          <StatCard label="Total Runs" value={stats.total} />
          <StatCard label="Passed" value={stats.passed} color="var(--accent-green)" />
          <StatCard label="Failed" value={stats.failed} color="var(--accent-red)" />
          <StatCard label="Pass Rate" value={stats.total > 0 ? `${Math.round(stats.passed / stats.total * 100)}%` : '-'} color="var(--accent-blue)" />
        </div>
      )}

      {/* Filters */}
      <div className="flex gap-3 px-6 py-2 flex-wrap items-center" style={{ borderBottom: `1px solid var(--border-color)` }}>
        <select
          value={project}
          onChange={e => { setProject(e.target.value); setBranch(''); setPage(1); }}
          className="px-2 py-1 rounded text-sm border outline-none"
          style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
        >
          <option value="">All Projects</option>
          {projects.map(p => <option key={p} value={p}>{p}</option>)}
        </select>
        <select
          value={branch}
          onChange={e => { setBranch(e.target.value); setPage(1); }}
          className="px-2 py-1 rounded text-sm border outline-none"
          style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
          disabled={!project}
        >
          <option value="">All Branches</option>
          {branches.map(b => <option key={b} value={b}>{b}</option>)}
        </select>
        <div className="flex gap-1 items-center">
          <select
            value={version}
            onChange={e => { setVersion(e.target.value); setPage(1); }}
            className="px-2 py-1 rounded text-sm border outline-none"
            style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
          >
            <option value="">All Versions</option>
            {versions.map(v => <option key={v} value={v}>{v}</option>)}
          </select>
          {versions.length > 0 && (
            <button
              onClick={() => { setVersion(versions[0]); setPage(1); }}
              className={`px-2 py-1 rounded text-xs font-medium border ${version === versions[0] ? 'opacity-100' : 'opacity-50 hover:opacity-75'}`}
              style={{ borderColor: 'var(--accent-blue)', color: 'var(--accent-blue)', background: 'transparent' }}
            >
              Latest
            </button>
          )}
        </div>
        <div className="flex gap-1">
          {['', 'pass', 'fail', 'warn', 'running'].map(r => (
            <button
              key={r}
              onClick={() => { setResult(r); setPage(1); }}
              className={`px-2 py-1 rounded text-xs font-medium border ${result === r ? 'opacity-100' : 'opacity-50 hover:opacity-75'}`}
              style={{
                borderColor: r === 'pass' ? 'var(--accent-green)' : r === 'fail' ? 'var(--accent-red)' : r === 'warn' ? 'var(--accent-orange)' : r === 'running' ? 'var(--accent-blue)' : 'var(--border-color)',
                color: r === 'pass' ? 'var(--accent-green)' : r === 'fail' ? 'var(--accent-red)' : r === 'warn' ? 'var(--accent-orange)' : r === 'running' ? 'var(--accent-blue)' : 'var(--text-secondary)',
                background: 'transparent',
              }}
            >
              {r || 'All'}
            </button>
          ))}
        </div>
        <span className="ml-auto text-xs" style={{ color: 'var(--text-muted)' }}>
          {total} session{total !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Table */}
      <div className="px-6 overflow-auto">
        <RunsTable
          runs={runs}
          loading={loading}
          expandedRunId={expandedRunId}
          expandedSessions={expandedSessions}
          loadingExpanded={loadingExpanded}
          onExpandRun={handleExpandRun}
          onSessionClick={id => navigate(`/session/${id}`)}
          onDeleteRun={handleDeleteRun}
        />
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex justify-center gap-2 py-4">
          <button
            disabled={page <= 1}
            onClick={() => setPage(p => p - 1)}
            className="px-3 py-1 rounded text-sm border disabled:opacity-30"
            style={{ borderColor: 'var(--border-color)', color: 'var(--text-secondary)', background: 'var(--bg-tertiary)' }}
          >
            Prev
          </button>
          <span className="px-3 py-1 text-sm" style={{ color: 'var(--text-muted)' }}>
            Page {page} of {totalPages}
          </span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage(p => p + 1)}
            className="px-3 py-1 rounded text-sm border disabled:opacity-30"
            style={{ borderColor: 'var(--border-color)', color: 'var(--text-secondary)', background: 'var(--bg-tertiary)' }}
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}

function RunsTable({ runs, loading, expandedRunId, expandedSessions, loadingExpanded, onExpandRun, onSessionClick, onDeleteRun }: {
  runs: RunSummary[];
  loading: boolean;
  expandedRunId: string | null;
  expandedSessions: TestSession[];
  loadingExpanded: boolean;
  onExpandRun: (runId: string) => void;
  onSessionClick: (id: string) => void;
  onDeleteRun: (runId: string, e: React.MouseEvent) => void;
}) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr style={{ color: 'var(--text-secondary)', borderBottom: `1px solid var(--border-color)` }}>
          <th className="text-left py-2 px-2 font-medium w-6"></th>
          <th className="text-left py-2 px-2 font-medium">Result</th>
          <th className="text-left py-2 px-2 font-medium">Tests</th>
          <th className="text-left py-2 px-2 font-medium">Project</th>
          <th className="text-left py-2 px-2 font-medium">Branch</th>
          <th className="text-left py-2 px-2 font-medium">Version</th>
          <th className="text-right py-2 px-2 font-medium">Duration</th>
          <th className="text-right py-2 px-2 font-medium">Time</th>
          <th className="text-right py-2 px-2 font-medium w-8"></th>
        </tr>
      </thead>
      <tbody>
        {loading ? (
          <tr><td colSpan={9} className="text-center py-8" style={{ color: 'var(--text-muted)' }}>Loading...</td></tr>
        ) : runs.length === 0 ? (
          <tr><td colSpan={9} className="text-center py-8" style={{ color: 'var(--text-muted)' }}>No sessions found</td></tr>
        ) : runs.map(run => (
          <RunRow
            key={run.runId}
            run={run}
            isExpanded={expandedRunId === run.runId}
            expandedSessions={expandedRunId === run.runId ? expandedSessions : []}
            loadingExpanded={expandedRunId === run.runId && loadingExpanded}
            onExpand={() => onExpandRun(run.runId)}
            onSessionClick={onSessionClick}
            onDelete={(e) => onDeleteRun(run.runId, e)}
          />
        ))}
      </tbody>
    </table>
  );
}

function RunRow({ run, isExpanded, expandedSessions, loadingExpanded, onExpand, onSessionClick, onDelete }: {
  run: RunSummary;
  isExpanded: boolean;
  expandedSessions: TestSession[];
  loadingExpanded: boolean;
  onExpand: () => void;
  onSessionClick: (id: string) => void;
  onDelete: (e: React.MouseEvent) => void;
}) {
  return (
    <>
      <tr
        onClick={onExpand}
        className="cursor-pointer hover:opacity-80"
        style={{ borderBottom: isExpanded ? 'none' : `1px solid var(--border-color)` }}
        onMouseEnter={e => (e.currentTarget.style.background = 'var(--bg-hover)')}
        onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
      >
        <td className="py-2 px-2" style={{ color: 'var(--text-muted)' }}>
          <span style={{ display: 'inline-block', transform: isExpanded ? 'rotate(90deg)' : 'none', transition: 'transform 0.15s' }}>
            &#9654;
          </span>
        </td>
        <td className="py-2 px-2">
          <span className={`px-2 py-0.5 rounded text-xs font-medium border ${resultBadge(run.result)}`}>
            {run.result.toUpperCase()}
          </span>
        </td>
        <td className="py-2 px-2" style={{ color: 'var(--text-primary)' }}>
          <span>{runTestSummary(run)}</span>
        </td>
        <td className="py-2 px-2" style={{ color: 'var(--text-secondary)' }}>{run.project || '-'}</td>
        <td className="py-2 px-2" style={{ color: 'var(--text-secondary)' }}>{run.branch || '-'}</td>
        <td className="py-2 px-2 font-mono" style={{ color: 'var(--text-muted)' }}>{run.appVersion || '-'}</td>
        <td className="py-2 px-2 text-right font-mono" style={{ color: 'var(--text-muted)' }}>{formatDuration(run.totalDuration)}</td>
        <td className="py-2 px-2 text-right" style={{ color: 'var(--text-muted)' }}>{timeAgo(run.startedAt)}</td>
        <td className="py-2 px-2 text-right">
          <button
            onClick={onDelete}
            className="opacity-30 hover:opacity-100 text-red-400"
            title="Delete run"
          >
            x
          </button>
        </td>
      </tr>
      {isExpanded && (
        <tr>
          <td colSpan={9} style={{ padding: 0, background: 'var(--bg-secondary)' }}>
            {loadingExpanded ? (
              <div className="py-4 text-center text-sm" style={{ color: 'var(--text-muted)' }}>Loading sessions...</div>
            ) : expandedSessions.length === 0 ? (
              <div className="py-4 text-center text-sm" style={{ color: 'var(--text-muted)' }}>No sessions in this run</div>
            ) : (
              <div className="pl-6 border-l-2" style={{ borderColor: 'var(--accent-blue)', marginLeft: '8px' }}>
                <table className="w-full text-sm">
                  <tbody>
                    {expandedSessions.map(s => (
                      <tr
                        key={s.id}
                        onClick={() => onSessionClick(s.id)}
                        className="cursor-pointer hover:opacity-80"
                        style={{ borderBottom: `1px solid var(--border-color)` }}
                        onMouseEnter={e => (e.currentTarget.style.background = 'var(--bg-hover)')}
                        onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                      >
                        <td className="py-1.5 px-2">
                          <span className={`px-2 py-0.5 rounded text-xs font-medium border ${resultBadge(s.result)}`}>
                            {s.result.toUpperCase()}
                          </span>
                        </td>
                        <td className="py-1.5 px-2">
                          <div className="font-mono" style={{ color: 'var(--text-primary)' }}>{s.testName}</div>
                          {s.failureMessage && (
                            <div className="text-xs mt-0.5 truncate" style={{ color: 'var(--accent-red)', maxWidth: 500, opacity: 0.8 }}>
                              {s.failureMessage}
                            </div>
                          )}
                        </td>
                        <td className="py-1.5 px-2 text-right font-mono" style={{ color: 'var(--text-muted)' }}>{formatDuration(s.duration)}</td>
                        <td className="py-1.5 px-2 text-right" style={{ color: 'var(--text-muted)' }}>{s.eventCount} events</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

function StatCard({ label, value, color }: { label: string; value: string | number; color?: string }) {
  return (
    <div className="rounded p-3 border" style={{ background: 'var(--bg-secondary)', borderColor: 'var(--border-color)' }}>
      <div className="text-xs mb-1" style={{ color: 'var(--text-muted)' }}>{label}</div>
      <div className="text-xl font-semibold" style={{ color: color || 'var(--text-primary)' }}>{value}</div>
    </div>
  );
}
