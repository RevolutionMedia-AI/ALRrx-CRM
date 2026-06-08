import { useNavigate, useLocation } from 'react-router-dom';
import { useSliceAuth } from '../context/SliceAuthContext';

interface NavItem {
  label: string;
  path: string;
  icon: string;
  badge?: string;
}

const navItems: NavItem[] = [
  { label: 'POD Overview', path: '/slice/pod', icon: 'dashboard' },
  { label: 'Agent Overview', path: '/slice/agents', icon: 'groups' },
  { label: 'Shop Overview', path: '/slice', icon: 'storefront' },
  { label: 'File Upload Center', path: '/slice/upload', icon: 'upload_file' },
  { label: 'History & Audit', path: '/slice/history', icon: 'history' },
];

// Reorder to match mockup (POD first, then Agents, then Shop, then Upload, then History)
const navOrder = ['/slice/pod', '/slice/agents', '/slice', '/slice/upload', '/slice/history'];
const sortedNav = [...navItems].sort(
  (a, b) => navOrder.indexOf(a.path) - navOrder.indexOf(b.path)
);

export default function SliceSidebar() {
  const { user, logout } = useSliceAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const handleSignOut = () => {
    logout();
    navigate('/');
  };

  const initials = (user?.fullName ?? user?.email ?? '?')
    .split(' ')
    .map((n) => n[0])
    .join('')
    .substring(0, 2)
    .toUpperCase();

  return (
    <nav className="bg-surface border-r border-whisper-border h-screen w-64 fixed left-0 top-0 flex flex-col py-6 px-4 z-40 hidden md:flex">
      <div className="flex items-center gap-3 mb-8 px-2">
        <div className="h-10 w-10 rounded-full overflow-hidden bg-surface-container flex items-center justify-center border border-whisper-border shrink-0">
          <span className="font-bold text-primary text-sm">{initials}</span>
        </div>
        <div className="flex flex-col overflow-hidden">
          <span className="font-display-hero text-xl font-bold tracking-tight text-primary truncate">SLICE</span>
          <span className="text-xs text-secondary truncate">Operational Intelligence</span>
        </div>
      </div>

      <div className="flex-1 flex flex-col gap-1 overflow-y-auto">
        {sortedNav.map((item) => {
          const active =
            item.path === '/slice'
              ? location.pathname === '/slice'
              : location.pathname.startsWith(item.path);
          return (
            <a
              key={item.path}
              onClick={(e) => {
                e.preventDefault();
                navigate(item.path);
              }}
              href={item.path}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-colors group cursor-pointer ${
                active
                  ? 'text-primary font-semibold bg-surface-container'
                  : 'text-secondary hover:bg-surface-container hover:text-primary'
              }`}
            >
              <span
                className="material-symbols-outlined text-[1.25rem]"
                style={active ? { fontVariationSettings: "'FILL' 1" } : undefined}
              >
                {item.icon}
              </span>
              <span className="font-medium">{item.label}</span>
              {item.badge && (
                <span className="ml-auto text-[10px] font-metadata-mono px-1.5 py-0.5 bg-electric-blue/10 text-electric-blue rounded">
                  {item.badge}
                </span>
              )}
            </a>
          );
        })}
      </div>

      <div className="mt-auto pt-4 border-t border-whisper-border flex flex-col gap-1">
        <button className="w-full mb-2 px-4 py-2 bg-primary text-on-primary rounded-lg font-medium hover:bg-primary/90 transition-colors shadow-sm text-sm">
          System Settings
        </button>
        <a className="flex items-center gap-3 px-3 py-2 rounded-lg text-secondary hover:bg-surface-container hover:text-primary transition-colors group cursor-pointer" href="#">
          <span className="material-symbols-outlined text-[1.25rem] group-hover:text-primary transition-colors">settings</span>
          <span className="font-medium">Settings</span>
        </a>
        <a className="flex items-center gap-3 px-3 py-2 rounded-lg text-secondary hover:bg-surface-container hover:text-primary transition-colors group cursor-pointer" href="#">
          <span className="material-symbols-outlined text-[1.25rem] group-hover:text-primary transition-colors">help_outline</span>
          <span className="font-medium">Support</span>
        </a>
        <button
          onClick={handleSignOut}
          className="flex items-center gap-3 px-3 py-2 rounded-lg text-deep-rose hover:bg-deep-rose/10 transition-colors group cursor-pointer"
        >
          <span className="material-symbols-outlined text-[1.25rem]">logout</span>
          <span className="font-medium">Sign Out</span>
        </button>
      </div>
    </nav>
  );
}
