import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';

// Types
export interface AuthUser {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
}

export interface Organization {
  id: string;
  name: string;
  role: string;
  createdAt: string;
}

export interface Project {
  id: string;
  name: string;
  orgId: string;
  createdAt: string;
}

interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  orgs: Organization[];
  activeOrg: Organization | null;
  projects: Project[];
  activeProject: Project | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => void;
  setActiveOrg: (org: Organization) => void;
  setActiveProject: (project: Project | null) => void;
  refreshOrgs: () => Promise<Organization[]>;
  refreshProjects: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

const BASE_URL = import.meta.env.VITE_API_URL || '';

async function authFetch<T>(url: string, options?: RequestInit): Promise<T> {
  const token = localStorage.getItem('auth_token');
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...((options?.headers as Record<string, string>) || {}),
  };
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const activeProjectId = localStorage.getItem('active_project_id');
  if (activeProjectId) headers['X-Project-Id'] = activeProjectId;

  const res = await fetch(`${BASE_URL}${url}`, { ...options, headers });
  if (res.status === 401) {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('active_org_id');
    localStorage.removeItem('active_project_id');
    throw new Error('Unauthorized');
  }
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `HTTP ${res.status}`);
  }
  if (res.status === 204 || res.headers.get('content-length') === '0') return {} as T;
  return res.json();
}

export { authFetch };

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string | null>(localStorage.getItem('auth_token'));
  const [orgs, setOrgs] = useState<Organization[]>([]);
  const [activeOrg, setActiveOrgState] = useState<Organization | null>(null);
  const [projects, setProjects] = useState<Project[]>([]);
  const [activeProject, setActiveProjectState] = useState<Project | null>(null);
  const [loading, setLoading] = useState(true);

  const refreshOrgs = useCallback(async () => {
    try {
      const data = await authFetch<Organization[]>('/api/orgs');
      setOrgs(data);
      return data;
    } catch {
      setOrgs([]);
      return [];
    }
  }, []);

  const refreshProjects = useCallback(async () => {
    if (!activeOrg) {
      setProjects([]);
      return;
    }
    try {
      const data = await authFetch<Project[]>(`/api/orgs/${activeOrg.id}/projects`);
      setProjects(data);
    } catch {
      setProjects([]);
    }
  }, [activeOrg]);

  // Load projects when active org changes
  useEffect(() => {
    if (activeOrg && token) {
      refreshProjects();
    }
  }, [activeOrg, token, refreshProjects]);

  // Restore session on mount
  useEffect(() => {
    if (!token) {
      setLoading(false);
      return;
    }

    (async () => {
      try {
        const me = await authFetch<{
          id: string;
          email: string;
          displayName: string;
          isAdmin: boolean;
          organizations: Organization[];
        }>('/api/auth/me');

        setUser({ id: me.id, email: me.email, displayName: me.displayName, isAdmin: me.isAdmin });
        setOrgs(me.organizations);

        // Restore active org
        const savedOrgId = localStorage.getItem('active_org_id');
        const org = me.organizations.find(o => o.id === savedOrgId) || me.organizations[0] || null;
        if (org) setActiveOrgState(org);
      } catch {
        // Token invalid
        setToken(null);
        localStorage.removeItem('auth_token');
      }
      setLoading(false);
    })();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Restore active project when projects load
  useEffect(() => {
    if (projects.length > 0) {
      const savedId = localStorage.getItem('active_project_id');
      const proj = projects.find(p => p.id === savedId) || null;
      setActiveProjectState(proj);
    } else {
      setActiveProjectState(null);
    }
  }, [projects]);

  const login = async (email: string, password: string) => {
    const data = await authFetch<{
      token: string;
      user: AuthUser;
      organizations: Organization[];
    }>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    });

    localStorage.setItem('auth_token', data.token);
    setToken(data.token);
    setUser(data.user);
    setOrgs(data.organizations);

    const org = data.organizations[0] || null;
    if (org) {
      setActiveOrgState(org);
      localStorage.setItem('active_org_id', org.id);
    }
  };

  const register = async (email: string, password: string, displayName: string) => {
    const data = await authFetch<{
      token: string;
      user: AuthUser;
      organizations: Organization[];
    }>('/api/auth/setup', {
      method: 'POST',
      body: JSON.stringify({ email, password, displayName }),
    });

    localStorage.setItem('auth_token', data.token);
    setToken(data.token);
    setUser(data.user);
    setOrgs(data.organizations);

    const org = data.organizations[0] || null;
    if (org) {
      setActiveOrgState(org);
      localStorage.setItem('active_org_id', org.id);
    }
  };

  const logout = () => {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('active_org_id');
    localStorage.removeItem('active_project_id');
    setToken(null);
    setUser(null);
    setOrgs([]);
    setActiveOrgState(null);
    setProjects([]);
    setActiveProjectState(null);
  };

  const setActiveOrg = (org: Organization) => {
    setActiveOrgState(org);
    localStorage.setItem('active_org_id', org.id);
    // Clear active project when org changes
    setActiveProjectState(null);
    localStorage.removeItem('active_project_id');
  };

  const setActiveProject = (project: Project | null) => {
    setActiveProjectState(project);
    if (project) {
      localStorage.setItem('active_project_id', project.id);
    } else {
      localStorage.removeItem('active_project_id');
    }
  };

  return (
    <AuthContext.Provider value={{
      user, token, orgs, activeOrg, projects, activeProject, loading,
      login, register, logout, setActiveOrg, setActiveProject,
      refreshOrgs, refreshProjects,
    }}>
      {children}
    </AuthContext.Provider>
  );
}
