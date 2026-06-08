import { GoogleOAuthProvider, useGoogleLogin } from '@react-oauth/google';
import { useAuth } from '../context/AuthContext';
import { useNavigate } from 'react-router-dom';
import { useState, useEffect } from 'react';
import logoSrc from '../images/RevolutionLogo.png';

function GoogleIcon() {
  return (
    <svg height="24px" viewBox="0 0 48 48" width="24px" xmlns="http://www.w3.org/2000/svg">
      <path d="M43.611,20.083H42V20H24v8h11.303c-1.649,4.657-6.08,8-11.303,8c-6.627,0-12-5.373-12-12c0-6.627,5.373-12,12-12c3.059,0,5.842,1.154,7.961,3.039l5.657-5.657C34.046,6.053,29.268,4,24,4C12.955,4,4,12.955,4,24c0,11.045,8.955,20,20,20c11.045,0,20-8.955,20-20C44,22.659,43.862,21.35,43.611,20.083z" fill="#FFC107"/>
      <path d="M6.306,14.691l6.571,4.819C14.655,15.108,18.961,12,24,12c3.059,0,5.842,1.154,7.961,3.039l5.657-5.657C34.046,6.053,29.268,4,24,4C16.318,4,9.656,8.337,6.306,14.691z" fill="#FF3D00"/>
      <path d="M24,44c5.166,0,9.86-1.977,13.409-5.192l-6.19-5.238C29.211,35.091,26.715,36,24,36c-5.202,0-9.619-3.317-11.283-7.946l-6.522,5.025C9.505,39.556,16.227,44,24,44z" fill="#4CAF50"/>
      <path d="M43.611,20.083H42V20H24v8h11.303c-0.792,2.237-2.231,4.166-4.087,5.571c0.001-0.001,0.002-0.001,0.003-0.002l6.19,5.238C36.971,39.205,44,34,44,24C44,22.659,43.862,21.35,43.611,20.083z" fill="#1976D2"/>
    </svg>
  );
}

function GoogleButton() {
  const { loginWithGoogle } = useAuth();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [success, setSuccess] = useState(false);

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
        setSuccess(true);
      } catch (e) {
        setError(`Google sign-in failed: ${e instanceof Error ? e.message : String(e)}`);
      } finally {
        setBusy(false);
      }
    },
    onError: () => setError('Google sign-in was cancelled or failed'),
  });

  if (success) {
    return (
      <button
        disabled
        className="w-full bg-emerald-signal text-on-primary font-bold text-lg py-4 rounded-lg transition-all flex items-center justify-center gap-3"
      >
        <span className="material-symbols-outlined">check_circle</span>
        Access Granted
      </button>
    );
  }

  if (busy) {
    return (
      <button
        disabled
        className="w-full bg-emerald-signal text-on-primary font-bold text-lg py-4 rounded-lg transition-all flex items-center justify-center gap-3"
      >
        <span className="animate-spin material-symbols-outlined">progress_activity</span>
        Validating access...
      </button>
    );
  }

  return (
    <>
      {error && (
        <div className="bg-deep-rose/10 dark:bg-deep-rose/20 border border-deep-rose/20 dark:border-deep-rose/30 rounded-lg px-4 py-3 text-deep-rose text-sm mb-6">
          {error}
        </div>
      )}
      <button
        onClick={() => handleGoogle()}
        className="w-full bg-pure-surface dark:bg-gray-800 border border-whisper-border dark:border-gray-700 text-on-surface dark:text-gray-100 font-bold text-lg py-4 rounded-lg hover:bg-surface-container-low dark:hover:bg-gray-700 active:scale-[0.98] transition-all flex items-center justify-center gap-3 shadow-sm"
      >
        <GoogleIcon />
        Continue with Google
      </button>
    </>
  );
}

