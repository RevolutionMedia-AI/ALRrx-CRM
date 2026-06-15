import { useAuth } from '../context/AuthContext';
import { useNavigate } from 'react-router-dom';

export default function NoAccessPage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const handleSignOut = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-canvas-white dark:bg-gray-950 px-4">
      <div className="w-full max-w-[560px] bg-pure-surface dark:bg-gray-900 diffused-shadow-lg border border-whisper-border dark:border-gray-700 p-10 md:p-12 rounded-xl relative overflow-hidden">
        <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-amber-warmth via-deep-rose to-amber-warmth" />

        <div className="flex flex-col items-center text-center">
          <div className="inline-flex items-center justify-center w-16 h-16 bg-amber-warmth/10 dark:bg-amber-warmth/20 text-amber-warmth rounded-full mb-6">
            <span className="material-symbols-outlined" style={{ fontSize: '32px', fontVariationSettings: "'FILL' 1" }}>
              lock
            </span>
          </div>

          <h1 className="font-display-hero text-headline-md text-primary dark:text-white mb-3">
            No access yet
          </h1>

          <p className="text-secondary dark:text-gray-300 text-base mb-2">
            Hi <span className="font-medium text-primary dark:text-white">{user?.fullName ?? user?.email}</span>, your account was created successfully.
          </p>

          <p className="text-secondary dark:text-gray-300 text-base mb-6">
            Please request access to the relevant portal by emailing{' '}
            <a
              href="mailto:kevin.escalante@revolutionmedia.ai?subject=Access%20request%20%E2%80%94%20"
              className="text-electric-blue font-medium hover:underline"
            >
              kevin.escalante@revolutionmedia.ai
            </a>
            .
          </p>

          <p className="text-xs text-muted-slate dark:text-gray-500 mb-8 max-w-md">
            An admin will assign your role and platform access. Once that is done, sign in again
            and you will land directly on the right dashboard.
          </p>

          <button
            onClick={handleSignOut}
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-medium bg-primary dark:bg-gray-700 text-on-primary dark:text-gray-100 hover:scale-[0.98] transition-transform shadow-sm"
          >
            <span className="material-symbols-outlined text-[18px]">logout</span>
            Sign out
          </button>
        </div>
      </div>
    </div>
  );
}
