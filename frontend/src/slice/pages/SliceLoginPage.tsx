import { useState, useEffect } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useSliceAuth } from '../context/SliceAuthContext';

declare global {
  interface Window {
    google?: {
      accounts: {
        oauth2: {
          initTokenClient: (config: {
            client_id: string;
            scope: string;
            callback: (response: { access_token?: string; error?: string }) => void;
          }) => { requestAccessToken: () => void };
        };
      };
    };
  }
}

function GoogleIcon() {
  return (
    <svg height="20" viewBox="0 0 48 48" width="20" xmlns="http://www.w3.org/2000/svg">
      <path d="M43.611,20.083H42V20H24v8h11.303c-1.649,4.657-6.08,8-11.303,8c-6.627,0-12-5.373-12-12c0-6.627,5.373-12,12-12c3.059,0,5.842,1.154,7.961,3.039l5.657-5.657C34.046,6.053,29.268,4,24,4C12.955,4,4,12.955,4,24c0,11.045,8.955,20,20,20c11.045,0,20-8.955,20-20C44,22.659,43.862,21.35,43.611,20.083z" fill="#FFC107" />
      <path d="M6.306,14.691l6.571,4.819C14.655,15.108,18.961,12,24,12c3.059,0,5.842,1.154,7.961,3.039l5.657-5.657C34.046,6.053,29.268,4,24,4C16.318,4,9.656,8.337,6.306,14.691z" fill="#FF3D00" />
      <path d="M24,44c5.166,0,9.86-1.977,13.409-5.192l-6.19-5.238C29.211,35.091,26.715,36,24,36c-5.202,0-9.619-3.317-11.283-7.946l-6.522,5.025C9.505,39.556,16.227,44,24,44z" fill="#4CAF50" />
      <path d="M43.611,20.083H42V20H24v8h11.303c-0.792,2.237-2.231,4.166-4.087,5.571c0.001-0.001,0.002-0.001,0.003-0.002l6.19,5.238C36.971,39.205,44,34,44,24C44,22.659,43.862,21.35,43.611,20.083z" fill="#1976D2" />
    </svg>
  );
}

export default function SliceLoginPage() {
  const { user, loginWithGoogle, login } = useSliceAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [gsiReady, setGsiReady] = useState(false);
  const [clientId, setClientId] = useState<string | null>(null);
  const [loadingClient, setLoadingClient] = useState(true);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [mode, setMode] = useState<'google' | 'password'>('google');

  useEffect(() => {
    if (user) {
      const paramRedirect = searchParams.get('redirect');
      navigate(paramRedirect ?? '/slice', { replace: true });
      return;
    }
    fetch('/api/config/google-client-id')
      .then((r) => r.json())
      .then((d) => setClientId(d.clientId || null))
      .catch(() => setClientId(null))
      .finally(() => setLoadingClient(false));
  }, [user, navigate, searchParams]);

  useEffect(() => {
    if (!clientId) {
      setError('Google Sign-In is not configured. Set Google__ClientId in the backend environment.');
      return;
    }
    setError(null);
    const id = 'gsi-client-slice';
    if (document.getElementById(id)) {
      setGsiReady(true);
      return;
    }
    const script = document.createElement('script');
    script.id = id;
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.defer = true;
    script.onload = () => setGsiReady(true);
    script.onerror = () => setError('Failed to load Google Sign-In.');
    document.body.appendChild(script);
  }, [clientId]);

  const handleGoogle = () => {
    if (!window.google?.accounts?.oauth2 || !clientId) return;
    setBusy(true);
    setError(null);
    const client = window.google.accounts.oauth2.initTokenClient({
      client_id: clientId,
      scope: 'openid email profile',
      callback: async (response) => {
        if (response.error || !response.access_token) {
          setError('Google sign-in failed.');
          setBusy(false);
          return;
        }
        try {
          await loginWithGoogle(response.access_token);
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Authentication failed.');
        } finally {
          setBusy(false);
        }
      },
    });
    client.requestAccessToken();
  };

  const handlePasswordLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(email, password);
    } catch (err) {
      const msg = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { error?: string } } }).response?.data?.error
        : undefined;
      setError(msg ?? 'Login failed.');
    } finally {
      setBusy(false);
    }
  };

  if (loadingClient) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-canvas-white">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-2 border-electric-blue border-t-transparent rounded-full animate-spin" />
          <p className="text-secondary text-sm">Loading...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-canvas-white px-4">
      <div className="w-full max-w-[420px] bg-surface border border-whisper-border rounded-xl shadow-md p-8 relative overflow-hidden">
        <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-electric-blue via-emerald-signal to-electric-blue" />
        <div className="text-center mb-8">
          <h1 className="font-display-hero text-3xl font-bold tracking-tight text-primary mb-1">SLICE</h1>
          <p className="text-sm text-steel-secondary">Operational Intelligence — Sign in</p>
        </div>

        {error && (
          <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-lg px-4 py-3 text-deep-rose text-sm mb-4">
            {error}
          </div>
        )}

        {mode === 'google' ? (
          <>
            <button
              onClick={handleGoogle}
              disabled={busy || !gsiReady || !clientId}
              className="w-full flex items-center justify-center gap-3 bg-surface border border-whisper-border text-primary font-semibold text-sm py-3 rounded-lg hover:bg-surface-container transition-all disabled:opacity-50 disabled:cursor-not-allowed shadow-sm"
            >
              {busy ? (
                <span className="material-symbols-outlined animate-spin">progress_activity</span>
              ) : (
                <GoogleIcon />
              )}
              {busy ? 'Validating...' : 'Continue with Google'}
            </button>
            <p className="text-center text-xs text-muted-slate mt-4">
              Only authorized <span className="font-medium text-secondary">@revolutionmedia.ai</span> accounts allowed.
            </p>
          </>
        ) : (
          <form onSubmit={handlePasswordLogin} className="space-y-4">
            <div>
              <label className="block text-xs uppercase tracking-wider text-secondary font-semibold mb-1.5">Email</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                className="w-full px-3 py-2 rounded-lg border border-whisper-border bg-canvas-white text-primary text-sm outline-none focus:border-electric-blue focus:ring-2 focus:ring-electric-blue/20 transition-colors"
              />
            </div>
            <div>
              <label className="block text-xs uppercase tracking-wider text-secondary font-semibold mb-1.5">Password</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                className="w-full px-3 py-2 rounded-lg border border-whisper-border bg-canvas-white text-primary text-sm outline-none focus:border-electric-blue focus:ring-2 focus:ring-electric-blue/20 transition-colors"
              />
            </div>
            <button
              type="submit"
              disabled={busy}
              className="w-full bg-primary text-on-primary font-semibold text-sm py-3 rounded-lg hover:bg-primary/90 transition-colors disabled:opacity-50"
            >
              {busy ? 'Signing in...' : 'Sign In'}
            </button>
          </form>
        )}

        <div className="mt-6 text-center">
          <button
            onClick={() => setMode(mode === 'google' ? 'password' : 'google')}
            className="text-xs text-secondary hover:text-primary transition-colors"
          >
            {mode === 'google' ? 'Use email + password' : 'Use Google Sign-In'}
          </button>
        </div>
        <div className="mt-4 text-center">
          <a href="/" className="text-xs text-muted-slate hover:text-primary transition-colors">
            ← Back to ALRrx platform
          </a>
        </div>
      </div>
    </div>
  );
}
