import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getMe } from '../services/authApi';
import { sliceGetMe } from '../services/sliceAuthApi';
import { setAuthToken } from '../services/httpClient';
import { setSliceAuthToken } from '../services/sliceHttpClient';

export default function AppSelectorPage() {
  const navigate = useNavigate();
  const [status, setStatus] = useState<'checking' | 'choose' | 'alrrx-only' | 'slice-only' | 'none'>('checking');

  useEffect(() => {
    const check = async () => {
      const alrrxToken = localStorage.getItem('alrrx_token');
      const sliceToken = localStorage.getItem('slice_token');

      if (!alrrxToken && !sliceToken) {
        setStatus('none');
        return;
      }

      let alrrxOk = false;
      let sliceOk = false;

      if (alrrxToken) {
        setAuthToken(alrrxToken);
        try {
          await getMe();
          alrrxOk = true;
        } catch {
          localStorage.removeItem('alrrx_token');
        }
      }
      if (sliceToken) {
        setSliceAuthToken(sliceToken);
        try {
          await sliceGetMe();
          sliceOk = true;
        } catch {
          localStorage.removeItem('slice_token');
        }
      }

      if (alrrxOk && sliceOk) setStatus('choose');
      else if (alrrxOk) setStatus('alrrx-only');
      else if (sliceOk) setStatus('slice-only');
      else setStatus('none');
    };
    check();
  }, []);

  useEffect(() => {
    if (status === 'alrrx-only') navigate('/', { replace: true });
    if (status === 'slice-only') navigate('/slice', { replace: true });
    if (status === 'none') navigate('/login', { replace: true });
  }, [status, navigate]);

  if (status !== 'choose') {
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
      <div className="w-full max-w-3xl">
        <div className="text-center mb-10">
          <h1 className="font-display-hero text-4xl font-bold tracking-tight text-primary mb-2">RevolutionMedia</h1>
          <p className="text-steel-secondary">Choose a platform to continue</p>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <button
            onClick={() => navigate('/altrx')}
            className="bg-surface border border-whisper-border rounded-xl p-8 text-left hover:border-electric-blue hover:shadow-md transition-all group"
          >
            <div className="flex items-center gap-3 mb-3">
              <span className="material-symbols-outlined text-3xl text-electric-blue" style={{ fontVariationSettings: "'FILL' 1" }}>
                local_fire_department
              </span>
              <h2 className="text-2xl font-bold text-primary">ALRrx</h2>
            </div>
            <p className="text-sm text-steel-secondary">
              Call center operational dashboard, analytics, real-time monitoring and Vicidial form.
            </p>
            <div className="mt-4 text-xs text-electric-blue font-semibold flex items-center gap-1 group-hover:gap-2 transition-all">
              Open ALRrx <span className="material-symbols-outlined text-base">arrow_forward</span>
            </div>
          </button>
          <button
            onClick={() => navigate('/slice')}
            className="bg-surface border border-whisper-border rounded-xl p-8 text-left hover:border-electric-blue hover:shadow-md transition-all group"
          >
            <div className="flex items-center gap-3 mb-3">
              <span className="material-symbols-outlined text-3xl text-electric-blue" style={{ fontVariationSettings: "'FILL' 1" }}>
                content_cut
              </span>
              <h2 className="text-2xl font-bold text-primary">SLICE</h2>
            </div>
            <p className="text-sm text-steel-secondary">
              Operational intelligence: process Excel/ZIP reports, analyze shop & agent metrics.
            </p>
            <div className="mt-4 text-xs text-electric-blue font-semibold flex items-center gap-1 group-hover:gap-2 transition-all">
              Open SLICE <span className="material-symbols-outlined text-base">arrow_forward</span>
            </div>
          </button>
        </div>
      </div>
    </div>
  );
}
