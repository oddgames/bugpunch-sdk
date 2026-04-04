import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth, authFetch, type Organization, type Project } from '../lib/auth';

/* ==================== Types ==================== */

interface OrgMember {
  userId: string;
  email: string;
  displayName: string;
  role: string;
}

interface ApiKeyInfo {
  id: string;
  key: string;
  label: string;
  createdAt: string;
}

type Tab = 'general' | 'members' | 'projects' | 'keys';

/* ==================== Main Page ==================== */

export function OrgPage() {
  const { user, orgs, activeOrg, setActiveOrg, refreshOrgs, projects, refreshProjects } = useAuth();
  const navigate = useNavigate();

  const [activeTab, setActiveTab] = useState<Tab>('general');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  // Members
  const [members, setMembers] = useState<OrgMember[]>([]);

  // Keys
  const [keys, setKeys] = useState<Record<string, ApiKeyInfo[]>>({});

  const isOrgAdmin = activeOrg?.role === 'admin' || user?.isAdmin;

  const toast = useCallback((msg: string, isError = true) => {
    if (isError) { setError(msg); setSuccess(''); }
    else { setSuccess(msg); setError(''); }
    setTimeout(() => { setError(''); setSuccess(''); }, 5000);
  }, []);

  const fetchMembers = useCallback(async () => {
    if (!activeOrg) return;
    try { setMembers(await authFetch<OrgMember[]>(`/api/orgs/${activeOrg.id}/members`)); }
    catch { setMembers([]); }
  }, [activeOrg]);

  const fetchKeys = useCallback(async () => {
    const result: Record<string, ApiKeyInfo[]> = {};
    for (const p of projects) {
      try { result[p.id] = await authFetch<ApiKeyInfo[]>(`/api/projects/${p.id}/keys`); }
      catch { result[p.id] = []; }
    }
    setKeys(result);
  }, [projects]);

  useEffect(() => { fetchMembers(); }, [fetchMembers]);
  useEffect(() => { fetchKeys(); }, [fetchKeys]);

  const allKeys = projects.flatMap(p =>
    (keys[p.id] || []).map(k => ({ ...k, projectName: p.name, projectId: p.id }))
  );

  return (
    <div className="h-screen flex flex-col" style={{ background: 'var(--bg-primary)' }}>
      {/* Header */}
      <header className="shrink-0 border-b px-5 h-11 flex items-center gap-3" style={{ borderColor: 'var(--border-color)', background: 'var(--bg-secondary)' }}>
        <button onClick={() => navigate('/')} className="hover:opacity-80 flex items-center" style={{ color: 'var(--text-muted)' }}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M15 18l-6-6 6-6"/></svg>
        </button>
        <span className="text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>Settings</span>
        <div className="flex-1" />
        {activeOrg && (
          <span className="text-xs" style={{ color: 'var(--text-muted)' }}>{activeOrg.name}</span>
        )}
      </header>

      {/* Toast */}
      {(error || success) && (
        <div className="mx-5 mt-3 px-3 py-2 rounded text-xs" style={{
          background: error ? 'rgba(255,77,77,0.12)' : 'rgba(77,204,77,0.12)',
          color: error ? 'var(--accent-red)' : 'var(--accent-green)',
          border: `1px solid ${error ? 'rgba(255,77,77,0.25)' : 'rgba(77,204,77,0.25)'}`,
        }}>
          {error || success}
        </div>
      )}

      {/* Mobile tabs */}
      <div className="md:hidden shrink-0 border-b flex overflow-x-auto" style={{ borderColor: 'var(--border-color)', background: 'var(--bg-secondary)' }}>
        {navItems.map(n => (
          <button key={n.id} onClick={() => setActiveTab(n.id)}
            className="shrink-0 px-4 py-2.5 text-xs whitespace-nowrap"
            style={{
              color: activeTab === n.id ? 'var(--text-primary)' : 'var(--text-muted)',
              borderBottom: `2px solid ${activeTab === n.id ? 'var(--accent-blue)' : 'transparent'}`,
            }}>
            {n.label}
          </button>
        ))}
      </div>

      <div className="flex flex-1 min-h-0">
        {/* Desktop sidebar */}
        <nav className="hidden md:block shrink-0 border-r overflow-y-auto" style={{ borderColor: 'var(--border-color)', width: 220 }}>
          <div className="p-3 flex flex-col gap-0.5">
            {navItems.map(n => (
              <button key={n.id} onClick={() => setActiveTab(n.id)}
                className="w-full text-left px-3 py-1.5 rounded text-xs flex items-center gap-2"
                style={{
                  color: activeTab === n.id ? 'var(--text-primary)' : 'var(--text-muted)',
                  background: activeTab === n.id ? 'var(--bg-tertiary)' : 'transparent',
                }}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><path d={n.icon}/></svg>
                {n.label}
              </button>
            ))}
          </div>
        </nav>

        {/* Content */}
        <main className="flex-1 overflow-y-auto">
          <div className="max-w-[720px] mx-auto px-6 py-6 max-sm:px-4 max-sm:py-4">

            {activeTab === 'general' && (
              <GeneralTab orgs={orgs} activeOrg={activeOrg} setActiveOrg={setActiveOrg} refreshOrgs={refreshOrgs} toast={toast} />
            )}

            {activeTab === 'members' && activeOrg && (
              <MembersTab org={activeOrg} members={members} isAdmin={!!isOrgAdmin} currentUserId={user?.id}
                onRefresh={fetchMembers} toast={toast} />
            )}

            {activeTab === 'projects' && activeOrg && (
              <ProjectsTab org={activeOrg} projects={projects} isAdmin={!!isOrgAdmin}
                onRefresh={refreshProjects} toast={toast} />
            )}

            {activeTab === 'keys' && activeOrg && (
              <KeysTab projects={projects} allKeys={allKeys} isAdmin={!!isOrgAdmin}
                onRefresh={fetchKeys} toast={toast} />
            )}

            {activeTab !== 'general' && !activeOrg && (
              <EmptyState message="Select an organization first" action="Go to General" onClick={() => setActiveTab('general')} />
            )}
          </div>
        </main>
      </div>
    </div>
  );
}