export default function LoginPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [clientId, setClientId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    if (user) {
      navigate('/', { replace: true });
      return;
    }
    fetch('/api/config/google-client-id')
      .then((r) => r.json())
      .then((d) => setClientId(d.clientId || null))
      .catch(() => setClientId(null))
      .finally(() => setLoading(false));
  }, [user, navigate]);

  useEffect(() => {
    setMounted(true);
  }, []);

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
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 overflow-hidden">

        <div className="fixed inset-0 z-0 data-grid pointer-events-none"></div>

        <div className="fixed inset-0 z-10 overflow-hidden pointer-events-none">
          <div className="absolute top-0 left-0 w-full h-14 border-b border-whisper-border dark:border-gray-800 bg-pure-surface/60 dark:bg-gray-900/60 backdrop-blur-md flex items-center px-6 gap-4 font-metadata-mono text-sm">
            <span className="text-electric-blue italic font-bold text-lg">fx</span>
            <div className="h-6 w-px bg-whisper-border dark:border-gray-800"></div>
            <span className="text-secondary dark:text-gray-400 opacity-60 flex-1 truncate">
              =QUERY(DataMatrix, "SELECT * WHERE status = 'active' AND user = 'current'", 1)
            </span>
          </div>

          <div className="absolute top-14 left-0 w-12 h-full border-r border-whisper-border dark:border-gray-800 bg-surface-container-lowest/40 dark:bg-gray-900/40 flex flex-col items-center pt-4 gap-[24px] text-outline text-xs font-metadata-mono">
            <span>1</span><span>2</span><span>3</span><span>4</span><span>5</span>
            <span>6</span><span>7</span><span>8</span><span>9</span><span>10</span>
            <span>11</span><span>12</span><span>13</span><span>14</span><span>15</span>
          </div>

          <div className="absolute top-14 left-12 w-full h-8 border-b border-whisper-border dark:border-gray-800 bg-surface-container-lowest/40 dark:bg-gray-900/40 flex items-center pl-8 gap-[32px] text-outline text-xs font-metadata-mono">
            <span>A</span><span>B</span><span>C</span><span>D</span><span>E</span>
            <span>F</span><span>G</span><span>H</span><span>I</span><span>J</span>
            <span>K</span><span>L</span><span>M</span><span>N</span>
          </div>

          <div className="absolute top-[25%] right-[20%] opacity-5">
            <span className="material-symbols-outlined text-primary dark:text-gray-100" style={{ fontSize: '180px' }}>view_column</span>
          </div>
          <div className="absolute bottom-[15%] left-[15%] opacity-5">
            <span className="material-symbols-outlined text-primary dark:text-gray-100" style={{ fontSize: '240px' }}>table_chart</span>
          </div>
        </div>

        <main className="relative z-20 min-h-screen w-full flex items-center justify-center p-gutter-mobile">
          <div className="w-full max-w-[480px] space-y-8">
            <div className="bg-pure-surface dark:bg-gray-900 diffused-shadow-lg border border-whisper-border dark:border-gray-700 p-10 md:p-12 rounded-xl relative overflow-hidden group">
              <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-electric-blue via-emerald-signal to-electric-blue"></div>

              <div className="mb-10 text-center">
                <div className="inline-flex items-center justify-center w-12 h-12 bg-primary dark:bg-gray-700 text-on-primary dark:text-gray-100 rounded-lg mb-6 group-hover:scale-105 transition-transform duration-500">
                  <span className="material-symbols-outlined" style={{ fontVariationSettings: "'FILL' 1" }}>
                    table_chart
                  </span>
                </div>

                <h1 className="font-display-hero text-headline-lg tracking-tighter uppercase mb-2 leading-[0.95]">
                  <span className="block text-primary dark:text-white dark:font-extrabold">RevolutionMedia</span>
                  <span className="block text-primary dark:text-white dark:font-extrabold pl-19 mt-1">Report</span>
                  <span className="block text-primary dark:text-white dark:font-extrabold pl-5 mt-1">Platform</span>
                </h1>
                <p className="text-secondary dark:text-gray-400">
                  Sign in to your account
                </p>
              </div>

              <div className="bg-deep-rose/10 border border-deep-rose/20 dark:bg-deep-rose/20 dark:border-deep-rose/30 rounded-lg px-4 py-3 text-deep-rose text-sm mb-6">
                Google Sign-In is not configured. Set <code className="text-electric-blue bg-electric-blue/10 px-1 rounded text-xs">Google__ClientId</code> in Northflank backend environment.
              </div>
            </div>
          </div>
        </main>
      </div>
    );
  }

  return (
    <GoogleOAuthProvider clientId={clientId}>
      <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 overflow-hidden">

        <div className="fixed inset-0 z-0 data-grid pointer-events-none"></div>

        <div className="fixed inset-0 z-10 overflow-hidden pointer-events-none">
          <div className="absolute top-0 left-0 w-full h-14 border-b border-whisper-border dark:border-gray-800 bg-pure-surface/60 dark:bg-gray-900/60 backdrop-blur-md flex items-center px-6 gap-4 font-metadata-mono text-sm">
            <span className="text-electric-blue italic font-bold text-lg">fx</span>
            <div className="h-6 w-px bg-whisper-border dark:border-gray-800"></div>
            <span className="text-secondary dark:text-gray-400 opacity-60 flex-1 truncate">
              =QUERY(Users, "SELECT * WHERE domain = '@revolutionmedia.ai'", 1)
            </span>
          </div>

          <div className="absolute top-14 left-0 w-12 h-full border-r border-whisper-border dark:border-gray-800 bg-surface-container-lowest/40 dark:bg-gray-900/40 flex flex-col items-center pt-4 gap-[24px] text-outline text-xs font-metadata-mono">
            <span>1</span><span>2</span><span>3</span><span>4</span><span>5</span>
            <span>6</span><span>7</span><span>8</span><span>9</span><span>10</span>
            <span>11</span><span>12</span><span>13</span><span>14</span><span>15</span>
          </div>

          <div className="absolute top-14 left-12 w-full h-8 border-b border-whisper-border dark:border-gray-800 bg-surface-container-lowest/40 dark:bg-gray-900/40 flex items-center pl-8 gap-[32px] text-outline text-xs font-metadata-mono">
            <span>A</span><span>B</span><span>C</span><span>D</span><span>E</span>
            <span>F</span><span>G</span><span>H</span><span>I</span><span>J</span>
            <span>K</span><span>L</span><span>M</span><span>N</span>
          </div>

          <div className="absolute top-[25%] right-[20%] opacity-5">
            <span className="material-symbols-outlined text-primary dark:text-gray-100" style={{ fontSize: '180px' }}>view_column</span>
          </div>
          <div className="absolute bottom-[15%] left-[15%] opacity-5">
            <span className="material-symbols-outlined text-primary dark:text-gray-100" style={{ fontSize: '240px' }}>table_chart</span>
          </div>
        </div>

        <main className="relative z-20 min-h-screen w-full flex items-center justify-center p-gutter-mobile">
          <div className={`w-full max-w-[480px] space-y-8 transition-all duration-1000 cubic-bezier(0.16, 1, 0.3, 1) ${mounted ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-4'}`}>

            <div className="bg-pure-surface dark:bg-gray-900 diffused-shadow-lg border border-whisper-border dark:border-gray-700 p-10 md:p-12 rounded-xl relative overflow-hidden group">

              <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-electric-blue via-emerald-signal to-electric-blue"></div>

              <div className="mb-10 text-center">
                <img src={logoSrc} alt="Revolution Logo" className="h-12 mb-6 mx-auto" />

                <h1 className="font-display-hero text-headline-lg tracking-tighter uppercase mb-2 leading-[0.95]">
                  <span className="block text-primary dark:text-white dark:font-extrabold">RevolutionMedia</span>
                  <span className="block text-primary dark:text-white dark:font-extrabold pl-19 mt-1">Report</span>
                  <span className="block text-primary dark:text-white dark:font-extrabold pl-5 mt-1">Platform</span>
                </h1>
                <p className="text-secondary dark:text-gray-400">
                  Sign in to your account
                </p>
              </div>

              <div className="space-y-6">
                <div className="pt-2">
                  <GoogleButton />
                </div>
              </div>

              <p className="text-center text-muted-slate dark:text-gray-500 text-xs mt-6">
                Only <span className="font-medium text-secondary dark:text-gray-400">@revolutionmedia.ai</span> accounts are allowed
              </p>

            </div>

            <footer className="flex items-center justify-center px-4">
              <p className="text-metadata-mono text-outline dark:text-gray-600">
                © 2024 RevolutionMedia
              </p>
            </footer>

          </div>
        </main>
      </div>
    </GoogleOAuthProvider>
  );
}
