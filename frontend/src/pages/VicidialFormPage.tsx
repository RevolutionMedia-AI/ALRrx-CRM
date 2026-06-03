import { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import VFormHeader from '../components/vicidial-form/VFormHeader';
import VSaleForm from '../components/vicidial-form/VSaleForm';
import VSalesList from '../components/vicidial-form/VSalesList';
import { useAuth } from '../context/AuthContext';
import { authenticateVicidialFormToken } from '../services/vicidialFormApi';
import { saveFormToken, clearFormToken, getFormToken, getFormIdentity } from '../utils/vicidialFormAuth';
import type { VicidialFormIdentity } from '../types';

export default function VicidialFormPage() {
  const { token: dashboardToken, user, loading: authLoading } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [refreshKey, setRefreshKey] = useState(0);
  const [identity, setIdentity] = useState<VicidialFormIdentity | null>(() => getFormIdentity());
  const [validating, setValidating] = useState(() => !!getFormToken());
  const [validationError, setValidationError] = useState<string | null>(null);

  const isDashboardAuthenticated = !authLoading && !!dashboardToken && !!user;
  const showHomeButton = isDashboardAuthenticated;

  useEffect(() => {
    document.title = 'ALTRX Sales Form';
  }, []);

  useEffect(() => {
    const urlToken = searchParams.get('token');
    if (urlToken) {
      setValidating(true);
      setValidationError(null);
      authenticateVicidialFormToken(urlToken)
        .then((id) => {
          saveFormToken(urlToken, id);
          setIdentity(id);
          setValidating(false);
          const next = new URLSearchParams(searchParams);
          next.delete('token');
          setSearchParams(next, { replace: true });
        })
        .catch((err: unknown) => {
          clearFormToken();
          setIdentity(null);
          setValidating(false);
          setValidationError(err && typeof err === 'object' && 'response' in err
            ? 'Invalid or expired link'
            : 'Could not validate the link');
        });
    } else if (getFormToken() && !identity) {
      setValidating(false);
    } else {
      setValidating(false);
    }
  }, []);

  const handleRefresh = () => {
    setRefreshKey((k) => k + 1);
  };

  if (validating) {
    return (
      <div className="bg-canvas-white dark:bg-gray-950 min-h-screen text-on-surface dark:text-gray-100 transition-colors">
        <VFormHeader showHomeButton={showHomeButton} />
        <main className="max-w-3xl mx-auto px-4 sm:px-6 py-12">
          <div className="flex flex-col items-center justify-center text-center">
            <div className="w-10 h-10 border-2 border-electric-blue border-t-transparent rounded-full animate-spin mb-4" />
            <p className="text-sm text-secondary">Validating your link…</p>
          </div>
        </main>
      </div>
    );
  }

  if (!identity) {
    return (
      <div className="bg-canvas-white dark:bg-gray-950 min-h-screen text-on-surface dark:text-gray-100 transition-colors">
        <VFormHeader showHomeButton={showHomeButton} />
        <main className="max-w-3xl mx-auto px-4 sm:px-6 py-12">
          <div className="bg-pure-surface dark:bg-gray-900 border border-deep-rose/30 dark:border-gray-700 rounded-2xl shadow-card p-10 flex flex-col items-center text-center">
            <div className="w-14 h-14 rounded-full bg-deep-rose/10 flex items-center justify-center mb-4">
              <span className="material-symbols-outlined text-3xl text-deep-rose">link_off</span>
            </div>
            <h2 className="text-lg font-bold text-primary dark:text-gray-100 mb-2">
              Select your user
            </h2>
            <p className="text-sm text-secondary dark:text-gray-400 max-w-md">
              {validationError ?? 'This form requires a valid link.'}
              {' '}Please request a new invitation from your supervisor.
            </p>
            {showHomeButton && (
              <button
                onClick={() => navigate('/')}
                className="mt-6 inline-flex items-center gap-2 px-4 py-2 bg-electric-blue text-white rounded-lg text-sm font-medium hover:scale-[0.98] transition-transform shadow-sm"
              >
                <span className="material-symbols-outlined text-[18px]">home</span>
                Go to dashboard
              </button>
            )}
          </div>
        </main>
      </div>
    );
  }

  return (
    <div className="bg-canvas-white dark:bg-gray-950 min-h-screen text-on-surface dark:text-gray-100 transition-colors">
      <VFormHeader showHomeButton={showHomeButton} />
      <main className="max-w-3xl mx-auto px-4 sm:px-6 py-8">
        <div className="space-y-6">
          <VSaleForm
            identity={identity}
            onSubmitted={handleRefresh}
          />
          <VSalesList refreshKey={refreshKey} />
        </div>
      </main>
    </div>
  );
}
