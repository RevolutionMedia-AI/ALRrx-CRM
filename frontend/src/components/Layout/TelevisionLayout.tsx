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

const sliceNavItem = { label: 'Slice Platform', path: '/slice' };
const tvNavItem = { label: 'TV Leaderboard', path: '/tv' };

export default function TelevisionLayout({ children }: { children: ReactNode }) {
  const { user, logout, isAdmin, authUnavailable, has } = useAuth();
  const { isDark, toggle } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();
  const canSeeTv = has('tv.view');

  const hasDualAccess = !!user && getAccessGroup(user.platformAccess) === 'both';
  const handleSignOut = () => {
    if (hasDualAccess || isAdmin) {
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
      <nav className={`fixed ${authUnavailable ? 'top-9' : 'top-0'} left-0 right-0 z-50 flex justify-between items-center px-4 h-16 bg-pure-surface dark:bg-gray-900 border-b border-whisper-border dark:border-gray-800 transition-colors`}>
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
            {canSeeTv && (
              <button
                key={tvNavItem.path}
                onClick={() => navigate(tvNavItem.path)}
                className={
                  location.pathname.startsWith(tvNavItem.path)
                    ? 'text-primary dark:text-gray-100 border-b-2 border-primary dark:border-gray-100 pb-1 h-full flex items-center pt-1 text-sm font-semibold'
                    : 'text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors h-full flex items-center text-sm font-medium'
                }
                title="Live sales leaderboard for the office TV"
              >
                {tvNavItem.label}
              </button>
            )}
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
      <main className={`w-full px-2.5 pt-[calc(4rem+0.5rem)] pb-2 flex flex-col gap-3 min-h-[calc(100dvh-4rem)] ${authUnavailable ? 'pt-[calc(4rem+2.25rem)]' : ''}`}>
        {children}
      </main>
    </div>
  );
}