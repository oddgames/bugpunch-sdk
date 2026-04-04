import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../lib/auth';

const BASE_URL = import.meta.env.VITE_API_URL || '';

export function LoginPage() {
  const { login, register, user } = useAuth();
  const navigate = useNavigate();
  const [needsSetup, setNeedsSetup] = useState<boolean | null>(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  // Redirect if already logged in
  useEffect(() => {
    if (user) navigate('/');
  }, [user, navigate]);

  // Check if first-time setup is needed
  useEffect(() => {
    (async () => {
      try {
        const res = await fetch(`${BASE_URL}/api/auth/needs-setup`);
        const data = await res.json();
        setNeedsSetup(data.needsSetup);
      } catch {
        setNeedsSetup(false);
      }
    })();
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSubmitting(true);
    try {
      if (needsSetup) {
        // Re-check right before submitting — someone else may have set up
        const check = await fetch(`${BASE_URL}/api/auth/needs-setup`);
        const data = await check.json();
        if (!data.needsSetup) {
          setNeedsSetup(false);
          setError('Admin account already exists. Please login.');
          setSubmitting(false);
          return;
        }
        await register(email, password, displayName || email.split('@')[0]);
      } else {
        await login(email, password);
      }
      navigate('/');
    } catch (err: any) {
      const msg = err.message || 'Authentication failed';
      if (needsSetup && msg.includes('Setup already completed')) {
        setNeedsSetup(false);
        setError('Admin account already exists. Please login.');
      } else {
        setError(msg);
      }
    }
    setSubmitting(false);
  };

  if (needsSetup === null) {
    return (
      <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--bg-primary)' }}>
        <span style={{ color: 'var(--text-muted)' }}>Loading...</span>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center" style={{ background: 'var(--bg-primary)' }}>
      <div className="w-full max-w-sm rounded-lg border p-6" style={{ background: 'var(--bg-secondary)', borderColor: 'var(--border-color)' }}>
        <h1 className="text-lg font-semibold mb-1 text-center" style={{ color: 'var(--text-primary)' }}>
          UIAutomation
        </h1>
        <p className="text-xs text-center mb-4" style={{ color: 'var(--text-muted)' }}>
          {needsSetup ? 'Create your admin account to get started' : 'Sign in to continue'}
        </p>

        <form onSubmit={handleSubmit} className="flex flex-col gap-3">
          <div>
            <label className="block text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Email</label>
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              required
              className="w-full px-3 py-2 rounded text-sm border outline-none"
              style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
              autoFocus
            />
          </div>

          {needsSetup && (
            <div>
              <label className="block text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Display Name</label>
              <input
                type="text"
                value={displayName}
                onChange={e => setDisplayName(e.target.value)}
                placeholder="Optional"
                className="w-full px-3 py-2 rounded text-sm border outline-none"
                style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
              />
            </div>
          )}

          <div>
            <label className="block text-xs mb-1" style={{ color: 'var(--text-muted)' }}>Password</label>
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
              minLength={6}
              className="w-full px-3 py-2 rounded text-sm border outline-none"
              style={{ background: 'var(--bg-tertiary)', borderColor: 'var(--border-color)', color: 'var(--text-primary)' }}
            />
          </div>

          {error && (
            <div className="text-xs px-3 py-2 rounded" style={{ background: 'rgba(255, 77, 77, 0.15)', color: 'var(--accent-red)' }}>
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="w-full py-2 rounded text-sm font-medium disabled:opacity-50"
            style={{ background: 'var(--accent-blue)', color: '#1e1e1e' }}
          >
            {submitting ? 'Please wait...' : needsSetup ? 'Create Admin Account' : 'Login'}
          </button>
        </form>

        {!needsSetup && (
          <p className="text-xs text-center mt-3" style={{ color: 'var(--text-muted)' }}>
            Contact your admin to get an account
          </p>
        )}
      </div>
    </div>
  );
}