/* ==================== Navigation ==================== */

const navItems: { id: Tab; label: string; icon: string }[] = [
  { id: 'general', label: 'General', icon: 'M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z M15 12a3 3 0 11-6 0 3 3 0 016 0z' },
  { id: 'members', label: 'Members', icon: 'M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z' },
  { id: 'projects', label: 'Projects', icon: 'M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z' },
  { id: 'keys', label: 'API Keys', icon: 'M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z' },
];

/* ==================== General Tab ==================== */

function GeneralTab({ orgs, activeOrg, setActiveOrg, refreshOrgs, toast }: {
  orgs: Organization[]; activeOrg: Organization | null;
  setActiveOrg: (o: Organization) => void;
  refreshOrgs: () => Promise<Organization[]>;
  toast: (msg: string, isError?: boolean) => void;
}) {
  const [newName, setNewName] = useState('');
  const [busy, setBusy] = useState(false);

  const handleCreate = async () => {
    if (!newName.trim()) return;
    setBusy(true);
    try {
      await authFetch('/api/orgs', { method: 'POST', body: JSON.stringify({ name: newName.trim() }) });
      const updated = await refreshOrgs();
      const created = (updated as Organization[])?.find(o => o.name === newName.trim());
      if (created) setActiveOrg(created);
      setNewName('');
      toast('Organization created', false);
    } catch (e: any) { toast(e.message); }
    setBusy(false);
  };

  return (
    <>
      <PageHeader title="General" subtitle="Manage your organizations." />

      <Card>
        <CardHeader title="Organizations" description="Switch between organizations or create a new one." />
        <div className="flex flex-col">
          {orgs.map((org, i) => (
            <button key={org.id} onClick={() => setActiveOrg(org)}
              className="flex items-center gap-3 px-4 py-3 text-left hover:opacity-90 transition-opacity"
              style={{
                background: org.id === activeOrg?.id ? 'var(--bg-tertiary)' : 'transparent',
                borderTop: i > 0 ? '1px solid var(--border-color)' : undefined,
              }}>
              <Avatar name={org.name} active={org.id === activeOrg?.id} />
              <div className="flex-1 min-w-0">
                <div className="text-sm" style={{ color: 'var(--text-primary)' }}>{org.name}</div>
                <div className="text-xs" style={{ color: 'var(--text-muted)' }}>{org.role}</div>
              </div>
              {org.id === activeOrg?.id && <Badge text="Active" color="blue" />}
            </button>
          ))}
        </div>
        <CardFooter>
          <Input value={newName} onChange={setNewName} placeholder="New organization name"
            onSubmit={handleCreate} />
          <Btn onClick={handleCreate} disabled={busy || !newName.trim()}>Create</Btn>
        </CardFooter>
      </Card>
    </>
  );
}

