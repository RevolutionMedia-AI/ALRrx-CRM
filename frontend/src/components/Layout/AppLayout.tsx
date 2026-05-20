import { useAuth } from '../../context/AuthContext';
import { useNavigate, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';

const navItems = [
  { label: 'Dashboard', path: '/' },
  { label: 'Analytics', path: '/analytics' },
  { label: 'Real-Time Report', path: '/real-time' },
];

export default function AppLayout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <div className="bg-canvas-white text-on-surface font-body-md antialiased min-h-screen">
      <nav className="fixed top-0 w-full z-50 flex justify-between items-center px-gutter-desktop h-16 bg-pure-surface border-b border-whisper-border">
        <div className="flex items-center gap-8">
          <div className="font-headline-lg text-headline-lg font-bold text-primary flex items-center gap-2">
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
                    ? 'text-primary border-b-2 border-primary pb-1 h-full flex items-center pt-1 font-medium'
                    : 'text-secondary hover:text-primary transition-colors h-full flex items-center font-medium'
                }
              >
                {item.label}
              </button>
            ))}
          </div>
        </div>
        <div className="flex items-center gap-4">
          <div className="hidden lg:flex items-center bg-surface-container-low px-3 py-1.5 rounded-full border border-whisper-border focus-within:border-electric-blue transition-colors">
            <span className="material-symbols-outlined text-muted-slate text-sm mr-2">search</span>
            <input
              className="bg-transparent border-none focus:ring-0 text-sm w-48 text-on-surface placeholder:text-muted-slate p-0 outline-none"
              placeholder="Search..."
              type="text"
            />
          </div>
          <div className="flex items-center gap-3">
            <button className="p-2 text-secondary hover:text-primary transition-colors hover:bg-surface-container rounded-full flex items-center justify-center">
              <span className="material-symbols-outlined">notifications</span>
              <span className="absolute top-1 right-1 w-2 h-2 bg-deep-rose rounded-full" />
            </button>
            <div className="flex items-center gap-2 text-sm">
              <span className="text-secondary hidden sm:inline">{user?.fullName}</span>
              <span className="material-symbols-outlined text-secondary">account_circle</span>
            </div>
            <button
              onClick={logout}
              className="ml-2 bg-primary text-on-primary px-4 py-1.5 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm"
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
