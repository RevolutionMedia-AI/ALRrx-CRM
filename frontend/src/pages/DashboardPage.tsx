import { useEffect, useState, useCallback } from 'react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
} from 'recharts';
import { getDashboardSummary, getReport, getStaffing, getGoogleSheetsSales } from '../services/api';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, MetricCardDto, SalesSummary } from '../types';
import FunnelBlock from '../components/dashboard/FunnelBlock';
import DispositionLegend, { type DispositionItem } from '../components/dashboard/DispositionLegend';
import ProductivityCard from '../components/dashboard/ProductivityCard';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';

const PERIOD_API: Record<Period, string> = { Today: 'Today', Week: 'ThisWeek', Month: 'ThisMonth', Custom: 'Custom' };

const DISPOSITION_PALETTE = [
  '#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6',
  '#EC4899', '#14B8A6', '#F97316', '#6366F1', '#84CC16',
  '#06B6D4', '#A855F7', '#F43F5E', '#22C55E', '#EAB308',
];

function DarkTooltip({ active, payload, label }: { active?: boolean; payload?: Array<{ name: string; value: number; color: string }>; label?: string }) {
  if (!active || !payload) return null;
  return (
    <div className="bg-pure-surface dark:bg-gray-800 border border-whisper-border dark:border-gray-600 rounded-lg px-3 py-2 shadow-lg text-sm">
      {label && <p className="font-medium text-primary dark:text-gray-100 mb-1">{label}</p>}
      {payload.map((p, i) => (
        <p key={i} className="flex items-center gap-2 text-primary dark:text-gray-200">
          <span className="w-2.5 h-2.5 rounded-full shrink-0" style={{ backgroundColor: p.color }} />
          <span className="font-medium">{p.name}:</span>
          <span className="font-metadata-mono">{p.value}</span>
        </p>
      ))}
    </div>
  );
}

function findMetric(metrics: MetricCardDto[], label: string): MetricCardDto | undefined {
  return metrics.find((m) => m.label.toLowerCase().includes(label.toLowerCase()));
}

function parseMetricNumber(value: string | undefined): number {
  if (!value) return 0;
  const cleaned = value.replace(/[^\d.-]/g, '');
  const n = parseFloat(cleaned);
  return isNaN(n) ? 0 : n;
}