/* ==================== Members Tab ==================== */

function MembersTab({ org, members, isAdmin, currentUserId, onRefresh, toast }: {
  org: Organization; members: OrgMember[]; isAdmin: boolean; currentUserId?: string;
  onRefresh: () => void; toast: (msg: string, isError?: boolean) => void;
}) {
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState('member');
  const [busy, setBusy] = useState(false);

  const handleInvite = async () => {
    if (!email.trim()) return;
    setBusy(true);
    try {
      await authFetch(`/api/orgs/${org.id}/members`, {
        method: 'POST',
        body: JSON.stringify({
          email: email.trim(), role,
          password: password || undefined,
          displayName: displayName || undefined,
        }),
      });
      setEmail(''); setPassword(''); setDisplayName('');
      onRefresh();
      toast('Member added', false);
    } catch (e: any) { toast(e.message); }
    setBusy(false);
  };

  const handleRemove = async (userId: string) => {
    if (!confirm('Remove this member from the organization?')) return;
    try {
      await authFetch(`/api/orgs/${org.id}/members/${userId}`, { method: 'DELETE' });
      onRefresh();
      toast('Member removed', false);
    } catch (e: any) { toast(e.message); }
  };

  return (
    <>
      <PageHeader title="Members" subtitle={`${members.length} member${members.length !== 1 ? 's' : ''} in ${org.name}`} />

      <Card>
        <table className="w-full">
          <thead>
            <tr style={{ borderBottom: '1px solid var(--border-color)' }}>
              <Th>User</Th>
              <Th align="right">Role</Th>
              {isAdmin && <Th align="right" width={40} />}
            </tr>
          </thead>
          <tbody>
            {members.map(m => (
              <tr key={m.userId} className="group" style={{ borderBottom: '1px solid var(--border-color)' }}>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-3">
                    <Avatar name={m.displayName || m.email} size={28} />
                    <div>
                      <div className="text-sm" style={{ color: 'var(--text-primary)' }}>{m.displayName}</div>
                      <div className="text-xs" style={{ color: 'var(--text-muted)' }}>{m.email}</div>
                    </div>
                  </div>
                </td>
                <td className="px-4 py-3 text-right">
                  <Badge text={m.role} color={m.role === 'admin' ? 'blue' : 'gray'} />
                </td>
                {isAdmin && (
                  <td className="px-4 py-3 text-right">
                    {m.userId !== currentUserId && (
                      <DangerBtn onClick={() => handleRemove(m.userId)} title="Remove member">
                        <XIcon />
                      </DangerBtn>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>

        {isAdmin && (
          <CardFooter>
            <div className="flex-1 flex flex-col gap-2">
              <div className="text-xs" style={{ color: 'var(--text-muted)' }}>
                Invite a member — provide a password to create a new account
              </div>
              <div className="flex gap-2 flex-wrap">
                <Input value={email} onChange={setEmail} placeholder="Email" className="flex-1 min-w-[180px]" />
                <Input value={displayName} onChange={setDisplayName} placeholder="Display name" style={{ width: 130 }} />
              </div>
              <div className="flex gap-2 flex-wrap items-center">
                <Input value={password} onChange={setPassword} placeholder="Password (new accounts)" type="password"
                  className="flex-1 min-w-[180px]" onSubmit={handleInvite} />
                <select value={role} onChange={e => setRole(e.target.value)}
                  className="h-8 px-2 rounded text-xs border outline-none"
                  style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}>
                  <option value="member">Member</option>
                  <option value="admin">Admin</option>
                </select>
                <Btn onClick={handleInvite} disabled={busy || !email.trim()}>Invite</Btn>
              </div>
            </div>
          </CardFooter>
        )}
      </Card>
    </>
  );
}

/* ==================== Projects Tab ==================== */

function ProjectsTab({ org, projects, isAdmin, onRefresh, toast }: {
  org: Organization; projects: Project[]; isAdmin: boolean;
  onRefresh: () => Promise<void>; toast: (msg: string, isError?: boolean) => void;
}) {
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);

  const handleCreate = async () => {
    if (!name.trim()) return;
    setBusy(true);
    try {
      await authFetch(`/api/orgs/${org.id}/projects`, { method: 'POST', body: JSON.stringify({ name: name.trim() }) });
      setName('');
      await onRefresh();
      toast('Project created', false);
    } catch (e: any) { toast(e.message); }
    setBusy(false);
  };

  const handleDelete = async (id: string, projectName: string) => {
    if (!confirm(`Delete "${projectName}" and all its API keys?`)) return;
    try {
      await authFetch(`/api/projects/${id}`, { method: 'DELETE' });
      await onRefresh();
      toast('Project deleted', false);
    } catch (e: any) { toast(e.message); }
  };

  return (
    <>
      <PageHeader title="Projects" subtitle="Projects group test sessions and API keys. Each project gets a default key on creation." />

      <Card>
        {projects.length > 0 ? (
          <table className="w-full">
            <thead>
              <tr style={{ borderBottom: '1px solid var(--border-color)' }}>
                <Th>Name</Th>
                <Th align="right">Created</Th>
                {isAdmin && <Th align="right" width={40} />}
              </tr>
            </thead>
            <tbody>
              {projects.map(p => (
                <tr key={p.id} style={{ borderBottom: '1px solid var(--border-color)' }}>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" strokeWidth="1.5"><path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z"/></svg>
                      <span className="text-sm" style={{ color: 'var(--text-primary)' }}>{p.name}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-right text-xs" style={{ color: 'var(--text-muted)' }}>
                    {new Date(p.createdAt).toLocaleDateString()}
                  </td>
                  {isAdmin && (
                    <td className="px-4 py-3 text-right">
                      <DangerBtn onClick={() => handleDelete(p.id, p.name)} title="Delete project">
                        <TrashIcon />
                      </DangerBtn>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <div className="px-4 py-8 text-center text-xs" style={{ color: 'var(--text-muted)' }}>
            No projects yet
          </div>
        )}

        {isAdmin && (
          <CardFooter>
            <Input value={name} onChange={setName} placeholder="Project name" onSubmit={handleCreate} />
            <Btn onClick={handleCreate} disabled={busy || !name.trim()}>Create Project</Btn>
          </CardFooter>
        )}
      </Card>
    </>
  );
}

/* ==================== API Keys Tab ==================== */

function KeysTab({ projects, allKeys, isAdmin, onRefresh, toast }: {
  projects: Project[];
  allKeys: (ApiKeyInfo & { projectName: string; projectId: string })[];
  isAdmin: boolean;
  onRefresh: () => Promise<void>;
  toast: (msg: string, isError?: boolean) => void;
}) {
  const [selectedProject, setSelectedProject] = useState<string>(projects[0]?.id || '');
  const [label, setLabel] = useState('');
  const [busy, setBusy] = useState(false);
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [revealedId, setRevealedId] = useState<string | null>(null);

  useEffect(() => {
    if (projects.length > 0 && !projects.find(p => p.id === selectedProject))
      setSelectedProject(projects[0].id);
  }, [projects, selectedProject]);

  const filtered = selectedProject ? allKeys.filter(k => k.projectId === selectedProject) : allKeys;

  const handleCreate = async () => {
    if (!label.trim() || !selectedProject) return;
    setBusy(true);
    try {
      await authFetch(`/api/projects/${selectedProject}/keys`, {
        method: 'POST', body: JSON.stringify({ label: label.trim() }),
      });
      setLabel('');
      await onRefresh();
      toast('API key created', false);
    } catch (e: any) { toast(e.message); }
    setBusy(false);
  };

  const handleDelete = async (keyId: string, keyLabel: string) => {
    if (!confirm(`Revoke "${keyLabel}"? Clients using this key will stop working.`)) return;
    try {
      await authFetch(`/api/keys/${keyId}`, { method: 'DELETE' });
      await onRefresh();
      toast('API key revoked', false);
    } catch (e: any) { toast(e.message); }
  };

  const copy = (id: string, value: string) => {
    navigator.clipboard.writeText(value);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const maskKey = (key: string) => {
    if (key.length <= 12) return key;
    return key.slice(0, 8) + '\u2022'.repeat(24) + key.slice(-4);
  };

  return (
    <>
      <PageHeader title="API Keys" subtitle="Keys authenticate Unity clients for uploading test sessions. Each key is scoped to a single project." />

      {/* Project filter tabs */}
      {projects.length > 1 && (
        <div className="flex gap-1 mb-4 flex-wrap">
          {projects.map(p => (
            <button key={p.id} onClick={() => setSelectedProject(p.id)}
              className="px-3 py-1 rounded-full text-xs transition-colors"
              style={{
                background: selectedProject === p.id ? 'var(--bg-tertiary)' : 'transparent',
                color: selectedProject === p.id ? 'var(--text-primary)' : 'var(--text-muted)',
                border: `1px solid ${selectedProject === p.id ? 'var(--border-color)' : 'transparent'}`,
              }}>
              {p.name}
            </button>
          ))}
        </div>
      )}

      <Card>
        {filtered.length > 0 ? (
          <table className="w-full">
            <thead>
              <tr style={{ borderBottom: '1px solid var(--border-color)' }}>
                <Th>Label</Th>
                <Th>Key</Th>
                <Th align="right">Created</Th>
                <Th align="right" width={80} />
              </tr>
            </thead>
            <tbody>
              {filtered.map(k => (
                <tr key={k.id} style={{ borderBottom: '1px solid var(--border-color)' }}>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="var(--accent-blue)" strokeWidth="2"><path d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z"/></svg>
                      <span className="text-xs font-medium" style={{ color: 'var(--text-primary)' }}>{k.label}</span>
                      {projects.length > 1 && (
                        <span className="text-[10px] px-1.5 py-0.5 rounded" style={{ background: 'var(--bg-hover)', color: 'var(--text-muted)' }}>
                          {k.projectName}
                        </span>
                      )}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <code className="font-mono text-xs select-all" style={{ color: 'var(--text-secondary)' }}>
                      {revealedId === k.id ? k.key : maskKey(k.key)}
                    </code>
                  </td>
                  <td className="px-4 py-3 text-right text-xs whitespace-nowrap" style={{ color: 'var(--text-muted)' }}>
                    {new Date(k.createdAt).toLocaleDateString()}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-1">
                      <IconBtn onClick={() => setRevealedId(revealedId === k.id ? null : k.id)}
                        title={revealedId === k.id ? 'Hide key' : 'Reveal key'}>
                        {revealedId === k.id ? <EyeOffIcon /> : <EyeIcon />}
                      </IconBtn>
                      <IconBtn onClick={() => copy(k.id, k.key)}
                        title={copiedId === k.id ? 'Copied!' : 'Copy key'}
                        style={copiedId === k.id ? { color: 'var(--accent-green)' } : undefined}>
                        {copiedId === k.id ? <CheckIcon /> : <CopyIcon />}
                      </IconBtn>
                      {isAdmin && (
                        <DangerBtn onClick={() => handleDelete(k.id, k.label)} title="Revoke key">
                          <TrashIcon />
                        </DangerBtn>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <div className="px-4 py-8 text-center text-xs" style={{ color: 'var(--text-muted)' }}>
            {projects.length === 0 ? 'Create a project first' : 'No API keys for this project'}
          </div>
        )}

        {isAdmin && selectedProject && (
          <CardFooter>
            <Input value={label} onChange={setLabel}
              placeholder={`Key label (e.g. "CI Server")`}
              onSubmit={handleCreate} />
            <Btn onClick={handleCreate} disabled={busy || !label.trim()}>
              {busy ? 'Creating...' : 'Create Key'}
            </Btn>
          </CardFooter>
        )}
      </Card>
    </>
  );
}

/* ==================== Shared UI Components ==================== */

function PageHeader({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="mb-5">
      <h2 className="text-lg font-semibold" style={{ color: 'var(--text-primary)' }}>{title}</h2>
      <p className="text-xs mt-0.5" style={{ color: 'var(--text-muted)' }}>{subtitle}</p>
    </div>
  );
}

function Card({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-lg border overflow-hidden" style={{ borderColor: 'var(--border-color)', background: 'var(--bg-secondary)' }}>
      {children}
    </div>
  );
}

function CardHeader({ title, description }: { title: string; description?: string }) {
  return (
    <div className="px-4 py-3" style={{ borderBottom: '1px solid var(--border-color)' }}>
      <div className="text-sm font-medium" style={{ color: 'var(--text-primary)' }}>{title}</div>
      {description && <div className="text-xs mt-0.5" style={{ color: 'var(--text-muted)' }}>{description}</div>}
    </div>
  );
}

function CardFooter({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-end gap-2 px-4 py-3 flex-wrap" style={{ borderTop: '1px solid var(--border-color)', background: 'var(--bg-primary)' }}>
      {children}
    </div>
  );
}

function Th({ children, align, width }: { children?: React.ReactNode; align?: 'left' | 'right'; width?: number }) {
  return (
    <th className="px-4 py-2 text-xs font-medium" style={{
      color: 'var(--text-muted)', textAlign: align || 'left', width: width ? `${width}px` : undefined,
    }}>
      {children}
    </th>
  );
}

function Avatar({ name, active, size = 32 }: { name: string; active?: boolean; size?: number }) {
  return (
    <span className="rounded-full flex items-center justify-center font-bold shrink-0" style={{
      width: size, height: size, fontSize: size * 0.38,
      background: active ? 'rgba(128,179,255,0.15)' : 'var(--bg-tertiary)',
      color: active ? 'var(--accent-blue)' : 'var(--text-muted)',
    }}>
      {name.charAt(0).toUpperCase()}
    </span>
  );
}

function Badge({ text, color }: { text: string; color: 'blue' | 'gray' | 'green' | 'red' }) {
  const colors = {
    blue: { bg: 'rgba(128,179,255,0.12)', fg: 'var(--accent-blue)' },
    gray: { bg: 'var(--bg-tertiary)', fg: 'var(--text-muted)' },
    green: { bg: 'rgba(77,204,77,0.12)', fg: 'var(--accent-green)' },
    red: { bg: 'rgba(255,77,77,0.12)', fg: 'var(--accent-red)' },
  };
  const c = colors[color];
  return (
    <span className="text-[10px] px-1.5 py-0.5 rounded font-medium" style={{ background: c.bg, color: c.fg }}>
      {text}
    </span>
  );
}

function Input({ value, onChange, placeholder, type, className, style, onSubmit }: {
  value: string; onChange: (v: string) => void; placeholder: string;
  type?: string; className?: string; style?: React.CSSProperties; onSubmit?: () => void;
}) {
  return (
    <input type={type || 'text'} value={value} onChange={e => onChange(e.target.value)}
      placeholder={placeholder}
      onKeyDown={onSubmit ? e => e.key === 'Enter' && onSubmit() : undefined}
      className={`h-8 px-3 rounded text-xs border outline-none focus:border-[var(--accent-blue)] transition-colors ${className || ''}`}
      style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)', ...style }} />
  );
}

function Btn({ children, onClick, disabled }: { children: React.ReactNode; onClick: () => void; disabled?: boolean }) {
  return (
    <button onClick={onClick} disabled={disabled}
      className="h-8 px-4 rounded text-xs font-medium disabled:opacity-30 transition-colors shrink-0"
      style={{ background: 'var(--accent-blue)', color: '#1a1a1a' }}>
      {children}
    </button>
  );
}

function IconBtn({ children, onClick, title, style }: {
  children: React.ReactNode; onClick: () => void; title: string; style?: React.CSSProperties;
}) {
  return (
    <button onClick={onClick} title={title}
      className="p-1 rounded opacity-50 hover:opacity-100 transition-opacity"
      style={{ color: 'var(--text-secondary)', ...style }}>
      {children}
    </button>
  );
}

function DangerBtn({ children, onClick, title }: { children: React.ReactNode; onClick: () => void; title: string }) {
  return (
    <button onClick={onClick} title={title}
      className="p-1 rounded opacity-30 hover:opacity-100 transition-opacity"
      style={{ color: 'var(--accent-red)' }}>
      {children}
    </button>
  );
}

function EmptyState({ message, action, onClick }: { message: string; action: string; onClick: () => void }) {
  return (
    <div className="text-center py-16">
      <p className="text-sm mb-2" style={{ color: 'var(--text-muted)' }}>{message}</p>
      <button onClick={onClick} className="text-xs underline" style={{ color: 'var(--accent-blue)' }}>{action}</button>
    </div>
  );
}

/* ==================== Icons ==================== */

const XIcon = () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M18 6L6 18M6 6l12 12"/></svg>;
const TrashIcon = () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"/></svg>;
const CopyIcon = () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/></svg>;
const CheckIcon = () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M20 6L9 17l-5-5"/></svg>;
const EyeIcon = () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>;
const EyeOffIcon = () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>;
