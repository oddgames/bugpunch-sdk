import { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../lib/auth';

export function UserMenu() {
  const { user, orgs, activeOrg, setActiveOrg, logout } = useAuth();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handle = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handle);
    return () => document.removeEventListener('mousedown', handle);
  }, []);

  if (!user) return null;

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(!open)}
        className="flex items-center gap-2 px-3 py-1.5 rounded text-sm border"
        style={{ borderColor: 'var(--border-color)', color: 'var(--text-primary)', background: 'var(--bg-tertiary)' }}
      >
        <span className="w-5 h-5 rounded-full flex items-center justify-center text-xs font-bold"
          style={{ background: 'var(--accent-blue)', color: '#1e1e1e' }}>
          {user.displayName[0].toUpperCase()}
        </span>
        <span>{user.displayName}</span>
        <svg width="10" height="6" viewBox="0 0 10 6" fill="currentColor"><path d="M0 0l5 6 5-6z"/></svg>
      </button>

      {open && (
        <div className="absolute right-0 top-full mt-1 w-64 rounded-lg border shadow-lg z-50"
          style={{ background: 'var(--bg-secondary)', borderColor: 'var(--border-color)' }}>

          {/* User info */}
          <div className="px-3 py-2 border-b" style={{ borderColor: 'var(--border-color)' }}>
            <div className="text-sm font-medium" style={{ color: 'var(--text-primary)' }}>{user.displayName}</div>
            <div className="text-xs" style={{ color: 'var(--text-muted)' }}>{user.email}</div>
          </div>

          {/* Organization selector */}
          {orgs.length > 0 && (
            <div className="px-3 py-2 border-b" style={{ borderColor: 'var(--border-color)' }}>
              <div className="text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Organization</div>
              {orgs.map(org => (
                <button
                  key={org.id}
                  onClick={() => { setActiveOrg(org); setOpen(false); }}
                  className="w-full text-left px-2 py-1 rounded text-sm flex items-center gap-2"
                  style={{
                    color: org.id === activeOrg?.id ? 'var(--accent-blue)' : 'var(--text-secondary)',
                    background: org.id === activeOrg?.id ? 'var(--bg-tertiary)' : 'transparent',
                  }}
                  onMouseEnter={e => e.currentTarget.style.background = 'var(--bg-hover)'}
                  onMouseLeave={e => e.currentTarget.style.background = org.id === activeOrg?.id ? 'var(--bg-tertiary)' : 'transparent'}
                >
                  {org.id === activeOrg?.id && <span>&#10003;</span>}
                  {org.name}
                </button>
              ))}
            </div>
          )}

          {/* Actions */}
          <div className="px-3 py-2">
            <button
              onClick={() => { navigate('/settings'); setOpen(false); }}
              className="w-full text-left px-2 py-1 rounded text-sm"
              style={{ color: 'var(--text-secondary)' }}
              onMouseEnter={e => e.currentTarget.style.background = 'var(--bg-hover)'}
              onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
            >
              Settings
            </button>
            <button
              onClick={() => { logout(); navigate('/login'); setOpen(false); }}
              className="w-full text-left px-2 py-1 rounded text-sm"
              style={{ color: 'var(--accent-red)' }}
              onMouseEnter={e => e.currentTarget.style.background = 'var(--bg-hover)'}
              onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
            >
              Logout
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
