import { useEffect, useState, useCallback, useRef } from 'react';
import { getStaffing, getDashboardSummary } from '../services/api';
import type { DashboardSummaryDto, ReportDto } from '../types';

type AgentStatus = 'READY' | 'INCALL' | 'QUEUE' | 'PAUSED' | 'OFFLINE';

const STATUS_COLORS: Record<AgentStatus, { bg: string; text: string; dot: string; label: string }> = {
  READY:    { bg: 'bg-emerald-signal/8', text: 'text-emerald-signal', dot: 'bg-emerald-signal', label: 'Available' },
  INCALL:   { bg: 'bg-electric-blue/8', text: 'text-electric-blue', dot: 'bg-electric-blue', label: 'On Call' },
  QUEUE:    { bg: 'bg-amber-warmth/8', text: 'text-amber-warmth', dot: 'bg-amber-warmth', label: 'In Queue' },
  PAUSED:   { bg: 'bg-deep-rose/8', text: 'text-deep-rose', dot: 'bg-deep-rose', label: 'Paused' },
  OFFLINE:  { bg: 'bg-muted-slate/8', text: 'text-muted-slate', dot: 'bg-muted-slate', label: 'Offline' },
};

function getInitials(name: string): string {
  if (!name) return '--';
  return name.split(' ').map((n) => n[0]).join('').substring(0, 2).toUpperCase();
}

function elapsed(isoDate: string | null): string {
  if (!isoDate) return '';
  const diff = Math.floor((Date.now() - new Date(isoDate).getTime()) / 1000);
  if (diff < 60) return `${diff}s`;
  const m = Math.floor(diff / 60);
  const s = diff % 60;
  if (m < 60) return `${m}m ${s}s`;
  const h = Math.floor(m / 60);
  return `${h}h ${m % 60}m`;
}

interface AgentRow {
  Supervisor: string;
  Emp_Number: string;
  Name: string;
  User: string;
  Status: string;
  last_call_time: string | null;
  last_update_time: string | null;
}

