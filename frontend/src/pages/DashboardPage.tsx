import { useEffect, useState, useCallback, useRef } from 'react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, BarChart, Bar,
} from 'recharts';
import { getDashboardSummary, getReport, getStaffing, exportDashboardPdf, exportDashboardExcel } from '../services/api';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, MetricCardDto } from '../types';
import { useAuth } from '../context/AuthContext';
import GoogleSheetsSalesTable, { GoogleSheetsKpiCards } from '../components/GoogleSheetsSalesTable';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';

const PERIOD_API: Record<Period, string> = { Today: 'Today', Week: 'ThisWeek', Month: 'ThisMonth', Custom: 'Custom' };
const PERIOD_LABEL: Record<Period, string> = { Today: 'Sales Today', Week: 'Sales This Week', Month: 'Sales This Month', Custom: 'Sales (Custom)' };

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

type AgentStatus = 'READY' | 'INCALL' | 'QUEUE' | 'PAUSED' | 'OFFLINE';

const STATUS_COLORS: Record<AgentStatus, { bg: string; text: string; dot: string; label: string }> = {
  READY:   { bg: 'bg-emerald-signal/8', text: 'text-emerald-signal', dot: 'bg-emerald-signal', label: 'Available' },
  INCALL:  { bg: 'bg-electric-blue/8', text: 'text-electric-blue', dot: 'bg-electric-blue', label: 'On Call' },
  QUEUE:   { bg: 'bg-amber-warmth/8', text: 'text-amber-warmth', dot: 'bg-amber-warmth', label: 'In Queue' },
  PAUSED:  { bg: 'bg-deep-rose/8', text: 'text-deep-rose', dot: 'bg-deep-rose', label: 'Paused' },
  OFFLINE: { bg: 'bg-muted-slate/8', text: 'text-muted-slate', dot: 'bg-muted-slate', label: 'Offline' },
};

function getStatusColor(status: string) {
  switch (status) {
    case 'completed': return { bg: 'bg-emerald-signal/10', text: 'text-emerald-signal', dot: 'bg-emerald-signal' };
    case 'escalated': return { bg: 'bg-amber-warmth/10', text: 'text-amber-warmth', dot: 'bg-amber-warmth' };
    case 'abandoned': return { bg: 'bg-deep-rose/10', text: 'text-deep-rose', dot: 'bg-deep-rose' };
    default: return { bg: 'bg-emerald-signal/10', text: 'text-emerald-signal', dot: 'bg-emerald-signal' };
  }
}

