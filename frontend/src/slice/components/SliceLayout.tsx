import type { ReactNode } from 'react';
import SliceSidebar from './SliceSidebar';
import { useSliceAuth } from '../context/SliceAuthContext';

export default function SliceLayout({ children }: { children: ReactNode }) {
  const { user } = useSliceAuth();
  return (
    <div className="bg-canvas-white text-on-surface font-body-md antialiased min-h-screen flex">
      <SliceSidebar />
      <main className="flex-1 flex flex-col min-w-0 md:ml-64 bg-canvas-white">
        <header className="bg-surface border-b border-whisper-border sticky top-0 flex justify-between items-center w-full h-16 px-gutter-desktop z-30 shadow-sm">
          <div className="flex items-center gap-4">
            <h1 className="text-lg font-bold text-primary hidden md:block">SLICE Operational Dashboard</h1>
            <h1 className="text-lg font-bold text-primary md:hidden">SLICE</h1>
          </div>
          <div className="flex items-center gap-3">
            <div className="relative hidden lg:block">
              <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-secondary text-sm">search</span>
              <input
                className="pl-9 pr-4 py-1.5 bg-surface border border-whisper-border rounded-full text-sm text-on-surface focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-colors w-56"
                placeholder="Search..."
                type="text"
              />
            </div>
            <div className="flex items-center gap-1 border-r border-whisper-border pr-3 mr-1">
              <button className="p-2 text-secondary hover:text-primary transition-colors rounded-full hover:bg-surface-container">
                <span className="material-symbols-outlined text-xl">filter_alt</span>
              </button>
              <button className="p-2 text-secondary hover:text-primary transition-colors rounded-full hover:bg-surface-container relative">
                <span className="material-symbols-outlined text-xl">notifications</span>
                <span className="absolute top-2 right-2 w-2 h-2 bg-deep-rose rounded-full"></span>
              </button>
            </div>
            <span className="text-sm text-secondary hidden md:inline">{user?.fullName}</span>
            <div className="h-8 w-8 rounded-full bg-primary text-on-primary flex items-center justify-center text-xs font-bold">
              {(user?.fullName ?? user?.email ?? '?')
                .split(' ')
                .map((n) => n[0])
                .join('')
                .substring(0, 2)
                .toUpperCase()}
            </div>
          </div>
        </header>
        <div className="flex-1 overflow-auto p-gutter-mobile md:p-gutter-tablet lg:p-gutter-desktop">
          <div className="max-w-container-max mx-auto h-full flex flex-col gap-6">{children}</div>
        </div>
      </main>
    </div>
  );
}