export default function RealTimePage() {
  const [staffing, setStaffing] = useState<ReportDto | null>(null);
  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const fetchData = useCallback(async () => {
    try {
      const [st, sm] = await Promise.all([
        getStaffing().catch(() => null),
        getDashboardSummary({ period: 'Today' }).catch(() => null),
      ]);
      setStaffing(st);
      setSummary(sm);
      setError(null);
    } catch {
      setError('Failed to load live data');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
    intervalRef.current = setInterval(() => {
      fetchData();
      setTick((t) => t + 1);
    }, 10000);
    return () => { if (intervalRef.current) clearInterval(intervalRef.current); };
  }, [fetchData]);

  const agents = (staffing?.rows ?? []) as unknown as AgentRow[];

  const statusCounts = {
    ready: agents.filter((a) => a.Status === 'READY').length,
    incall: agents.filter((a) => a.Status === 'INCALL' || a.Status === 'QUEUE').length,
    paused: agents.filter((a) => a.Status === 'PAUSED').length,
    offline: agents.filter((a) => !a.Status || a.Status === 'OFFLINE').length,
  };

  const totalOnline = statusCounts.ready + statusCounts.incall + statusCounts.paused;
  const totalSales = summary?.metrics.find((m) => m.label.toLowerCase().includes('sales today'))?.value ?? '--';
  const totalCalls = summary?.metrics.find((m) => m.label.toLowerCase().includes('total calls'))?.value ?? '--';
  const contacts = summary?.metrics.find((m) => m.label.toLowerCase().includes('contacts'))?.value ?? '--';
  const occupancy = summary?.metrics.find((m) => m.label.toLowerCase().includes('occupancy'))?.value ?? '--%';
  const leadsDialed = summary?.metrics.find((m) => m.label.toLowerCase().includes('leads dialed'))?.value ?? '--';
  const leadsContacted = summary?.metrics.find((m) => m.label.toLowerCase().includes('leads contacted'))?.value ?? '--';
  const contactRateVal = summary?.metrics.find((m) => m.label.toLowerCase().includes('contact rate'))?.value ?? '--%';

  return (
    <>
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-end gap-4 border-b border-whisper-border pb-5">
        <div>
          <h1 className="font-headline-lg text-headline-lg font-bold text-primary tracking-tight">Real-Time Report</h1>
          <p className="text-secondary text-sm mt-1">
            Live agent monitoring — auto-refreshes every 10s
            {summary ? ` • ${new Date(summary.lastUpdated).toLocaleTimeString()}` : ''}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <span className="flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium text-emerald-signal bg-emerald-signal/8">
            <span className="w-2 h-2 rounded-full bg-emerald-signal animate-pulse" />
            Live
          </span>
          <span className="text-xs text-secondary">{tick}</span>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/8 border border-deep-rose/15 rounded-xl p-4 text-deep-rose text-sm mt-4">
          {error}
        </div>
      )}

      <section className="grid grid-cols-3 gap-4 mt-6">
        {[
          { label: 'Leads Dialed', value: leadsDialed, bg: 'bg-electric-blue/5 dark:bg-electric-blue/10', border: 'border-electric-blue/15 dark:border-electric-blue/25', textColor: 'text-electric-blue dark:text-blue-300' },
          { label: 'Leads Contacted', value: leadsContacted, bg: 'bg-emerald-signal/5 dark:bg-emerald-signal/10', border: 'border-emerald-signal/15 dark:border-emerald-signal/25', textColor: 'text-emerald-signal dark:text-emerald-300' },
          { label: 'Contact Rate', value: contactRateVal, bg: 'bg-violet-500/5 dark:bg-violet-500/10', border: 'border-violet-500/15 dark:border-violet-500/25', textColor: 'text-violet-500 dark:text-violet-300' },
        ].map((l) => (
          <div key={l.label} className={`${l.bg} ${l.border} border rounded-xl p-5 transition-colors`}>
            <p className="text-secondary dark:text-gray-400 text-sm">{l.label}</p>
            <p className={`text-2xl font-bold mt-1 ${l.textColor}`}>
              {loading ? '--' : l.value}
            </p>
          </div>
        ))}
      </section>

      <section className="grid grid-cols-2 lg:grid-cols-4 gap-4 mt-6">
        {[
          { label: 'Available', count: statusCounts.ready, color: STATUS_COLORS.READY },
          { label: 'On Call', count: statusCounts.incall, color: STATUS_COLORS.INCALL },
          { label: 'Paused', count: statusCounts.paused, color: STATUS_COLORS.PAUSED },
          { label: 'Offline', count: statusCounts.offline, color: STATUS_COLORS.OFFLINE },
        ].map((s) => (
          <div key={s.label} className="border border-whisper-border rounded-xl p-6 flex items-center justify-between">
            <div>
              <p className="text-secondary text-sm">{s.label}</p>
              <p className="text-3xl font-bold text-primary mt-1">
                {loading ? '--' : s.count}
              </p>
            </div>
            <span className={`w-3 h-3 rounded-full ${s.color.dot}`} />
          </div>
        ))}
      </section>

      <section className="mt-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="font-bold text-lg text-primary">Agent Status ({totalOnline} online)</h2>
        </div>
        {loading ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
            {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
              <div key={i} className="h-28 border border-whisper-border rounded-xl bg-surface-container animate-pulse" />
            ))}
          </div>
        ) : agents.length > 0 ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
            {agents.map((agent) => {
              const s = (agent.Status?.toUpperCase() || 'OFFLINE') as AgentStatus;
              const c = STATUS_COLORS[s];
              const time = s === 'INCALL' || s === 'QUEUE'
                ? elapsed(agent.last_call_time)
                : s === 'PAUSED'
                ? elapsed(agent.last_update_time)
                : '';
              return (
                <div key={agent.User} className={`border border-whisper-border rounded-xl p-5 ${c.bg}`}>
                  <div className="flex items-start justify-between mb-3">
                    <div className="flex items-center gap-3">
                      <div className="w-9 h-9 rounded-full bg-pure-surface border border-whisper-border flex items-center justify-center text-primary font-bold text-xs">
                        {getInitials(agent.Name ?? agent.User ?? '')}
                      </div>
                      <div>
                        <p className="text-sm font-medium text-primary leading-tight">{agent.Name ?? agent.User ?? '--'}</p>
                        <p className="text-[11px] text-secondary font-metadata-mono">#{agent.Emp_Number ?? agent.User ?? '--'}</p>
                      </div>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <span className={`w-2 h-2 rounded-full ${c.dot}`} />
                      <span className={`text-[11px] font-medium ${c.text}`}>{c.label}</span>
                    </div>
                  </div>
                  {time && (
                    <div className="flex items-center gap-2 text-[11px] text-secondary font-metadata-mono">
                      <span className="material-symbols-outlined text-[14px]">timer</span>
                      {s === 'INCALL' || s === 'QUEUE' ? 'Call: ' : 'Paused: '}
                      <span className="font-medium text-primary">{time}</span>
                    </div>
                  )}
                  {!time && (
                    <div className="flex items-center gap-2 text-[11px] text-secondary font-metadata-mono">
                      <span className="material-symbols-outlined text-[14px]">schedule</span>
                      {s === 'READY' ? 'Waiting for calls' : s === 'OFFLINE' ? 'Not logged in' : '\u00A0'}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        ) : (
          <div className="border border-whisper-border rounded-xl bg-surface-container-low flex items-center justify-center p-16 text-muted-slate text-sm">
            No agents found
          </div>
        )}
      </section>

      <section className="mt-6 mb-8">
        <h2 className="font-bold text-lg text-primary mb-4">Today&apos;s Running Totals</h2>
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[
            { label: 'Total Calls', value: totalCalls, color: 'text-electric-blue' },
            { label: 'Sales Today', value: totalSales, color: 'text-emerald-signal' },
            { label: 'Contacts', value: contacts, color: 'text-amber-warmth' },
            { label: 'Occupancy', value: occupancy, color: 'text-violet-500' },
          ].map((kpi) => (
            <div key={kpi.label} className="border border-whisper-border rounded-xl p-5">
              <p className="text-secondary text-sm">{kpi.label}</p>
              <p className={`text-2xl font-bold mt-1 ${kpi.color}`}>
                {loading ? '--' : kpi.value}
              </p>
            </div>
          ))}
        </div>
      </section>
    </>
  );
}
