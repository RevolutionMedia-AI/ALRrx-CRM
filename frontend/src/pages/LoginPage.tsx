import { GoogleOAuthProvider, useGoogleLogin } from '@react-oauth/google';
import { useAuth } from '../context/AuthContext';
import { useNavigate } from 'react-router-dom';
import { useState, useEffect } from 'react';
import logoSrc from '../images/RevolutionLogo.png';

function GoogleIcon() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
      <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 01-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z" />
      <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
      <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" />
      <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" />
    </svg>
  );
}

function GoogleButton() {
  const { loginWithGoogle } = useAuth();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const handleGoogle = useGoogleLogin({
    flow: 'implicit',
    onSuccess: async (res: Record<string, unknown>) => {
      setBusy(true);
      setError('');
      try {
        const accessToken = String(res.access_token ?? '');
        if (!accessToken) {
          setError(`No access_token. Keys: ${Object.keys(res).join(', ')}`);
          return;
        }
        await loginWithGoogle(accessToken);
      } catch (e) {
        setError(`Google sign-in failed: ${e instanceof Error ? e.message : String(e)}`);
      } finally {
        setBusy(false);
      }
    },
    onError: () => setError('Google sign-in was cancelled or failed'),
  });

  return (
    <>
      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-lg px-4 py-3 text-deep-rose text-sm mb-6">
          {error}
        </div>
      )}
      <button
        onClick={() => handleGoogle()}
        disabled={busy}
        className="w-full flex items-center justify-center gap-3 bg-pure-surface dark:bg-gray-800 border border-whisper-border dark:border-gray-700 rounded-lg px-6 py-3 text-primary dark:text-gray-200 font-medium text-sm hover:bg-surface-container-low dark:hover:bg-gray-700 hover:border-electric-blue transition-all shadow-sm disabled:opacity-50"
      >
        <GoogleIcon />
        {busy ? 'Signing in...' : 'Continue with Google'}
      </button>
    </>
  );
}

export default function LoginPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [clientId, setClientId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (user) { navigate('/', { replace: true }); return; }
    fetch('/api/config/google-client-id')
      .then((r) => r.json())
      .then((d) => setClientId(d.clientId || null))
      .catch(() => setClientId(null))
      .finally(() => setLoading(false));
  }, [user, navigate]);

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 px-4">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-2 border-electric-blue border-t-transparent rounded-full animate-spin" />
          <p className="text-secondary dark:text-gray-400 text-sm">Loading...</p>
        </div>
      </div>
    );
  }

  if (!clientId) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 px-4">
        <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-2xl shadow-diffused w-full max-w-md p-10 text-center">
          <img src={logoSrc} alt="Revolution Logo" className="h-12 mb-6 mx-auto" />
          <h1 className="text-xl font-bold text-primary dark:text-gray-100 mb-3">RevolutionMedia Reports</h1>
          <p className="text-secondary dark:text-gray-400 text-sm">
            Google Sign-In is not configured. Set <code className="text-electric-blue bg-electric-blue/10 px-1 rounded text-xs">Google__ClientId</code> in Northflank backend environment.
          </p>
        </div>
      </div>
    );
  }

  return (
    <GoogleOAuthProvider clientId={clientId}>
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 px-4">
        <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-800 rounded-2xl shadow-diffused w-full max-w-md p-10">
          <div className="flex flex-col items-center mb-8">
            <img src={logoSrc} alt="Revolution Logo" className="h-12 mb-6" />
            <h1 className="text-xl font-bold text-primary dark:text-gray-100">RevolutionMedia Reports</h1>
            <p className="text-secondary dark:text-gray-400 text-sm mt-1">Sign in to your account</p>
          </div>
          <GoogleButton />
          <p className="text-center text-muted-slate text-xs mt-6">
            Only <span className="font-medium text-secondary dark:text-gray-400">@revolutionmedia.ai</span> accounts are allowed
          </p>
        </div>
      </div>
    </GoogleOAuthProvider>
  );
}