function formatDuration(seconds: number): string {
  if (!seconds && seconds !== 0) return '--:--';
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${s}s`;
}

function getInitials(name: string): string {
  if (!name) return '--';
  return name.split(' ').map((n) => n[0]).join('').substring(0, 2).toUpperCase();
}

type AgentStatus = 'READY' | 'INCALL' | 'QUEUE' | 'PAUSED' | 'OFFLINE';

const STATUS_COLORS: Record<AgentStatus, { bg: string; text: string; dot: string; label: string }> = {
  READY:   { bg: 'bg-emerald-signal/8', text: 'text-emerald-signal', dot: 'bg-emerald-signal', label: 'Available' },
  INCALL:  { bg: 'bg-electric-blue/8', text: 'text-electric-blue', dot: 'bg-electric-blue', label: 'On Call' },
  QUEUE:   { bg: 'bg-amber-warmth/8', text: 'text-amber-warmth', dot: 'bg-amber-warmth', label: 'In Queue' },
  PAUSED:  { bg: 'bg-deep-rose/8', text: 'text-deep-rose', dot: 'bg-deep-rose', label: 'Paused' },
  OFFLINE: { bg: 'bg-muted-slate/8', text: 'text-muted-slate', dot: 'bg-muted-slate', label: 'Offline' },
};

function getCallStatusColor(status: string) {
  switch (status) {
    case 'completed': return { bg: 'bg-emerald-signal/10', text: 'text-emerald-signal', dot: 'bg-emerald-signal' };
    case 'escalated': return { bg: 'bg-amber-warmth/10', text: 'text-amber-warmth', dot: 'bg-amber-warmth' };
    case 'abandoned': return { bg: 'bg-deep-rose/10', text: 'text-deep-rose', dot: 'bg-deep-rose' };
    default: return { bg: 'bg-emerald-signal/10', text: 'text-emerald-signal', dot: 'bg-emerald-signal' };
  }
}

export default function DashboardPage() {
  const [period, setPeriod] = useState<Period>('Today');
  const [customStart, setCustomStart] = useState(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });
  const [customEnd, setCustomEnd] = useState(() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });

  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [staffingReport, setStaffingReport] = useState<ReportDto | null>(null);
  const [callsReport, setCallsReport] = useState<ReportDto | null>(null);
  const [salesSummary, setSalesSummary] = useState<SalesSummary | null>(null);

  const [summaryLoading, setSummaryLoading] = useState(false);
  const [agentLoading, setAgentLoading] = useState(false);
  const [staffingLoading, setStaffingLoading] = useState(false);
  const [callsLoading, setCallsLoading] = useState(false);
  const [salesLoading, setSalesLoading] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string>('');

  const filter = useCallback((p: Period, cs: string, ce: string): TimeFilterDto => {
    if (p === 'Custom') return { period: PERIOD_API[p], customStart: `${cs}T00:00:00`, customEnd: `${ce}T23:59:59` };
    return { period: PERIOD_API[p] };
  }, []);

  const loadAll = useCallback(async (p: Period) => {
    setSummaryLoading(true);
    setAgentLoading(true);
    setStaffingLoading(true);
    setCallsLoading(true);
    setSalesLoading(true);
    setError(null);
    try {
      const filterResult = filter(p, customStart, customEnd);
      const s = await getDashboardSummary(filterResult);
      setSummary(s);
      const [a, st, c, sales] = await Promise.all([
        getReport('agent_performance', filterResult).catch(() => null),
        getStaffing().catch(() => null),
        getReport('all_calls', filterResult).catch(() => null),
        getGoogleSheetsSales(filterResult).catch(() => null),
      ]);
      setAgentReport(a);
      setStaffingReport(st);
      setCallsReport(c);
      setSalesSummary(sales);
    } catch {
      setError('Failed to load dashboard data');
    } finally {
      setSummaryLoading(false);
      setAgentLoading(false);
      setStaffingLoading(false);
      setCallsLoading(false);
      setSalesLoading(false);
    }
  }, [customStart, customEnd, filter]);

  useEffect(() => {
    loadAll(period);
    setLastUpdated(new Date().toLocaleTimeString());
  }, [period, customStart, customEnd, loadAll]);

  const handleManualRefresh = () => {
    loadAll(period);
    setLastUpdated(new Date().toLocaleTimeString());
  };

  const totalCalls = summary ? findMetric(summary.metrics, 'Total Calls') : undefined;
  const salesToday = summary ? findMetric(summary.metrics, 'Sales Today') : undefined;
  const aht = summary ? findMetric(summary.metrics, 'Handle Time') : undefined;
  const occupancy = summary ? findMetric(summary.metrics, 'Occupancy') : undefined;
  const leadsDialed = summary ? findMetric(summary.metrics, 'Leads Dialed') : undefined;
  const leadsContacted = summary ? findMetric(summary.metrics, 'Leads Contacted') : undefined;
  const contactRate = summary ? findMetric(summary.metrics, 'Contact Rate') : undefined;

  const dispositionsChart = summary?.charts?.[0];
  const chartData = dispositionsChart
    ? dispositionsChart.labels.map((label, i) => ({
        name: label,
        ...Object.fromEntries(dispositionsChart.series.map((s) => [s.name, s.data[i]])),
      }))
    : [];

  const dispositionItems: DispositionItem[] = chartData.map((d, i) => {
    const total = (d as unknown as Record<string, number>)[dispositionsChart!.series[0].name] ?? 0;
    return {
      name: d.name,
      value: total,
      color: DISPOSITION_PALETTE[i % DISPOSITION_PALETTE.length],
    };
  });

  const agents = agentReport?.rows ?? [];
  const calls = callsReport?.rows ?? [];
  const staffRows = staffingReport?.rows ?? [];

  const liveStatus = {
    available: staffRows.filter((r) => String(r.Status ?? '').toUpperCase() === 'READY').length,
    busy: staffRows.filter((r) => ['INCALL', 'QUEUE'].includes(String(r.Status ?? '').toUpperCase())).length,
    break: staffRows.filter((r) => String(r.Status ?? '').toUpperCase() === 'PAUSED').length,
  };
  const totalStaff = liveStatus.available + liveStatus.busy + liveStatus.break;

  const periodBtn = (p: Period) => (
    <button
      key={p}
      onClick={() => setPeriod(p)}
      className={`px-4 py-1.5 text-sm border-r border-whisper-border last:border-r-0 ${
        period === p
          ? 'bg-pure-surface text-primary font-medium'
          : 'text-secondary hover:bg-surface-container transition-colors'
      }`}
    >
      {p}
    </button>
  );

  return (
    <>
      <div className="flex flex-col md:flex-row justify-between items-start md:items-end gap-4 border-b border-whisper-border pb-4">
        <div>
          <h1 className="font-headline-lg text-headline-lg text-primary tracking-tight">
            Operations Overview — ALTRX
          </h1>
          <p className="text-secondary mt-1 flex items-center gap-2 text-sm">
            <span className="w-2 h-2 rounded-full bg-emerald-signal" />
            <span>
              Click refresh to update{lastUpdated && ` • Last updated: ${lastUpdated}`}
            </span>
          </p>
        </div>
        <div className="flex gap-2 flex-wrap items-end">
          <div className="bg-surface-container-low border border-whisper-border rounded flex text-sm overflow-hidden">
            {periodBtn('Today')}
            {periodBtn('Week')}
            {periodBtn('Month')}
            {periodBtn('Custom')}
          </div>
          {period === 'Custom' && (
            <div className="flex gap-2 items-center bg-surface-container-low border border-whisper-border rounded px-3 py-1">
              <input
                type="date"
                value={customStart}
                onChange={(e) => setCustomStart(e.target.value)}
                className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
              />
              <span className="text-muted-slate text-xs">to</span>
              <input
                type="date"
                value={customEnd}
                onChange={(e) => setCustomEnd(e.target.value)}
                className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
              />
            </div>
          )}
          <button
            onClick={handleManualRefresh}
            disabled={summaryLoading}
            className="flex items-center gap-2 px-3 py-1.5 border border-whisper-border rounded bg-pure-surface text-secondary hover:text-primary transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            title="Refresh dashboard data"
          >
            <span className={`material-symbols-outlined text-[20px] ${summaryLoading ? 'animate-spin' : ''}`}>sync</span>
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      {/* ========== HERO KPI ROW (6 cards) ========== */}
      <section>
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
          <KpiCard
            title="Leads Dialed"
            value={leadsDialed?.value ?? '0'}
            icon="phone_forwarded"
            valueColor="var(--card-value-blue)"
            loading={summaryLoading}
          />
          <KpiCard
            title="Total Calls"
            value={totalCalls?.value ?? '0'}
            change={totalCalls?.trend}
            icon="call"
            valueColor="var(--card-value-dark)"
            loading={summaryLoading}
          />
          <KpiCard
            title="Contacted"
            value={leadsContacted?.value ?? '0'}
            icon="contact_page"
            valueColor="var(--card-value-emerald)"
            loading={summaryLoading}
          />
          <KpiCard
            title="Contact Rate"
            value={contactRate?.value ?? '0%'}
            icon="percent"
            valueColor="#8B5CF6"
            loading={summaryLoading}
          />
          <KpiCard
            title="Sales"
            value={parseMetricNumber(salesToday?.value)}
            icon="confirmation_number"
            valueColor="var(--card-value-emerald)"
            loading={summaryLoading}
            isSales
          />
          <KpiCard
            title="Revenue"
            value={salesSummary?.totalSales ?? 0}
            icon="payments"
            valueColor="var(--card-value-emerald)"
            loading={salesLoading}
            isCurrency
            isSales
          />
        </div>
        <div className="flex justify-end mt-3">
          <ProductivityCard
            aht={aht?.value}
            occupancy={occupancy?.value}
            occupancyStatus={occupancy?.trend}
            loading={summaryLoading}
          />
        </div>
      </section>

      {/* ========== 2-COLUMN BODY ========== */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* LEFT COLUMN (8/12) */}
        <div className="lg:col-span-8 flex flex-col gap-6">
          {/* 1. Funnel */}
          <FunnelBlock
            dialed={parseMetricNumber(leadsDialed?.value) || null}
            contacted={parseMetricNumber(leadsContacted?.value) || null}
            sales={parseMetricNumber(salesToday?.value)}
            loading={summaryLoading}
          />

          {/* 2. Agent Performance Trend + Legend */}
          <section className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-xl shadow-card">
            <div className="p-6 border-b border-whisper-border flex justify-between items-center">
              <div>
                <h3 className="font-bold text-lg text-primary">Agent Performance Trend</h3>
                <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
                  Dispositions over the period
                </p>
              </div>
              <span className="material-symbols-outlined text-electric-blue text-2xl">monitoring</span>
            </div>
            <div className="p-8 flex flex-col gap-6">
              <div className="w-full h-80">
                {chartData.length > 0 ? (
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={chartData}>
                      <defs>
                        {dispositionsChart?.series.map((s, idx) => (
                          <linearGradient
                            key={s.name}
                            id={`gradient-${s.name}`}
                            x1="0"
                            y1="0"
                            x2="0"
                            y2="1"
                          >
                            <stop offset="5%" stopColor={DISPOSITION_PALETTE[idx % DISPOSITION_PALETTE.length]} stopOpacity={0.35} />
                            <stop offset="95%" stopColor={DISPOSITION_PALETTE[idx % DISPOSITION_PALETTE.length]} stopOpacity={0} />
                          </linearGradient>
                        ))}
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" />
                      <XAxis
                        dataKey="name"
                        tick={{ fontSize: 11, fill: '#94A3B8' }}
                        interval={0}
                        angle={-30}
                        textAnchor="end"
                        height={50}
                      />
                      <YAxis tick={{ fontSize: 11, fill: '#94A3B8' }} />
                      <Tooltip content={<DarkTooltip />} />
                      {dispositionsChart?.series.map((s, idx) => (
                        <Area
                          key={s.name}
                          type="monotone"
                          dataKey={s.name}
                          stroke={DISPOSITION_PALETTE[idx % DISPOSITION_PALETTE.length]}
                          fill={`url(#gradient-${s.name})`}
                          strokeWidth={2}
                        />
                      ))}
                    </AreaChart>
                  </ResponsiveContainer>
                ) : (
                  <div className="w-full h-full rounded-lg border border-dashed border-whisper-border bg-surface-container-low flex flex-col items-center justify-center text-muted-slate text-sm gap-1">
                    <span className="material-symbols-outlined text-3xl text-muted-slate/50">bar_chart</span>
                    <p>No disposition data yet</p>
                  </div>
                )}
              </div>
              {dispositionItems.length > 0 && (
                <div>
                  <p className="text-[10px] font-bold text-secondary uppercase tracking-wider mb-2">
                    Disposition codes — click for description
                  </p>
                  <DispositionLegend data={dispositionItems} />
                </div>
              )}
            </div>
          </section>

          {/* 3. Recent Calls Log */}
        </div>

        {/* RIGHT SIDEBAR (4/12) */}
        <div className="lg:col-span-4 flex flex-col gap-6">
          {/* Live Status */}
          <div className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused">
            <h3 className="font-bold text-lg text-primary mb-5 flex items-center justify-between">
              <span className="flex items-center gap-2">
                <span className="material-symbols-outlined text-emerald-signal">sensors</span>
                Live Status
              </span>
              <span className="text-[11px] text-muted-slate font-metadata-mono uppercase tracking-wider">
                {totalStaff} agents
              </span>
            </h3>
            {staffingLoading ? (
              <div className="space-y-5 animate-pulse">
                {[1, 2, 3].map((i) => (
                  <div key={i}>
                    <div className="h-4 w-full bg-surface-container rounded mb-1" />
                    <div className="h-2 w-full bg-surface-container rounded" />
                  </div>
                ))}
              </div>
            ) : (
              <div className="space-y-5">
                <StatusBar label="Available" count={liveStatus.available} total={totalStaff} color="bg-emerald-signal" />
                <StatusBar label="Busy / On Call" count={liveStatus.busy} total={totalStaff} color="bg-amber-warmth" />
                <StatusBar label="Break" count={liveStatus.break} total={totalStaff} color="bg-muted-slate" />
              </div>
            )}
          </div>

          {/* Top Performers */}
          <div className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused">
            <h3 className="font-bold text-lg text-primary mb-5 flex items-center gap-2">
              <span className="material-symbols-outlined text-amber-warmth">emoji_events</span>
              Top Performers
            </h3>
            {agentLoading ? (
              <div className="space-y-3 animate-pulse">
                {[1, 2, 3, 4].map((i) => (
                  <div key={i} className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      <div className="w-8 h-8 rounded-full bg-surface-container" />
                      <div className="space-y-1">
                        <div className="h-3 w-24 bg-surface-container rounded" />
                        <div className="h-2 w-16 bg-surface-container rounded" />
                      </div>
                    </div>
                    <div className="text-right space-y-1">
                      <div className="h-3 w-16 bg-surface-container rounded" />
                      <div className="h-2 w-12 bg-surface-container rounded" />
                    </div>
                  </div>
                ))}
              </div>
            ) : agents.length > 0 ? (
              <div className="space-y-3">
                {agents.slice(0, 6).map((agent, i) => (
                  <div
                    key={i}
                    className="flex items-center justify-between group hover:bg-surface-container-low -mx-2 px-2 py-1.5 rounded transition-colors"
                  >
                    <div className="flex items-center gap-3">
                      <div
                        className={`w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold border ${
                          i === 0
                            ? 'bg-amber-warmth/15 text-amber-warmth border-amber-warmth/30'
                            : 'bg-surface-container text-primary border-whisper-border'
                        }`}
                      >
                        {getInitials(String(agent.Name ?? agent.User ?? ''))}
                      </div>
                      <div>
                        <p className="text-sm font-medium text-primary">
                          {String(agent.Name ?? agent.User ?? '')}
                        </p>
                        <p className="text-[11px] text-secondary font-metadata-mono">
                          #{String(agent.User ?? '--')}
                        </p>
                      </div>
                    </div>
                    <div className="text-right">
                      <p className="text-sm font-bold text-primary font-metadata-mono">
                        {String(agent.Sales_Made ?? '0')}
                      </p>
                      <p className="text-[11px] text-emerald-signal font-metadata-mono">
                        {String(agent.Calls_Handled ?? '0')} calls
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-sm text-muted-slate">No agent data for this period</p>
            )}
          </div>

          {/* Agent Status */}
          <div className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused">
            <h3 className="font-bold text-lg text-primary mb-3 flex items-center gap-2">
              <span className="material-symbols-outlined text-electric-blue">groups</span>
              Agent Status
            </h3>
            <div className="flex flex-wrap gap-x-3 gap-y-1.5 mb-4 pb-4 border-b border-whisper-border">
              {(['READY', 'INCALL', 'QUEUE', 'PAUSED', 'OFFLINE'] as AgentStatus[]).map((s) => {
                const c = STATUS_COLORS[s];
                return (
                  <div key={s} className="flex items-center gap-1.5">
                    <span className={`w-2 h-2 rounded-full shrink-0 ${c.dot}`} />
                    <span className="text-[10px] text-secondary font-metadata-mono uppercase tracking-wider">{c.label}</span>
                  </div>
                );
              })}
            </div>
            {staffingLoading ? (
              <div className="grid grid-cols-3 gap-1.5">
                {[1, 2, 3, 4, 5, 6].map((i) => (
                  <div key={i} className="h-7 bg-surface-container rounded animate-pulse" />
                ))}
              </div>
            ) : staffRows.length > 0 ? (
              <div className="grid grid-cols-3 gap-1.5">
                {staffRows.slice(0, 15).map((agent, i) => {
                  const s = (String(agent.Status ?? '').toUpperCase() || 'OFFLINE') as AgentStatus;
                  const c = STATUS_COLORS[s] ?? STATUS_COLORS.OFFLINE;
                  return (
                    <div
                      key={i}
                      className="flex items-center gap-1.5 px-2 py-1 rounded bg-surface-container-low"
                      title={`${agent.Name ?? agent.User ?? '--'} — ${c.label}`}
                    >
                      <span className={`w-2 h-2 rounded-full shrink-0 ${c.dot}`} />
                      <span className="text-[11px] text-primary truncate">
                        {String(agent.Name ?? agent.User ?? '--')}
                      </span>
                    </div>
                  );
                })}
              </div>
            ) : (
              <p className="text-sm text-muted-slate">No agent data available</p>
            )}
          </div>
        </div>
      </div>

      {/* 3. Recent Calls Log (full width) */}
      <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
        <div className="p-6 border-b border-whisper-border flex justify-between items-center bg-canvas-white">
          <h3 className="font-bold text-lg text-primary flex items-center gap-2">
            <span className="material-symbols-outlined text-secondary text-xl">list_alt</span>
            Recent Calls Log
          </h3>
        </div>
        {callsLoading ? (
          <div className="p-6 space-y-3 animate-pulse">
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="h-8 bg-surface-container rounded" />
            ))}
          </div>
        ) : calls.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-surface-container-low border-b border-whisper-border text-xs uppercase tracking-wider text-secondary font-metadata-mono">
                  <th className="p-4 font-medium">Call ID</th>
                  <th className="p-4 font-medium">Agent</th>
                  <th className="p-4 font-medium">Duration</th>
                  <th className="p-4 font-medium">Disposition</th>
                  <th className="p-4 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {calls.slice(0, 10).map((call, i) => {
                  const status = String(call.status ?? 'completed').toLowerCase();
                  const sc = getCallStatusColor(status);
                  return (
                    <tr
                      key={i}
                      className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors"
                    >
                      <td className="p-4 font-metadata-mono text-primary">
                        #{String(call.call_id ?? call.id ?? i + 1)}
                      </td>
                      <td className="p-4">
                        <div className="flex items-center gap-2">
                          <div className="w-6 h-6 rounded-full bg-surface-container flex items-center justify-center text-[10px] font-bold">
                            {getInitials(String(call.Name ?? call.agent_name ?? call.user ?? ''))}
                          </div>
                          <span className="font-medium text-primary">
                            {String(call.Name ?? call.agent_name ?? call.user ?? '')}
                          </span>
                        </div>
                      </td>
                      <td className="p-4 font-metadata-mono text-secondary">
                        {formatDuration(Number(call.length_in_sec ?? 0))}
                      </td>
                      <td className="p-4">
                        <span className="px-2 py-1 bg-surface-container rounded text-xs text-primary font-medium border border-whisper-border font-metadata-mono">
                          {String(call.disposition ?? call.status ?? '')}
                        </span>
                      </td>
                      <td className="p-4">
                        <span
                          className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-medium ${sc.bg} ${sc.text}`}
                        >
                          <span className={`w-1.5 h-1.5 rounded-full ${sc.dot}`} />
                          {status.charAt(0).toUpperCase() + status.slice(1)}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="p-10 flex flex-col items-center justify-center text-center">
            <span className="material-symbols-outlined text-4xl text-muted-slate/40 mb-2">call_end</span>
            <p className="text-sm font-medium text-primary">No calls yet</p>
            <p className="text-xs text-muted-slate mt-1">Click refresh to load data</p>
          </div>
        )}
      </section>
    </>
  );
}

function KpiCard({
  title, value, change, icon, valueColor = 'var(--card-value-dark)', loading, isCurrency, isSales,
}: {
  title: string;
  value: string | number;
  change?: string;
  icon: string;
  valueColor?: string;
  loading?: boolean;
  isCurrency?: boolean;
  isSales?: boolean;
}) {
  const isPositive = change ? !change.startsWith('-') : true;
  const numericValue = typeof value === 'number' ? value : parseMetricNumber(value);
  const isEmpty = isSales && numericValue === 0;

  let displayValue: string;
  if (loading) {
    displayValue = '';
  } else if (isCurrency) {
    displayValue = isEmpty
      ? '--'
      : new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(numericValue);
  } else {
    displayValue = isEmpty ? 'No sales yet' : String(numericValue);
  }

  return (
    <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-5 shadow-card transition-transform hover:scale-[1.01] relative">
      <div className="flex justify-between items-start mb-4">
        <p className="text-card-label text-[12px] font-medium">{title}</p>
        <div className="p-1.5 bg-card-icon-bg dark:bg-gray-800 rounded-md">
          <span className="material-symbols-outlined text-[16px] text-card-label">{icon}</span>
        </div>
      </div>
      {loading ? (
        <div className="h-7 w-20 bg-surface-container rounded animate-pulse" />
      ) : (
        <div className="flex items-baseline gap-1.5">
          <h2
            className={`text-[1.6rem] font-bold leading-none tracking-tight ${isEmpty ? 'text-muted-slate font-medium' : ''}`}
            style={isEmpty ? undefined : { color: valueColor }}
          >
            {displayValue}
          </h2>
        </div>
      )}
      {change && !loading && (
        <div className="flex items-center gap-1 mt-2">
          <span
            className={`flex items-center font-medium font-metadata-mono text-xs ${
              isPositive ? 'text-emerald-signal' : 'text-deep-rose'
            }`}
          >
            <span className="material-symbols-outlined text-base">
              {isPositive ? 'trending_up' : 'trending_down'}
            </span>
            {change}
          </span>
        </div>
      )}
    </div>
  );
}

function StatusBar({ label, count, total, color }: { label: string; count: number; total: number; color: string }) {
  const pct = total > 0 ? ((count / total) * 100).toFixed(0) : '0';
  return (
    <div>
      <div className="flex justify-between text-sm mb-1">
        <span className="flex items-center gap-2 text-primary font-medium">
          <span className={`w-2 h-2 rounded-full ${color}`} />
          {label}
        </span>
        <span className="font-metadata-mono text-secondary">
          {count} <span className="text-muted-slate">({pct}%)</span>
        </span>
      </div>
      <div className="w-full bg-surface-container h-2 rounded-full overflow-hidden">
        <div className={`${color} h-full rounded-full transition-all`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}
