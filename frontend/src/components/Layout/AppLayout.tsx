import { useAuth } from '../../context/AuthContext';
import { useTheme } from '../../context/ThemeContext';
import { useNavigate, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';

const navItems = [
  { label: 'Dashboard', path: '/' },
  { label: 'Analytics', path: '/analytics' },
  { label: 'Real-Time Report', path: '/real-time' },
];

export default function AppLayout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();
  const { isDark, toggle } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <div className="bg-canvas-white dark:bg-gray-950 text-on-surface dark:text-gray-100 font-body-md antialiased min-h-screen transition-colors">
      <nav className="fixed top-0 w-full z-50 flex justify-between items-center px-gutter-desktop h-16 bg-pure-surface dark:bg-gray-900 border-b border-whisper-border dark:border-gray-800 transition-colors">
        <div className="flex items-center gap-8">
          <div className="font-headline-lg text-headline-lg font-bold text-primary dark:text-gray-100 flex items-center gap-2">
            <span className="material-symbols-outlined text-electric-blue" style={{ fontVariationSettings: "'FILL' 1" }}>
              local_fire_department
            </span>
            OpsPulse Center
          </div>
          <div className="hidden md:flex gap-6 items-center h-full">
            {navItems.map((item) => (
              <button
                key={item.path}
                onClick={() => navigate(item.path)}
                className={
                  location.pathname === item.path
                    ? 'text-primary dark:text-gray-100 border-b-2 border-primary dark:border-gray-100 pb-1 h-full flex items-center pt-1 font-medium'
                    : 'text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors h-full flex items-center font-medium'
                }
              >
                {item.label}
              </button>
            ))}
          </div>
        </div>
        <div className="flex items-center gap-4">
          <button
            onClick={toggle}
            className="p-2 text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors rounded-full hover:bg-surface-container dark:hover:bg-gray-800"
            title={isDark ? 'Light mode' : 'Dark mode'}
          >
            <span className="material-symbols-outlined">
              {isDark ? 'light_mode' : 'dark_mode'}
            </span>
          </button>
          <div className="hidden lg:flex items-center bg-surface-container-low dark:bg-gray-800 px-3 py-1.5 rounded-full border border-whisper-border dark:border-gray-700 focus-within:border-electric-blue transition-colors">
            <span className="material-symbols-outlined text-muted-slate text-sm mr-2">search</span>
            <input
              className="bg-transparent border-none focus:ring-0 text-sm w-48 text-on-surface dark:text-gray-200 placeholder:text-muted-slate dark:placeholder:text-gray-500 p-0 outline-none"
              placeholder="Search..."
              type="text"
            />
          </div>
          <div className="flex items-center gap-3">
            <button className="p-2 text-secondary dark:text-gray-400 hover:text-primary dark:hover:text-gray-200 transition-colors hover:bg-surface-container dark:hover:bg-gray-800 rounded-full flex items-center justify-center">
              <span className="material-symbols-outlined">notifications</span>
              <span className="absolute top-1 right-1 w-2 h-2 bg-deep-rose rounded-full" />
            </button>
            <div className="flex items-center gap-2 text-sm">
              <span className="text-secondary dark:text-gray-400 hidden sm:inline">{user?.fullName}</span>
              <span className="material-symbols-outlined text-secondary dark:text-gray-400">account_circle</span>
            </div>
            <button
              onClick={logout}
              className="ml-2 bg-primary dark:bg-gray-700 text-on-primary dark:text-gray-100 px-4 py-1.5 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm"
            >
              Sign Out
            </button>
          </div>
        </div>
      </nav>
      <main className="max-w-[1400px] mx-auto px-gutter-mobile md:px-gutter-tablet lg:px-gutter-desktop py-8 flex flex-col gap-8 min-h-[calc(100dvh-4rem)] pt-24">
        {children}
      </main>
    </div>
  );
}
