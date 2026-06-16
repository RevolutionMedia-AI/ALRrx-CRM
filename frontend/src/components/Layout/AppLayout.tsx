import { useAuth } from '../../context/AuthContext';
import { useTheme } from '../../context/ThemeContext';
import { useNavigate, useLocation } from 'react-router-dom';
import { getAccessGroup } from '../../utils/accessControl';
import type { ReactNode } from 'react';

const navItems = [
  { label: 'Dashboard ALTRX', path: '/' },
  { label: 'Analytics ALTRX', path: '/analytics' },
  { label: 'Real-Time ALTRX', path: '/real-time' },
];

// Cross-platform shortcut visible to users with Slice access (admins and
// 'Both' users). Lets them jump to SLICE without going through /select-platform.
const sliceNavItem = { label: 'Slice Platform', path: '/slice' };

// Twilio Costs is part of the Admin Panel. It is intentionally NOT linked
// from the ALTRX navbar — admins must go through the platform picker to
// reach the Admin Panel, keeping it semantically separate from ALTRX/Slice.

export default function AppLayout({ children }: { children: ReactNode }) {
  const { user, logout, isAdmin, authUnavailable } = useAuth();
  const { isDark, toggle } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();

  // Admins and dual-platform users route to the platform picker instead of a
  // full sign-out. The picker is the only entry point to the Admin Panel.
  const hasDualAccess = !!user && getAccessGroup(user.platformAccess) === 'both';
  const handleSignOut = () => {
    if (hasDualAccess || isAdmin) {
      // BUG-15 fix: no-op when we're already on the picker, otherwise
      // repeated clicks would just spam the navigate call.
      if (location.pathname === '/select-platform') return;
      navigate('/select-platform');
    } else {
      logout();
    }
  };

  return (
    <div className="bg-canvas-white dark:bg-gray-950 text-on-surface dark:text-gray-100 font-body-md antialiased min-h-screen transition-colors">
      {authUnavailable && (
        <div className="fixed top-0 left-0 right-0 z-[60] bg-amber-warmth/95 text-white text-center text-sm py-2 px-4 shadow-md">
          <span className="material-symbols-outlined text-base align-middle mr-1">cloud_off</span>
          User service temporarily unavailable. Showing cached data. Retrying…
        </div>
      )}
      <nav className={`fixed ${authUnavailable ? 'top-9' : 'top-0'} left-0 right-0 z-50 flex justify-between items-center px-gutter-desktop h-16 bg-pure-surface dark:bg-gray-900 border-b border-whisper-border dark:border-gray-800 transition-colors`}>
        <div className="flex items-center gap-8">
          <div className="font-display-hero text-lg font-bold text-primary dark:text-gray-100 flex items-center gap-2">
            <span className="material-symbols-outlined text-electric-blue" style={{ fontVariationSettings: "'FILL' 1" }}>
              local_fire_department
            </span>
            RevolutionMedia Reports
          </div>
          <button
            onClick={toggle}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-full border border-whisper-border dark:border-gray-700 bg-surface-container-low dark:bg-gray-800 text-secondary dark:text-gray-300 hover:text-primary dark:hover:text-gray-100 hover:border-electric-blue transition-all text-sm font-medium"
            title={isDark ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro'}
          >
            <span className="material-symbols-outlined text-[18px]">
              {isDark ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
          <div className="hidden md:flex gap-6 items-center h-full">
            {navItems.map((item) => (
              <button
                key={item.path}
                onClick={() => navigate(item.path)}
                className={
                  location.pathname === item.path
                    ? 'text-primary dark:text-gray-100 border-b-2 border-primary dark:border-gray-100 pb-1 h-full flex items-center pt-1 text-sm font-semibold'
                    : 'text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors h-full flex items-center text-sm font-medium'
                }
              >
                {item.label}
              </button>
            ))}
            {user?.platformAccess === 'Slice' || user?.platformAccess === 'Both' ? (
              // Admins already have a "Switch" button that goes to the platform
              // picker, so the Slice quick-jump would be redundant for them.
              // Hide it here — they get Slice access via the picker instead.
              !isAdmin && (
                <button
                  key={sliceNavItem.path}
                  onClick={() => navigate(sliceNavItem.path)}
                  className={
                    location.pathname.startsWith('/slice')
                      ? 'text-primary dark:text-gray-100 border-b-2 border-primary dark:border-gray-100 pb-1 h-full flex items-center pt-1 text-sm font-semibold'
                      : 'text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors h-full flex items-center text-sm font-medium'
                  }
                  title="Go to Slice platform"
                >
                  {sliceNavItem.label}
                </button>
              )
            ) : null}
          </div>
        </div>
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 text-sm">
            <span className="text-secondary dark:text-gray-400 hidden sm:inline">{user?.fullName}</span>
            <span className="material-symbols-outlined text-secondary dark:text-gray-400">account_circle</span>
          </div>
          <button
            onClick={handleSignOut}
            title={hasDualAccess || isAdmin ? 'Switch platform or sign out' : 'Sign out'}
            className="bg-primary dark:bg-gray-700 text-on-primary dark:text-gray-100 px-4 py-1.5 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm"
          >
            {hasDualAccess || isAdmin ? 'Switch' : 'Sign Out'}
          </button>
        </div>
      </nav>
      <main className={`max-w-[1400px] mx-auto px-gutter-mobile md:px-gutter-tablet lg:px-gutter-desktop py-8 flex flex-col gap-8 min-h-[calc(100dvh-4rem)] ${authUnavailable ? 'pt-32' : 'pt-24'}`}>
        {children}
      </main>
    </div>
  );
}
