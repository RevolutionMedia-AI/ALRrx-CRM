import { useState, useRef, useEffect } from 'react';

interface ProductivityCardProps {
  aht?: string;
  occupancy?: string;
  loading?: boolean;
  occupancyStatus?: string;
}

export default function ProductivityCard({ aht, occupancy, loading, occupancyStatus }: ProductivityCardProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [open]);

  const ahtDisplay = aht ?? '--';
  const occupancyDisplay = occupancy ? `${occupancy}%` : '--%';
  const statusColor =
    occupancyStatus === 'Optimal' || occupancyStatus === 'optimal'
      ? 'text-emerald-signal'
      : occupancyStatus === 'High' || occupancyStatus === 'high'
      ? 'text-deep-rose'
      : 'text-muted-slate';

  return (
    <div ref={ref} className="relative inline-block">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-2 px-3 py-1.5 bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg text-xs text-secondary hover:text-primary hover:border-electric-blue/40 transition-all shadow-sm"
        aria-label="Show productivity details"
      >
        <span className="material-symbols-outlined text-[16px] text-electric-blue">monitoring</span>
        <span className="font-medium">Productivity</span>
        <span className="material-symbols-outlined text-[14px]">info</span>
      </button>

      {open && (
        <div className="absolute z-30 left-0 top-full mt-2 w-64 bg-pure-surface dark:bg-gray-800 border border-whisper-border dark:border-gray-600 rounded-xl shadow-diffused p-4 animate-in fade-in slide-in-from-top-2">
          <div className="flex items-center justify-between mb-3">
            <p className="text-xs font-bold text-secondary uppercase tracking-wider">Productivity</p>
            <button
              onClick={() => setOpen(false)}
              className="text-muted-slate hover:text-primary transition-colors"
              aria-label="Close"
            >
              <span className="material-symbols-outlined text-[16px]">close</span>
            </button>
          </div>
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-[18px] text-electric-blue">timer</span>
                <span className="text-sm text-secondary">Avg Handle Time</span>
              </div>
              {loading ? (
                <div className="h-4 w-12 bg-surface-container rounded animate-pulse" />
              ) : (
                <span className="text-sm font-bold text-primary font-metadata-mono">{ahtDisplay}</span>
              )}
            </div>
            <div className="h-px bg-whisper-border" />
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-[18px] text-electric-blue">pie_chart</span>
                <span className="text-sm text-secondary">Occupancy</span>
              </div>
              {loading ? (
                <div className="h-4 w-12 bg-surface-container rounded animate-pulse" />
              ) : (
                <div className="flex items-center gap-2">
                  <span className="text-sm font-bold text-primary font-metadata-mono">{occupancyDisplay}</span>
                  {occupancyStatus && (
                    <span className={`text-[10px] font-bold uppercase ${statusColor}`}>
                      {occupancyStatus}
                    </span>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
