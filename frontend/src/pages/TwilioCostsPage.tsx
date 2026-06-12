import { useEffect, useState, useCallback } from 'react';
import { twilioApi, type TwilioSummary, type TwilioCall, type TwilioDailyCost } from '../services/twilioApi';
import {
  CallOutgoing01Icon,
  CallReceived02Icon,
  Call02Icon,
  Money01Icon,
  Clock01Icon,
  Refresh01Icon,
  PauseIcon,
  PlayIcon,
  Pdf01Icon,
  GoogleSheetIcon,
} from 'hugeicons-react';

type Period = 'Today' | 'Week' | 'Month';
const PERIOD_API: Record<Period, string> = { Today: 'today', Week: 'week', Month: 'month' };

// Formatters
function fmtCost(n: number | null | undefined): string {
  if (n == null || !isFinite(n) || isNaN(n)) return '—';
  const abs = Math.abs(n);
  if (abs === 0) return '$0';
  if (abs < 1) return '$' + n.toFixed(7);
  if (abs < 100) return '$' + n.toFixed(4);
  return '$' + n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function fmtDuration(s: number): string {
  const m = Math.floor(s / 60);
  const sec = s % 60;
  return `${m}:${sec.toString().padStart(2, '0')}`;
}

function fmtTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString('en-US', {
      hour: '2-digit', minute: '2-digit', day: '2-digit', month: 'short',
    });
  } catch { return '-'; }
}

// Safely extract an error message string from an unknown axios error.
// The backend may return a ProblemDetails-style { title, detail } object
// (via ExceptionHandlingMiddleware), or { error, detail }, or just a string.
// We must never return an object, since React will throw #31 when trying
// to render an object as a child.
function extractTwilioError(err: any, fallback: string): string {
  if (!err) return fallback;
  const data = err?.response?.data;
  if (typeof data === 'string') return data;
  if (data && typeof data === 'object') {
    if (typeof data.error === 'string') return data.error;
    if (data.error && typeof data.error === 'object') {
      if (typeof data.error.title === 'string') return data.error.title;
      if (typeof data.error.detail === 'string') return data.error.detail;
    }
    if (typeof data.title === 'string') return data.title;
    if (typeof data.detail === 'string') return data.detail;
    if (typeof data.message === 'string') return data.message;
  }
  if (typeof err.message === 'string') return err.message;
  return fallback;
}

// Skeleton row
function SkeletonLine({ width = 'w-full' }: { width?: string }) {
  return <div className={`${width} h-3 bg-card-icon-bg dark:bg-gray-800 rounded animate-pulse`} />;
}

