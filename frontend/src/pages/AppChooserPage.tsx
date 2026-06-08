import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useSliceAuth } from '../slice/context/SliceAuthContext';

function GoogleIcon() {
  return (
    <svg height="20" viewBox="0 0 48 48" width="20" xmlns="http://www.w3.org/2000/svg">
      <path d="M43.611,20.083H42V20H24v8h11.303c-1.649,4.657-6.08,8-11.303,8c-6.627,0-12-5.373-12-12c0-6.627,5.373-12,12-12c3.059,0,5.842,1.154,7.961,3.039l5.657-5.657C34.046,6.053,29.268,4,24,4C12.955,4,4,12.955,4,24c0,11.045,8.955,20,20,20c11.045,0,20-8.955,20-20C44,22.659,43.862,21.35,43.611,20.083z" fill="#FFC107" />
      <path d="M6.306,14.691l6.571,4.819C14.655,15.108,18.961,12,24,12c3.059,0,5.842,1.154,7.961,3.039l5.657-5.657C34.046,6.053,29.268,4,24,4C16.318,4,9.656,8.337,6.306,14.691z" fill="#FF3D00" />
      <path d="M24,44c5.166,0,9.86-1.977,13.409-5.192l-6.19-5.238C29.211,35.091,26.715,36,24,36c-5.202,0-9.619-3.317-11.283-7.946l-6.522,5.025C9.505,39.556,16.227,44,24,44z" fill="#4CAF50" />
      <path d="M43.611,20.083H42V20H24v8h11.303c-0.792,2.237-2.231,4.166-4.087,5.571l6.19,5.238C36.971,39.205,44,34,44,24C44,22.659,43.862,21.35,43.611,20.083z" fill="#1976D2" />
    </svg>
  );
}

export default function AppChooserPage() {
  const navigate = useNavigate();
  const { user: alrrxUser } = useAuth();
  const { user: sliceUser } = useSliceAuth();

  const alrrxLoggedIn = !!alrrxUser;
  const sliceLoggedIn = !!sliceUser;

  const goAlrrx = () => {
    if (alrrxLoggedIn) navigate('/');
    else navigate('/login?next=/');
  };

  const goSlice = () => {
    if (sliceLoggedIn) navigate('/slice');
    else navigate('/slice/login?next=/slice');
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-canvas-white px-4 py-10">
      <div className="w-full max-w-3xl">
        <div className="text-center mb-10">
          <div className="inline-flex items-center justify-center w-14 h-14 bg-primary text-on-primary rounded-lg mb-4">
            <GoogleIcon />
          </div>
          <h1 className="font-display-hero text-3xl md:text-4xl font-bold tracking-tight text-primary mb-2">
            Which page would you like to visit?
          </h1>
          <p className="text-steel-secondary text-sm">
            You're signed in. Pick a platform to continue.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
          <button
            onClick={goAlrrx}
            className="group bg-surface border border-whisper-border rounded-2xl p-8 text-left hover:border-electric-blue hover:shadow-lg transition-all relative overflow-hidden"
          >
            <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-electric-blue via-emerald-signal to-electric-blue opacity-0 group-hover:opacity-100 transition-opacity" />
            <div className="flex items-center gap-3 mb-4">
              <div className="w-12 h-12 rounded-xl bg-electric-blue/10 text-electric-blue flex items-center justify-center group-hover:scale-110 transition-transform">
                <span className="material-symbols-outlined text-3xl" style={{ fontVariationSettings: "'FILL' 1" }}>
                  medical_services
                </span>
              </div>
              <div>
                <h2 className="text-2xl font-bold text-primary">ALRrx</h2>
                <p className="text-[11px] text-muted-slate font-metadata-mono uppercase tracking-wider">
                  Call Center
                </p>
              </div>
            </div>
            <p className="text-sm text-steel-secondary mb-5">
              Real-time call center operations, agent performance, dispositions, analytics.
            </p>
            <div className="flex items-center justify-between">
              <span
                className={`text-[11px] font-metadata-mono uppercase tracking-wider px-2 py-1 rounded ${
                  alrrxLoggedIn
                    ? 'bg-emerald-signal/10 text-emerald-signal'
                    : 'bg-muted-slate/10 text-muted-slate'
                }`}
              >
                {alrrxLoggedIn ? '● Signed in' : '○ Not signed in'}
              </span>
              <span className="text-sm font-semibold text-electric-blue flex items-center gap-1 group-hover:gap-2 transition-all">
                Open
                <span className="material-symbols-outlined text-base">arrow_forward</span>
              </span>
            </div>
          </button>

          <button
            onClick={goSlice}
            className="group bg-surface border border-whisper-border rounded-2xl p-8 text-left hover:border-electric-blue hover:shadow-lg transition-all relative overflow-hidden"
          >
            <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-electric-blue via-emerald-signal to-electric-blue opacity-0 group-hover:opacity-100 transition-opacity" />
            <div className="flex items-center gap-3 mb-4">
              <div className="w-12 h-12 rounded-xl bg-electric-blue/10 text-electric-blue flex items-center justify-center group-hover:scale-110 transition-transform">
                <span className="material-symbols-outlined text-3xl" style={{ fontVariationSettings: "'FILL' 1" }}>
                  local_pizza
                </span>
              </div>
              <div>
                <h2 className="text-2xl font-bold text-primary">SLICE</h2>
                <p className="text-[11px] text-muted-slate font-metadata-mono uppercase tracking-wider">
                  Operational Intelligence
                </p>
              </div>
            </div>
            <p className="text-sm text-steel-secondary mb-5">
              Process Excel/ZIP reports, analyze shop metrics, agent performance, audit ledger.
            </p>
            <div className="flex items-center justify-between">
              <span
                className={`text-[11px] font-metadata-mono uppercase tracking-wider px-2 py-1 rounded ${
                  sliceLoggedIn
                    ? 'bg-emerald-signal/10 text-emerald-signal'
                    : 'bg-muted-slate/10 text-muted-slate'
                }`}
              >
                {sliceLoggedIn ? '● Signed in' : '○ Not signed in'}
              </span>
              <span className="text-sm font-semibold text-electric-blue flex items-center gap-1 group-hover:gap-2 transition-all">
                Open
                <span className="material-symbols-outlined text-base">arrow_forward</span>
              </span>
            </div>
          </button>
        </div>

        <div className="mt-8 text-center">
          <p className="text-xs text-muted-slate">
            Your choice is not saved — pick freely each time you sign in.
          </p>
        </div>
      </div>
    </div>
  );
}
