import { useAuth } from '../../context/AuthContext';
import { useTheme } from '../../context/ThemeContext';
import { useNavigate, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';

const platformItems = [
  { label: 'ALTRX', path: '/dashboard' },
  { label: 'Slice Platform', path: '/slice' },
];

// Admin-only sections reachable from the Admin Panel nav. These live under
// /admin/* so they are exclusive to the Admin Panel and cannot be reached
// from ALTRX or Slice.
const adminSectionItems = [
  { label: 'Twilio Costs', path: '/admin/twilio' },
];

export default function AdminLayout({ children }: { children: ReactNode }) {
  const { user, logout, isAdmin } = useAuth();
  const { isDark, toggle } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();

  // Admins always have access to the picker. "Switch" sends them back to it
  // so they can pick a different surface. Non-admins would never reach
  // this layout (the AdminRoute guard requires role === 'Admin'), so the
  // 'Switch' branch is the only one that can fire here.
  const handleSignOut = () => {
    if (isAdmin) {
      if (location.pathname === '/select-platform') return;
      navigate('/select-platform');
    } else {
      logout();
    }
  };

  return (
    <div className="bg-canvas-white dark:bg-gray-950 text-on-surface dark:text-gray-100 font-body-md antialiased min-h-screen transition-colors">
      <nav className="fixed top-0 left-0 right-0 z-50 flex justify-between items-center px-gutter-desktop h-16 bg-pure-surface dark:bg-gray-900 border-b border-whisper-border dark:border-gray-800 transition-colors">
        <div className="flex items-center gap-8">
          <div className="font-display-hero text-lg font-bold text-primary dark:text-gray-100 flex items-center gap-2">
            <span className="material-symbols-outlined text-electric-blue" style={{ fontVariationSettings: "'FILL' 1" }}>
              local_fire_department
            </span>
            RevolutionMedia Reports
            <span className="ml-2 text-xs font-medium text-electric-blue uppercase tracking-wider px-2 py-0.5 rounded border border-electric-blue/30 bg-electric-blue/5">
              Admin Panel
            </span>
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
            {platformItems.map((item) => {
              const isActive = location.pathname === item.path ||
                (item.path !== '/dashboard' && location.pathname.startsWith(item.path));
              return (
                <button
                  key={item.path}
                  onClick={() => navigate(item.path)}
                  className={
                    isActive
                      ? 'text-primary dark:text-gray-100 border-b-2 border-primary dark:border-gray-100 pb-1 h-full flex items-center pt-1 text-sm font-semibold'
                      : 'text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors h-full flex items-center text-sm font-medium'
                  }
                  title={`Go to ${item.label}`}
                >
                  {item.label}
                </button>
              );
            })}
            {adminSectionItems.map((item) => {
              const isActive = location.pathname === item.path;
              return (
                <button
                  key={item.path}
                  onClick={() => navigate(item.path)}
                  className={
                    isActive
                      ? 'text-primary dark:text-gray-100 border-b-2 border-primary dark:border-gray-100 pb-1 h-full flex items-center pt-1 text-sm font-semibold'
                      : 'text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors h-full flex items-center text-sm font-medium'
                  }
                  title="Admin section"
                >
                  {item.label}
                </button>
              );
            })}
          </div>
        </div>
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 text-sm">
            <span className="text-secondary dark:text-gray-400 hidden sm:inline">{user?.fullName}</span>
            <span className="material-symbols-outlined text-secondary dark:text-gray-400">account_circle</span>
          </div>
          <button
            onClick={handleSignOut}
            title="Switch platform or sign out"
            className="bg-primary dark:bg-gray-700 text-on-primary dark:text-gray-100 px-4 py-1.5 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm"
          >
            Switch
          </button>
        </div>
      </nav>
      <main className="max-w-[1400px] mx-auto px-gutter-mobile md:px-gutter-tablet lg:px-gutter-desktop py-8 flex flex-col gap-8 min-h-[calc(100dvh-4rem)] pt-24">
        {children}
      </main>
    </div>
  );
}
