import { useEffect, useState, useCallback, useRef } from 'react';
import {
  AreaChart,
  Area,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
} from 'recharts';
import { twilioApi, type TwilioSummary, type TwilioCall, type TwilioDailyCost } from '../services/twilioApi';

type Period = 'today' | 'week' | 'month';

// ─── Formatters ────────────────────────────────────────────────────────────
// Smart currency formatter: shows enough precision so that small costs
// (e.g. $0.0000142) are still readable. Minimum 7 decimal places
// when value is < 1, otherwise standard 2-4 decimals.
function fmtCost(n: number): string {
  if (!isFinite(n)) return '$0.00';
  if (n === 0) return '$0.0000000';
  if (Math.abs(n) < 1) {
    return '$' + n.toFixed(7);
  }
  if (Math.abs(n) < 100) {
    return '$' + n.toFixed(4);
  }
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
      hour: '2-digit',
      minute: '2-digit',
      day: '2-digit',
      month: 'short',
    });
  } catch {
    return '—';
  }
}

function fmtDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('en-US', {
      day: '2-digit',
      month: 'short',
    });
  } catch {
    return iso;
  }
}

// ─── Skeleton block (used during initial load) ────────────────────────────
function SkeletonLine({ width = 'w-full' }: { width?: string }) {
  return (
    <div className={`${width} h-3 bg-surface-container-high dark:bg-gray-800 rounded-sm animate-pulse`} />
  );
}