// Page
export default function TwilioCostsPage() {
  const [period, setPeriod] = useState<Period>('Today');
  const [summary, setSummary] = useState<TwilioSummary | null>(null);
  const [calls, setCalls] = useState<TwilioCall[]>([]);
  const [, setDaily] = useState<TwilioDailyCost[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastRefresh, setLastRefresh] = useState<Date>(new Date());
  const [exportingPdf, setExportingPdf] = useState(false);
  const [exportingExcel, setExportingExcel] = useState(false);

  const loadData = useCallback(async () => {
    try {
      setError(null);
      const [sum, recent, dailyData] = await Promise.all([
        twilioApi.getSummary(PERIOD_API[period] as 'today' | 'week' | 'month'),
        twilioApi.getRecentCalls(PERIOD_API[period] as 'today' | 'week' | 'month', 20),
        twilioApi.getDailyCosts(PERIOD_API[period] as 'today' | 'week' | 'month'),
      ]);
      setSummary(sum);
      setCalls(recent);
      setDaily(dailyData);
      setLastRefresh(new Date());
    } catch (err: any) {
      setError(extractTwilioError(err, 'Error loading Twilio data'));
    } finally {
      setLoading(false);
    }
  }, [period]);

  useEffect(() => { loadData(); }, [loadData]);

  useEffect(() => {
    if (!autoRefresh) return;
    const id = setInterval(loadData, 30_000);
    return () => clearInterval(id);
  }, [autoRefresh, loadData]);

  const handleExportPdf = useCallback(async () => {
    setExportingPdf(true);
    try {
      const blob = await twilioApi.exportPdf(PERIOD_API[period] as 'today' | 'week' | 'month');
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `ALRrx_TwilioCosts_${PERIOD_API[period]}_${new Date().toISOString().split('T')[0]}.pdf`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('PDF export failed', err);
    } finally {
      setExportingPdf(false);
    }
  }, [period]);

  const handleExportExcel = useCallback(async () => {
    setExportingExcel(true);
    try {
      const blob = await twilioApi.exportExcel(PERIOD_API[period] as 'today' | 'week' | 'month');
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `ALRrx_TwilioCosts_${PERIOD_API[period]}_${new Date().toISOString().split('T')[0]}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('Excel export failed', err);
    } finally {
      setExportingExcel(false);
    }
  }, [period]);

  const hasCalls = calls.length > 0;

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between flex-wrap gap-4">
        <div>
          <h1 className="font-display-hero text-display-hero text-primary dark:text-white">
            Twilio Costs
          </h1>
          <p className="font-body-md text-secondary dark:text-gray-400 mt-1">
            Real-time spend tracking across inbound and outbound voice.{' '}
            <span className="font-metadata-mono text-primary dark:text-gray-300">
              {lastRefresh.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
            </span>
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={handleExportExcel}
            disabled={exportingExcel || loading}
            title="Export Excel"
            className="h-10 px-3 rounded-lg border border-whisper-border dark:border-gray-700 text-primary dark:text-gray-200 hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors flex items-center gap-2 text-sm font-medium disabled:opacity-50"
          >
            <GoogleSheetIcon size={16} className="text-emerald-signal" />
            <span className="hidden sm:inline">{exportingExcel ? 'Generating...' : 'Excel'}</span>
          </button>
          <button
            onClick={handleExportPdf}
            disabled={exportingPdf || loading}
            title="Export PDF"
            className="h-10 px-3 rounded-lg border border-whisper-border dark:border-gray-700 text-primary dark:text-gray-200 hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors flex items-center gap-2 text-sm font-medium disabled:opacity-50"
          >
            <Pdf01Icon size={16} className="text-deep-rose" />
            <span className="hidden sm:inline">{exportingPdf ? 'Generating...' : 'PDF'}</span>
          </button>
          <div className="w-px h-6 bg-whisper-border dark:bg-gray-700 mx-1" />
          <button
            onClick={() => setAutoRefresh(!autoRefresh)}
            title={autoRefresh ? 'Pause auto-refresh' : 'Resume auto-refresh'}
            className="w-10 h-10 rounded-lg border border-whisper-border dark:border-gray-700 text-primary dark:text-gray-200 hover:bg-card-icon-bg dark:hover:bg-gray-800 transition-colors flex items-center justify-center"
          >
            {autoRefresh ? <PauseIcon size={16} /> : <PlayIcon size={16} />}
          </button>
          <button
            onClick={loadData}
            title="Refresh now"
            className="w-10 h-10 rounded-lg bg-electric-blue text-white hover:scale-[0.98] transition-transform shadow-sm flex items-center justify-center"
          >
            <Refresh01Icon size={16} />
          </button>
        </div>
      </div>

      <div className="flex items-center gap-2 flex-wrap">
        {(['Today', 'Week', 'Month'] as Period[]).map((p) => {
          const isActive = period === p;
          return (
            <button
              key={p}
              onClick={() => setPeriod(p)}
              className={
                isActive
                  ? 'px-5 py-2 rounded-lg bg-electric-blue text-white font-medium shadow-sm transition-all text-sm'
                  : 'px-5 py-2 rounded-lg bg-canvas-white dark:bg-gray-800 text-secondary dark:text-gray-300 hover:bg-card-icon-bg dark:hover:bg-gray-700 font-medium transition-all text-sm border border-whisper-border dark:border-gray-700'
              }
            >
              {p}
            </button>
          );
        })}
        {autoRefresh && (
          <span className="ml-2 inline-flex items-center gap-1.5 font-metadata-mono text-xs text-emerald-signal">
            <span className="relative flex h-2 w-2">
              <span className="absolute inline-flex h-full w-full rounded-full bg-emerald-signal opacity-60 animate-ping" />
              <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-signal" />
            </span>
          </span>
        )}
      </div>

      {error && (
        <div className="px-5 py-4 rounded-lg border border-deep-rose/40 bg-deep-rose/10 text-deep-rose font-body-md text-sm">
          {error}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <KpiCard
          icon={<Money01Icon size={20} className="text-electric-blue" />}
          iconBg="bg-electric-blue/10"
          label="Total Spend"
          value={summary ? fmtCost(summary.totalCost) : null}
          sub={summary?.currency || 'USD'}
          loading={loading}
        />
        <KpiCard
          icon={<Call02Icon size={20} className="text-emerald-signal" />}
          iconBg="bg-emerald-signal/10"
          label="Calls"
          value={summary ? summary.totalCalls.toLocaleString('en-US') : null}
          sub={summary ? `${summary.inboundCalls} in - ${summary.outboundCalls} out` : '-'}
          loading={loading}
        />
        <KpiCard
          icon={<Clock01Icon size={20} className="text-amber-warmth" />}
          iconBg="bg-amber-warmth/10"
          label="Minutes"
          value={summary ? summary.totalMinutes.toLocaleString('en-US') : null}
          sub={summary && summary.totalCalls > 0
            ? `~${Math.round(summary.totalMinutes / summary.totalCalls)} min/call`
            : '-'}
          loading={loading}
        />
        <KpiCard
          icon={<Money01Icon size={20} className="text-muted-slate" />}
          iconBg="bg-muted-slate/10"
          label="Cost / Minute"
          value={summary && summary.totalMinutes > 0 ? fmtCost(summary.totalCost / summary.totalMinutes) : null}
          sub="blended average"
          loading={loading}
        />
      </div>

      <div className="bg-pure-surface dark:bg-gray-900 rounded-lg shadow-card border border-whisper-border dark:border-gray-800 overflow-hidden">
        <div className="flex items-baseline justify-between px-6 py-4 border-b border-whisper-border dark:border-gray-800">
          <h2 className="font-metadata-mono text-xs uppercase tracking-wider text-secondary dark:text-gray-400">
            Recent Calls
          </h2>
          <span className="font-metadata-mono text-xs text-secondary dark:text-gray-500">
            Last 20 - {hasCalls ? `${calls.length}` : '-'}
          </span>
        </div>

        <div className="hidden md:grid grid-cols-[1.1fr_0.7fr_1.1fr_1.1fr_0.8fr_0.5fr_0.8fr] gap-3 px-6 py-3 bg-card-icon-bg/50 dark:bg-gray-800/50 border-b border-whisper-border dark:border-gray-800">
          {['Time', 'Dir.', 'From', 'To', 'Status', 'Dur.', 'Cost'].map((h) => (
            <span key={h} className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-500 font-semibold">
              {h}
            </span>
          ))}
        </div>

        {loading && !summary ? (
          <div className="divide-y divide-whisper-border dark:divide-gray-800">
            {Array.from({ length: 8 }).map((_, i) => (
              <div key={i} className="grid grid-cols-[1.1fr_0.7fr_1.1fr_1.1fr_0.8fr_0.5fr_0.8fr] gap-3 px-6 py-4">
                <SkeletonLine width="w-24" />
                <SkeletonLine width="w-12" />
                <SkeletonLine width="w-32" />
                <SkeletonLine width="w-28" />
                <SkeletonLine width="w-20" />
                <SkeletonLine width="w-10" />
                <SkeletonLine width="w-24" />
              </div>
            ))}
          </div>
        ) : !hasCalls ? (
          <div className="px-6 py-16 text-center">
            <p className="font-metadata-mono text-xs uppercase tracking-wider text-secondary dark:text-gray-500">
              No recent calls
            </p>
            <p className="font-body-md text-sm text-secondary dark:text-gray-500 mt-2 max-w-md mx-auto">
              Once Twilio records call activity it will appear here in reverse chronological order.
            </p>
          </div>
        ) : (
          <div className="divide-y divide-whisper-border dark:divide-gray-800">
            {calls.map((c) => {
              const isIn = c.direction === 'inbound';
              return (
                <div
                  key={c.sid}
                  className="grid grid-cols-2 md:grid-cols-[1.1fr_0.7fr_1.1fr_1.1fr_0.8fr_0.5fr_0.8fr] gap-3 px-6 py-4 items-center hover:bg-card-icon-bg/30 dark:hover:bg-gray-800/30 transition-colors"
                >
                  <span className="font-metadata-mono text-xs text-primary dark:text-gray-200">
                    {fmtTime(c.startTime)}
                  </span>
                  <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-metadata-mono uppercase tracking-wider w-fit ${
                    isIn
                      ? 'bg-electric-blue/8 text-electric-blue'
                      : 'bg-emerald-signal/8 text-emerald-signal'
                  }`}>
                    {isIn
                      ? <CallReceived02Icon size={12} />
                      : <CallOutgoing01Icon size={12} />
                    }
                    {isIn ? 'in' : 'out'}
                  </span>
                  <span className="font-metadata-mono text-xs text-secondary dark:text-gray-400 truncate">
                    {c.from}
                  </span>
                  <span className="font-metadata-mono text-xs text-secondary dark:text-gray-400 truncate">
                    {c.to}
                  </span>
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-metadata-mono uppercase tracking-wider w-fit ${
                    c.status === 'completed'
                      ? 'bg-emerald-signal/10 text-emerald-signal'
                      : c.status === 'no-answer' || c.status === 'busy'
                      ? 'bg-amber-warmth/10 text-amber-warmth'
                      : 'bg-muted-slate/10 text-muted-slate'
                  }`}>
                    <span className={`w-1.5 h-1.5 rounded-full ${
                      c.status === 'completed' ? 'bg-emerald-signal'
                      : c.status === 'no-answer' || c.status === 'busy' ? 'bg-amber-warmth'
                      : 'bg-muted-slate'
                    }`} />
                    {c.status}
                  </span>
                  <span className="font-metadata-mono text-xs text-primary dark:text-gray-300 md:text-right">
                    {fmtDuration(c.durationSeconds)}
                  </span>
                  <span className="font-metadata-mono text-xs text-primary dark:text-white md:text-right font-semibold">
                    {fmtCost(c.cost)}
                    <span className="text-muted-slate ml-1 text-[10px] font-normal">{c.currency}</span>
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <p className="text-xs text-muted-slate dark:text-gray-500 font-metadata-mono text-center pt-2">
        Twilio - ALRrx SIP trunk - auto-refresh 30s - admin only
      </p>
    </div>
  );
}

function KpiCard({
  icon, iconBg, label, value, sub, loading,
}: {
  icon: React.ReactNode;
  iconBg: string;
  label: string;
  value: string | null;
  sub: string;
  loading: boolean;
}) {
  return (
    <div className="bg-pure-surface dark:bg-gray-900 rounded-lg shadow-card border border-whisper-border dark:border-gray-800 p-6">
      <div className="flex items-start justify-between mb-3">
        <p className="font-metadata-mono text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-semibold">
          {label}
        </p>
        <div className={`w-8 h-8 rounded-lg ${iconBg} flex items-center justify-center`}>
          {icon}
        </div>
      </div>
      {loading && !value ? (
        <>
          <div className="h-9 w-32 bg-card-icon-bg dark:bg-gray-800 animate-pulse rounded mb-2" />
          <div className="h-3 w-20 bg-card-icon-bg dark:bg-gray-800 animate-pulse rounded" />
        </>
      ) : (
        <>
          <p className="font-metadata-mono text-2xl md:text-3xl tracking-tight text-primary dark:text-white tabular-nums">
            {value}
          </p>
          <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-500 mt-2">
            {sub}
          </p>
        </>
      )}
    </div>
  );
}
