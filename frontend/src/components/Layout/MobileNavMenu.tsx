import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

export interface MobileNavItem {
  label: string;
  path: string;
  startsWith?: boolean;
}

export default function MobileNavMenu({ items }: { items: MobileNavItem[] }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!open) return;
    const onClick = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const isActive = (path: string, startsWith: boolean | undefined) =>
    startsWith ? location.pathname.startsWith(path) : location.pathname === path;

  if (!items.length) return null;

  return (
    <div ref={ref} className="relative md:hidden">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        className="flex h-9 w-9 items-center justify-center rounded-md border border-whisper-border dark:border-gray-700 bg-surface-container-low dark:bg-gray-800 text-primary dark:text-gray-100 hover:border-electric-blue transition-all"
        aria-label={open ? 'Close navigation menu' : 'Open navigation menu'}
        aria-expanded={open}
      >
        <span className="material-symbols-outlined text-[20px]">
          {open ? 'close' : 'menu'}
        </span>
      </button>
      {open ? (
        <div className="absolute left-0 top-full mt-2 w-60 overflow-hidden rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-900 shadow-2xl z-50">
          <div className="border-b border-whisper-border dark:border-gray-700 px-3 py-2 text-[10px] font-metadata-mono uppercase tracking-widest text-secondary dark:text-gray-400">
            Navigation
          </div>
          <ul className="flex flex-col">
            {items.map((item) => {
              const active = isActive(item.path, item.startsWith);
              return (
                <li key={item.path}>
                  <button
                    type="button"
                    onClick={() => {
                      setOpen(false);
                      navigate(item.path);
                    }}
                    className={`flex w-full items-center gap-2.5 px-3 py-2.5 text-sm font-medium transition-colors ${
                      active
                        ? 'bg-electric-blue/10 text-electric-blue'
                        : 'text-primary dark:text-gray-200 hover:bg-surface-container-low dark:hover:bg-gray-800'
                    }`}
                  >
                    <span
                      className={`h-1.5 w-1.5 shrink-0 rounded-full ${active ? 'bg-electric-blue' : 'bg-whisper-border dark:bg-gray-700'}`}
                    />
                    <span className="flex-1 text-left">{item.label}</span>
                    {active ? (
                      <span className="material-symbols-outlined text-[16px]">chevron_right</span>
                    ) : null}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