export default function DashboardPage() {
  const { canEdit } = useAuth();
  const [period, setPeriod] = useState<Period>('Today');
  const [customStart, setCustomStart] = useState(() => {
    const d = new Date();
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  });
  const [customEnd, setCustomEnd] = useState(() => {
    const d = new Date();
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  });

  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [staffingReport, setStaffingReport] = useState<ReportDto | null>(null);
  const [callsReport, setCallsReport] = useState<ReportDto | null>(null);
  const [dispositionsReport, setDispositionsReport] = useState<ReportDto | null>(null);
  const [contactReport, setContactReport] = useState<ReportDto | null>(null);

  const [summaryLoading, setSummaryLoading] = useState(false);
  const [agentLoading, setAgentLoading] = useState(false);
  const [staffingLoading, setStaffingLoading] = useState(false);
  const [callsLoading, setCallsLoading] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [exportingPdf, setExportingPdf] = useState(false);
  const [exportingExcel, setExportingExcel] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const filter = (p: Period, cs: string, ce: string): TimeFilterDto => {
    if (p === 'Custom') return { period: PERIOD_API[p], customStart: `${cs}T00:00:00`, customEnd: `${ce}T23:59:59` };
    return { period: PERIOD_API[p] };
  };

  const loadAll = useCallback(async (p: Period) => {
    setSummaryLoading(true);
    setAgentLoading(true);
    setStaffingLoading(true);
    setCallsLoading(true);
    setError(null);
    try {
      const filterResult = filter(p, customStart, customEnd);
      const s = await getDashboardSummary(filterResult);
      setSummary(s);
      const [a, st, c, d, ct] = await Promise.all([
        getReport('agent_performance', filterResult).catch(() => null),
        getStaffing().catch(() => null),
        getReport('all_calls', filterResult).catch(() => null),
        getReport('dispositions', filterResult).catch(() => null),
        getReport('contact_vs_nocontact', filterResult).catch(() => null),
      ]);
      setAgentReport(a);
      setStaffingReport(st);
      setCallsReport(c);
      setDispositionsReport(d);
      setContactReport(ct);
    } catch {
      setError('Failed to load dashboard data');
    } finally {
      setSummaryLoading(false);
      setAgentLoading(false);
      setStaffingLoading(false);
      setCallsLoading(false);
    }
  }, [customStart, customEnd]);

  useEffect(() => {
    loadAll(period);

    if (period !== 'Custom') {
      intervalRef.current = setInterval(() => loadAll(period), 30000);
      return () => { if (intervalRef.current) clearInterval(intervalRef.current); };
    }
  }, [period, customStart, customEnd]);

  const totalCalls = summary ? findMetric(summary.metrics, 'Total Calls') : undefined;
  const salesToday = summary ? findMetric(summary.metrics, 'Sales Today') : undefined;
  const aht = summary ? findMetric(summary.metrics, 'Handle Time') : undefined;
  const occupancy = summary ? findMetric(summary.metrics, 'Occupancy') : undefined;
  const leadsDialed = summary ? findMetric(summary.metrics, 'Leads Dialed') : undefined;
  const leadsContacted = summary ? findMetric(summary.metrics, 'Leads Contacted') : undefined;
  const contactRate = summary ? findMetric(summary.metrics, 'Contact Rate') : undefined;

  const chartData = summary?.charts?.[0]
    ? summary.charts[0].labels.map((label, i) => ({
        name: label,
        ...Object.fromEntries(summary.charts[0].series.map((s) => [s.name, s.data[i]])),
      }))
    : [];

  const agents = agentReport?.rows ?? [];
  const calls = callsReport?.rows ?? [];
  const staffRows = staffingReport?.rows ?? [];

  const liveStatus = {
    available: staffRows.filter((r) => String(r.Status ?? '').toUpperCase() === 'READY').length,
    busy: staffRows.filter((r) => ['INCALL', 'QUEUE'].includes(String(r.Status ?? '').toUpperCase())).length,
    break: staffRows.filter((r) => String(r.Status ?? '').toUpperCase() === 'PAUSED').length,
  };
  const totalStaff = liveStatus.available + liveStatus.busy + liveStatus.break;

  const handlePeriodChange = (p: Period) => {
    setPeriod(p);
  };

  const handleExportPdf = async () => {
    setExportingPdf(true);
    try {
      const blob = await exportDashboardPdf(filter(period, customStart, customEnd));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `ALTRX_Dashboard_${period}_${new Date().toISOString().split('T')[0]}.pdf`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      setError('Failed to export PDF');
    } finally {
      setExportingPdf(false);
    }
  };

  const periodBtn = (p: Period) => (
    <button
      key={p}
      onClick={() => handlePeriodChange(p)}
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
          <h1 className="font-headline-lg text-headline-lg text-primary tracking-tight">Operations Overview — ALTRX</h1>
          <p className="text-secondary mt-1 flex items-center gap-2 text-sm">
            <span className="w-2 h-2 rounded-full bg-emerald-signal animate-pulse" />
            <span>Auto-refreshing every 30s {summary ? `• ${new Date(summary.lastUpdated).toLocaleTimeString()}` : ''}</span>
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
                onChange={(e) => {
                  console.log('onChange setCustomStart:', e.target.value);
                  setCustomStart(e.target.value);
                }}
                className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
              />
              <span className="text-muted-slate text-xs">to</span>
              <input
                type="date"
                value={customEnd}
                onChange={(e) => {
                  console.log('onChange setCustomEnd:', e.target.value);
                  setCustomEnd(e.target.value);
                }}
                className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
              />
            </div>
          )}
          <button
            onClick={() => loadAll(period)}
            className="p-1.5 border border-whisper-border rounded bg-pure-surface text-secondary hover:text-primary transition-colors shadow-sm"
            title="Refresh"
          >
            <span className="material-symbols-outlined text-[20px]">sync</span>
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm">
          {error}
        </div>
      )}

      <section className="grid grid-cols-3 gap-5">
        {[
          { label: 'Leads Dialed', value: leadsDialed?.value ?? '--', valueColor: 'var(--card-value-dark)' },
          { label: 'Leads Contacted', value: leadsContacted?.value ?? '--', valueColor: 'var(--card-value-emerald)' },
          { label: 'Contact Rate', value: contactRate?.value ?? '--%', valueColor: 'var(--card-value-emerald)' },
        ].map((l) => (
          <div key={l.label} className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-7 shadow-card">
            <p className="text-card-label text-[13px] font-medium">{l.label}</p>
            <p className="text-[2rem] font-bold mt-1 leading-none" style={{ color: l.valueColor }}>
              {summaryLoading ? '--' : l.value}
            </p>
          </div>
        ))}
      </section>

      <section className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
        <KpiCard
          title="Total Calls"
          value={totalCalls?.value ?? '--'}
          change={totalCalls?.trend}
          icon="call"
          valueColor="var(--card-value-dark)"
          loading={summaryLoading}
        />
        <KpiCard
          title="Avg Handle Time"
          value={aht?.value ?? '--:--'}
          change={aht?.trend}
          icon="timer"
          valueColor="var(--card-value-blue)"
          loading={summaryLoading}
        />
        <KpiCard
          title={PERIOD_LABEL[period]}
          value={salesToday?.value ?? '--'}
          change={salesToday?.trend}
          icon="monetization_on"
          valueColor="var(--card-value-emerald)"
          loading={summaryLoading}
        />
        <KpiCard
          title="Occupancy"
          value={occupancy?.value ? `${occupancy.value}` : '--%'}
          status={occupancy?.trend}
          icon="pie_chart"
          valueColor="var(--card-value-blue)"
          loading={summaryLoading}
        />
        <GoogleSheetsKpiCards filter={filter(period, customStart, customEnd)} />
      </section>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden flex flex-col">
          <div className="p-6 border-b border-whisper-border flex justify-between items-center">
            <h3 className="font-bold text-lg text-primary">Agent Performance Trend</h3>
          </div>
          <div className="p-6 flex-grow flex flex-col">
            <div className="w-full h-64 mb-6">
              {chartData.length > 0 ? (
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={chartData}>
                    <defs>
                      {summary?.charts[0].series.map((s) => (
                        <linearGradient key={s.name} id={`gradient-${s.name}`} x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor={s.color ?? '#3B82F6'} stopOpacity={0.3} />
                          <stop offset="95%" stopColor={s.color ?? '#3B82F6'} stopOpacity={0} />
                        </linearGradient>
                      ))}
                    </defs>
                    <CartesianGrid strokeDasharray="3 3" stroke="#e5e2e1" />
                    <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94A3B8' }} />
                    <YAxis tick={{ fontSize: 11, fill: '#94A3B8' }} />
                    <Tooltip content={<DarkTooltip />} />
                    {summary?.charts[0].series.map((s) => (
                      <Area
                        key={s.name}
                        type="monotone"
                        dataKey={s.name}
                        stroke={s.color ?? '#3B82F6'}
                        fill={`url(#gradient-${s.name})`}
                        strokeWidth={2}
                      />
                    ))}
                  </AreaChart>
                </ResponsiveContainer>
              ) : (
                <div className="w-full h-full rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
                  Chart will render here
                </div>
              )}
            </div>
            <div>
              <h4 className="text-sm font-medium text-secondary mb-3 uppercase tracking-wider text-[11px]">
                Top Performers ({period})
              </h4>
              {agentLoading ? (
                <div className="space-y-3">
                  {[1, 2, 3].map((i) => (
                    <div key={i} className="flex items-center justify-between animate-pulse">
                      <div className="flex items-center gap-3"><div className="w-8 h-8 rounded-full bg-surface-container" /><div className="space-y-1"><div className="h-3 w-24 bg-surface-container rounded" /><div className="h-2 w-16 bg-surface-container rounded" /></div></div>
                      <div className="text-right space-y-1"><div className="h-3 w-16 bg-surface-container rounded" /><div className="h-2 w-12 bg-surface-container rounded" /></div>
                    </div>
                  ))}
                </div>
              ) : agents.length > 0 ? (
                <div className="space-y-3">
                  {agents.slice(0, 8).map((agent, i) => (
                    <div key={i} className="flex items-center justify-between group">
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 rounded-full bg-surface-container flex items-center justify-center text-primary font-bold text-xs border border-whisper-border">
                          {getInitials(String(agent.Name ?? agent.User ?? ''))}
                        </div>
                        <div>
                          <p className="text-sm font-medium text-primary">{String(agent.Name ?? agent.User ?? '')}</p>
                          <p className="text-[11px] text-secondary font-metadata-mono">#{String(agent.User ?? '--')}</p>
                        </div>
                      </div>
                      <div className="text-right">
                        <p className="text-sm font-bold text-primary">{String(agent.Sales_Made ?? '0')} Sales</p>
                        <p className="text-[11px] text-emerald-signal font-metadata-mono">{String(agent.Calls_Handled ?? '0')} calls</p>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-muted-slate">Click sync to load data</p>
              )}
            </div>
          </div>
        </div>

        <div className="lg:col-span-1 flex flex-col gap-6">
          <div className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused flex-1">
            <h3 className="font-bold text-lg text-primary mb-6">Live Status</h3>
            {staffingLoading ? (
              <div className="space-y-5 animate-pulse">
                {[1, 2, 3].map((i) => (
                  <div key={i}><div className="h-4 w-full bg-surface-container rounded mb-1" /><div className="h-2 w-full bg-surface-container rounded" /></div>
                ))}
              </div>
            ) : (
              <div className="space-y-5">
                <StatusBar label="Available" count={liveStatus.available} total={totalStaff} color="bg-emerald-signal" />
                <StatusBar label="Busy / On Call" count={liveStatus.busy} total={totalStaff} color="bg-amber-warmth" />
                <StatusBar label="Break" count={liveStatus.break} total={totalStaff} color="bg-muted-slate" />
              </div>
            )}
            <div className="mt-6 pt-4 border-t border-whisper-border">
              <h4 className="text-sm font-medium text-secondary mb-2 uppercase tracking-wider text-[11px]">Agent Status</h4>
              {staffingLoading ? (
                <div className="grid grid-cols-3 gap-1.5">
                  {[1, 2, 3, 4, 5, 6].map((i) => (
                    <div key={i} className="h-6 bg-surface-container rounded animate-pulse" />
                  ))}
                </div>
              ) : staffRows.length > 0 ? (
                <div className="grid grid-cols-3 gap-1.5">
                  {staffRows.slice(0, 15).map((agent, i) => {
                    const s = (String(agent.Status ?? '').toUpperCase() || 'OFFLINE') as AgentStatus;
                    const c = STATUS_COLORS[s] ?? STATUS_COLORS.OFFLINE;
                    return (
                      <div key={i} className="flex items-center gap-1.5 px-2 py-1 rounded bg-surface-container-low">
                        <span className={`w-2 h-2 rounded-full shrink-0 ${c.dot}`} />
                        <span className="text-[11px] text-primary truncate">{String(agent.Name ?? agent.User ?? '--')}</span>
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
      </div>

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
                  const sc = getStatusColor(status);
                  return (
                    <tr key={i} className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors">
                      <td className="p-4 font-metadata-mono text-primary">#{String(call.call_id ?? call.id ?? i + 1)}</td>
                      <td className="p-4">
                        <div className="flex items-center gap-2">
                          <div className="w-6 h-6 rounded-full bg-surface-container flex items-center justify-center text-[10px] font-bold">
                            {getInitials(String(call.Name ?? call.agent_name ?? call.user ?? ''))}
                          </div>
                          <span className="font-medium text-primary">{String(call.Name ?? call.agent_name ?? call.user ?? '')}</span>
                        </div>
                      </td>
                      <td className="p-4 font-metadata-mono text-secondary">{formatDuration(Number(call.length_in_sec ?? 0))}</td>
                      <td className="p-4">
                        <span className="px-2 py-1 bg-surface-container rounded text-xs text-primary font-medium border border-whisper-border">
                          {String(call.disposition ?? call.status ?? '')}
                        </span>
                      </td>
                      <td className="p-4">
                        <span className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-medium ${sc.bg} ${sc.text}`}>
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
          <div className="p-6 text-sm text-muted-slate text-center">Click sync to load calls</div>
        )}
      </section>

      <GoogleSheetsSalesTable filter={filter(period, customStart, customEnd)} periodLabel={period} />

      <div className="flex justify-end gap-3">
        <button
          onClick={async () => {
            setExportingExcel(true);
            try {
              const blob = await exportDashboardExcel(filter(period, customStart, customEnd));
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url;
              a.download = `ALTRX_Dashboard_${period}_${new Date().toISOString().split('T')[0]}.xlsx`;
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
              URL.revokeObjectURL(url);
            } catch { setError('Failed to export Excel'); }
            finally { setExportingExcel(false); }
          }}
          disabled={exportingExcel}
          className="bg-emerald-signal text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <span className="material-symbols-outlined text-sm">table_chart</span>
          {exportingExcel ? 'Generating...' : 'Export Excel'}
        </button>
        <button
          onClick={handleExportPdf}
          disabled={exportingPdf}
          className="bg-deep-rose text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <span className="material-symbols-outlined text-sm">picture_as_pdf</span>
          {exportingPdf ? 'Generating...' : 'Export PDF'}
        </button>
      </div>
    </>
  );
}

function KpiCard({
  title, value, change, suffix, icon, valueColor = 'var(--card-value-dark)', loading, bar, status,
}: {
  title: string;
  value: string;
  change?: string;
  suffix?: string;
  icon: string;
  valueColor?: string;
  loading?: boolean;
  bar?: string;
  status?: string;
}) {
  const isPositive = change ? !change.startsWith('-') : true;
  return (
    <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-7 shadow-card transition-transform relative">
      <div className="flex justify-between items-start mb-5">
        <p className="text-card-label text-[13px] font-medium">{title}</p>
        <div className="p-2 bg-card-icon-bg dark:bg-gray-800 rounded-lg">
          <span className="material-symbols-outlined text-base text-card-label">{icon}</span>
        </div>
      </div>
      {loading ? (
        <div className="h-8 w-24 bg-surface-container rounded animate-pulse" />
      ) : (
        <div className="flex items-center gap-3">
          <h2 className="text-[2rem] font-bold leading-none tracking-tight" style={{ color: valueColor }}>{value}</h2>
          {suffix && <span className="text-secondary text-sm">{suffix}</span>}
        </div>
      )}
      {(change || status) && !loading && (
        <div className="absolute bottom-4 right-4 flex items-center gap-1.5">
          {change && (
            <span className={`flex items-center font-medium font-metadata-mono ${isPositive ? 'text-emerald-signal' : 'text-deep-rose'}`}>
              <span className="material-symbols-outlined text-2xl">{isPositive ? 'trending_up' : 'trending_down'}</span>
              <span className="text-sm">{change}</span>
            </span>
          )}
          {status && (
            <span className={`text-sm font-medium font-metadata-mono ${
              status === 'Optimal' || status === 'optimal' ? 'text-emerald-signal'
              : status === 'High' || status === 'high' ? 'text-deep-rose'
              : 'text-muted-slate'
            }`}>
              {status}
            </span>
          )}
        </div>
      )}
      {bar && (
        <div className="w-full bg-surface-container h-1.5 rounded-full mt-3 overflow-hidden">
          <div className="bg-amber-warmth h-full rounded-full transition-all" style={{ width: bar }} />
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
        <span className="font-metadata-mono">{count} ({pct}%)</span>
      </div>
      <div className="w-full bg-surface-container h-2 rounded-full overflow-hidden">
        <div className={`${color} h-full rounded-full transition-all`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}