// ─── Empty state for charts ───────────────────────────────────────────────
function ChartEmpty({ label }: { label: string }) {
  return (
    <div className="h-[280px] flex flex-col items-center justify-center text-on-surface-variant dark:text-gray-500">
      <div className="w-12 h-12 mb-3 border border-outline-variant/40 rounded-full flex items-center justify-center font-metadata-mono text-xs">
        0
      </div>
      <p className="font-body-md text-sm">{label}</p>
      <p className="font-body-md text-xs opacity-60 mt-1">No activity recorded in this window</p>
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────
export default function TwilioCostsPage() {
  const [period, setPeriod] = useState<Period>('today');
  const [summary, setSummary] = useState<TwilioSummary | null>(null);
  const [calls, setCalls] = useState<TwilioCall[]>([]);
  const [daily, setDaily] = useState<TwilioDailyCost[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastRefresh, setLastRefresh] = useState<Date>(new Date());
  const containerRef = useRef<HTMLDivElement>(null);

  const loadData = useCallback(async () => {
    try {
      setError(null);
      const [sum, recent, dailyData] = await Promise.all([
        twilioApi.getSummary(period),
        twilioApi.getRecentCalls(20),
        twilioApi.getDailyCosts(30),
      ]);
      setSummary(sum);
      setCalls(recent);
      setDaily(dailyData);
      setLastRefresh(new Date());
    } catch (err: any) {
      const msg = err?.response?.data?.error || err?.message || 'Error loading Twilio data';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, [period]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  useEffect(() => {
    if (!autoRefresh) return;
    const id = setInterval(loadData, 30_000);
    return () => clearInterval(id);
  }, [autoRefresh, loadData]);

  const hasDailyData = daily.length > 0 && daily.some((d) => d.cost > 0 || d.callCount > 0);
  const hasCalls = calls.length > 0;

  return (
    <div ref={containerRef} className="max-w-[1400px] mx-auto px-gutter-mobile md:px-gutter-tablet lg:px-gutter-desktop py-12">
      {/* ─── Header ─────────────────────────────────────────── */}
      <header className="border-b border-outline-variant/40 dark:border-gray-800 pb-8 mb-10">
        <div className="flex items-start justify-between flex-wrap gap-6">
          <div>
            <p className="font-metadata-mono text-xs uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500 mb-3">
              Telephony · Twilio · Operational
            </p>
            <h1 className="text-headline-lg md:text-display-hero text-on-surface dark:text-white tracking-tight">
              Call cost ledger
            </h1>
            <p className="font-body-md text-on-surface-variant dark:text-gray-400 mt-3 max-w-xl">
              Real-time spend tracking across inbound and outbound voice. Last refreshed{' '}
              <span className="font-metadata-mono text-on-surface dark:text-gray-300">
                {lastRefresh.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
              </span>
              .
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => setAutoRefresh(!autoRefresh)}
              className="font-metadata-mono text-xs uppercase tracking-[0.1em] px-4 py-2 border border-outline-variant/60 dark:border-gray-700 hover:border-on-surface dark:hover:border-gray-400 transition-colors text-on-surface dark:text-gray-200"
            >
              {autoRefresh ? 'Pause stream' : 'Resume stream'}
            </button>
            <button
              onClick={loadData}
              className="font-metadata-mono text-xs uppercase tracking-[0.1em] px-4 py-2 bg-on-surface text-pure-surface dark:bg-white dark:text-gray-900 hover:bg-secondary dark:hover:bg-gray-200 transition-colors"
            >
              Refresh
            </button>
          </div>
        </div>

        {/* Period selector — underline style */}
        <nav className="mt-8 flex items-center gap-8 border-b border-outline-variant/30">
          {(['today', 'week', 'month'] as const).map((p) => {
            const labels: Record<typeof p, string> = {
              today: 'Today',
              week: 'This week',
              month: 'This month',
            };
            const isActive = period === p;
            return (
              <button
                key={p}
                onClick={() => setPeriod(p)}
                className={`relative font-metadata-mono text-xs uppercase tracking-[0.12em] pb-3 -mb-px transition-colors ${
                  isActive
                    ? 'text-on-surface dark:text-white border-b-2 border-on-surface dark:border-white'
                    : 'text-on-surface-variant dark:text-gray-500 hover:text-on-surface dark:hover:text-gray-300'
                }`}
              >
                {labels[p]}
              </button>
            );
          })}
          <div className="ml-auto font-metadata-mono text-xs text-on-surface-variant dark:text-gray-500 pb-3">
            {autoRefresh && (
              <span className="inline-flex items-center gap-1.5">
                <span className="relative flex h-1.5 w-1.5">
                  <span className="absolute inline-flex h-full w-full rounded-full bg-emerald-signal opacity-60 animate-ping" />
                  <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-signal" />
                </span>
                live · 30s
              </span>
            )}
          </div>
        </nav>
      </header>

      {error && (
        <div className="mb-8 px-5 py-4 border border-error/40 bg-error-container/30 text-error font-metadata-mono text-sm">
          {error}
        </div>
      )}

      {/* ─── KPIs ───────────────────────────────────────────── */}
      <section className="grid grid-cols-2 md:grid-cols-4 divide-x divide-outline-variant/40 border-y border-outline-variant/40 dark:divide-gray-800 dark:border-gray-800 mb-12">
        <MetricCell
          label="Total spend"
          value={summary ? fmtCost(summary.totalCost) : null}
          sub={summary?.currency || 'USD'}
          loading={loading}
        />
        <MetricCell
          label="Calls"
          value={summary ? summary.totalCalls.toString() : null}
          sub={summary ? `${summary.inboundCalls} in · ${summary.outboundCalls} out` : '—'}
          loading={loading}
        />
        <MetricCell
          label="Minutes"
          value={summary ? summary.totalMinutes.toString() : null}
          sub={summary && summary.totalCalls > 0
            ? `${Math.round(summary.totalMinutes / summary.totalCalls)} avg / call`
            : '—'}
          loading={loading}
        />
        <MetricCell
          label="Cost / minute"
          value={summary && summary.totalMinutes > 0 ? fmtCost(summary.totalCost / summary.totalMinutes) : null}
          sub="blended average"
          loading={loading}
        />
      </section>

      {/* ─── Charts ─────────────────────────────────────────── */}
      <section className="grid grid-cols-1 lg:grid-cols-5 gap-px bg-outline-variant/40 border border-outline-variant/40 dark:bg-gray-800 dark:border-gray-800 mb-12">
        <div className="bg-pure-surface dark:bg-gray-900 p-8 lg:col-span-3">
          <div className="flex items-baseline justify-between mb-6">
            <h2 className="font-metadata-mono text-xs uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500">
              Daily spend · last 30 days
            </h2>
            {hasDailyData && (
              <span className="font-metadata-mono text-xs text-on-surface-variant dark:text-gray-500">
                {fmtCost(daily.reduce((a, b) => a + b.cost, 0))} total
              </span>
            )}
          </div>
          {hasDailyData ? (
            <ResponsiveContainer width="100%" height={280}>
              <AreaChart data={daily} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id="twilioCostGradient" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#1c1b1c" stopOpacity={0.18} />
                    <stop offset="100%" stopColor="#1c1b1c" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid stroke="#EAEAEA" strokeDasharray="2 4" vertical={false} />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 10, fill: '#787774', fontFamily: 'JetBrains Mono, monospace' }}
                  tickLine={false}
                  axisLine={false}
                  tickFormatter={(d) => fmtDate(d)}
                  interval="preserveStartEnd"
                />
                <YAxis
                  tick={{ fontSize: 10, fill: '#787774', fontFamily: 'JetBrains Mono, monospace' }}
                  tickLine={false}
                  axisLine={false}
                  tickFormatter={(v) => '$' + v.toFixed(2)}
                  width={50}
                />
                <Tooltip
                  contentStyle={{
                    background: '#FFFFFF',
                    border: '1px solid #EAEAEA',
                    borderRadius: 0,
                    fontSize: 12,
                    fontFamily: 'JetBrains Mono, monospace',
                  }}
                  formatter={(v: number) => [fmtCost(v), 'Cost']}
                  labelFormatter={(d) => fmtDate(String(d))}
                />
                <Area
                  type="monotone"
                  dataKey="cost"
                  stroke="#1c1b1c"
                  strokeWidth={1.5}
                  fill="url(#twilioCostGradient)"
                />
              </AreaChart>
            </ResponsiveContainer>
          ) : (
            <ChartEmpty label="No spend recorded in the last 30 days" />
          )}
        </div>

        <div className="bg-pure-surface dark:bg-gray-900 p-8 lg:col-span-2">
          <div className="flex items-baseline justify-between mb-6">
            <h2 className="font-metadata-mono text-xs uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500">
              Volume · last 30 days
            </h2>
            {hasDailyData && (
              <span className="font-metadata-mono text-xs text-on-surface-variant dark:text-gray-500">
                {daily.reduce((a, b) => a + b.callCount, 0)} calls
              </span>
            )}
          </div>
          {hasDailyData ? (
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={daily} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid stroke="#EAEAEA" strokeDasharray="2 4" vertical={false} />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 10, fill: '#787774', fontFamily: 'JetBrains Mono, monospace' }}
                  tickLine={false}
                  axisLine={false}
                  tickFormatter={(d) => fmtDate(d)}
                  interval="preserveStartEnd"
                />
                <YAxis
                  tick={{ fontSize: 10, fill: '#787774', fontFamily: 'JetBrains Mono, monospace' }}
                  tickLine={false}
                  axisLine={false}
                  allowDecimals={false}
                  width={32}
                />
                <Tooltip
                  contentStyle={{
                    background: '#FFFFFF',
                    border: '1px solid #EAEAEA',
                    borderRadius: 0,
                    fontSize: 12,
                    fontFamily: 'JetBrains Mono, monospace',
                  }}
                  formatter={(v: number) => [v, 'Calls']}
                  labelFormatter={(d) => fmtDate(String(d))}
                />
                <Bar dataKey="callCount" fill="#1c1b1c" radius={0} />
              </BarChart>
            </ResponsiveContainer>
          ) : (
            <ChartEmpty label="No call activity in the last 30 days" />
          )}
        </div>
      </section>

      {/* ─── Recent calls table ────────────────────────────── */}
      <section>
        <div className="flex items-baseline justify-between mb-6">
          <h2 className="font-metadata-mono text-xs uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500">
            Recent calls
          </h2>
          <span className="font-metadata-mono text-xs text-on-surface-variant dark:text-gray-500">
            Last 20 · {hasCalls ? `${calls.length} entries` : '—'}
          </span>
        </div>

        <div className="border border-outline-variant/40 dark:border-gray-800">
          {/* Header row */}
          <div className="hidden md:grid grid-cols-[1.2fr_0.8fr_1.1fr_1.1fr_0.9fr_0.6fr_0.8fr] gap-4 px-6 py-3 border-b border-outline-variant/40 dark:border-gray-800 bg-surface-container-lowest/50 dark:bg-gray-900/50">
            {['Timestamp', 'Direction', 'From', 'To', 'Status', 'Duration', 'Cost'].map((h) => (
              <span
                key={h}
                className="font-metadata-mono text-[10px] uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500"
              >
                {h}
              </span>
            ))}
          </div>

          {/* Body */}
          {loading && !summary ? (
            <div className="divide-y divide-outline-variant/30 dark:divide-gray-800">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="grid grid-cols-[1.2fr_0.8fr_1.1fr_1.1fr_0.9fr_0.6fr_0.8fr] gap-4 px-6 py-4">
                  <SkeletonLine width="w-24" />
                  <SkeletonLine width="w-12" />
                  <SkeletonLine width="w-28" />
                  <SkeletonLine width="w-28" />
                  <SkeletonLine width="w-16" />
                  <SkeletonLine width="w-8" />
                  <SkeletonLine width="w-20" />
                </div>
              ))}
            </div>
          ) : !hasCalls ? (
            <div className="px-6 py-16 text-center">
              <p className="font-metadata-mono text-xs uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500">
                No recent calls
              </p>
              <p className="font-body-md text-sm text-on-surface-variant dark:text-gray-500 mt-2 max-w-md mx-auto">
                Once Twilio records call activity it will appear here in reverse chronological order.
              </p>
            </div>
          ) : (
            <div className="divide-y divide-outline-variant/30 dark:divide-gray-800">
              {calls.map((c) => (
                <div
                  key={c.sid}
                  className="grid grid-cols-2 md:grid-cols-[1.2fr_0.8fr_1.1fr_1.1fr_0.9fr_0.6fr_0.8fr] gap-4 px-6 py-4 items-center hover:bg-surface-container-lowest/40 dark:hover:bg-gray-900/40 transition-colors"
                >
                  <span className="font-metadata-mono text-xs text-on-surface dark:text-gray-300">
                    {fmtTime(c.startTime)}
                  </span>
                  <span className="font-metadata-mono text-[10px] uppercase tracking-[0.12em] text-on-surface-variant dark:text-gray-500">
                    {c.direction === 'inbound' ? '↙ inbound' : '↗ outbound'}
                  </span>
                  <span className="font-metadata-mono text-xs text-on-surface-variant dark:text-gray-400 truncate">
                    {c.from}
                  </span>
                  <span className="font-metadata-mono text-xs text-on-surface-variant dark:text-gray-400 truncate">
                    {c.to}
                  </span>
                  <span className="font-metadata-mono text-[10px] uppercase tracking-[0.12em] text-on-surface-variant dark:text-gray-500">
                    {c.status}
                  </span>
                  <span className="font-metadata-mono text-xs text-on-surface dark:text-gray-300 md:text-right">
                    {fmtDuration(c.durationSeconds)}
                  </span>
                  <span className="font-metadata-mono text-xs text-on-surface dark:text-white md:text-right font-medium">
                    {fmtCost(c.cost)}
                    <span className="text-on-surface-variant dark:text-gray-500 ml-1 text-[10px]">
                      {c.currency}
                    </span>
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      </section>

      {/* Footer / metadata */}
      <footer className="mt-16 pt-8 border-t border-outline-variant/30 dark:border-gray-800 flex items-center justify-between font-metadata-mono text-[10px] uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500">
        <span>Twilio · cost ledger v1</span>
        <span>Auto-refresh 30s · ALRrx only</span>
      </footer>
    </div>
  );
}

// ─── Metric cell (logic-grouped, no card overuse) ─────────────────────────
function MetricCell({
  label,
  value,
  sub,
  loading,
}: {
  label: string;
  value: string | null;
  sub: string;
  loading: boolean;
}) {
  return (
    <div className="px-6 py-7 md:px-8 md:py-8 bg-pure-surface dark:bg-gray-900">
      <p className="font-metadata-mono text-[10px] uppercase tracking-[0.14em] text-on-surface-variant dark:text-gray-500 mb-3">
        {label}
      </p>
      {loading && !value ? (
        <>
          <div className="h-9 w-32 bg-surface-container-high dark:bg-gray-800 animate-pulse mb-2" />
          <div className="h-3 w-20 bg-surface-container-high dark:bg-gray-800 animate-pulse" />
        </>
      ) : (
        <>
          <p className="font-metadata-mono text-2xl md:text-3xl tracking-tight text-on-surface dark:text-white tabular-nums">
            {value}
          </p>
          <p className="font-metadata-mono text-[10px] uppercase tracking-[0.12em] text-on-surface-variant dark:text-gray-500 mt-2">
            {sub}
          </p>
        </>
      )}
    </div>
  );
}
