import { useEffect, useState, useMemo } from 'react';
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell,
} from 'recharts';
import { getDashboardSummary, getReport, exportDashboardPdf, exportDashboardExcel } from '../services/api';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, MetricCardDto } from '../types';
import PeriodComparisonModal from '../components/PeriodComparisonModal';
import VicidialSalesSection from '../components/vicidial-form/VicidialSalesSection';
import {
  PaymentSuccess01Icon,
  CallOutgoing01Icon,
  CallReceived02Icon,
  Call02Icon,
  AnalyticsUpIcon,
  AnalyticsDownIcon,
  MinusSignCircleIcon,
  UngroupItemsIcon,
  ArrowUp02Icon,
  ArrowDown02Icon,
} from 'hugeicons-react';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';
const PERIOD_API: Record<Period, string> = { Today: 'Today', Week: 'ThisWeek', Month: 'ThisMonth', Custom: 'Custom' };

const DISPOSITION_COLORS = [
  '#3B82F6', '#6366F1', '#8B5CF6', '#A78BFA', '#C084FC',
  '#06B6D4', '#0EA5E9', '#14B8A6', '#10B981', '#84CC16',
  '#F59E0B', '#F97316', '#EAB308', '#D97706',
  '#EF4444', '#EC4899', '#F43F5E', '#E11D48',
  '#6B7280', '#78716C',
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



function dayRange(date: Date): { start: string; end: string } {
  const start = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const end = new Date(start.getTime() + 24 * 60 * 60 * 1000 - 1);
  return { start: start.toISOString(), end: end.toISOString() };
}

function previousPeriod(p: Period): TimeFilterDto {
  const y = dayRange(new Date(Date.now() - 86400000));
  const w = { start: dayRange(new Date(Date.now() - 14 * 86400000)).start, end: dayRange(new Date(Date.now() - 7 * 86400000)).end };
  const m = { start: dayRange(new Date(Date.now() - 60 * 86400000)).start, end: dayRange(new Date(Date.now() - 30 * 86400000)).end };
  const c = { start: dayRange(new Date(Date.now() - 7 * 86400000)).start, end: y.end };
  switch (p) {
    case 'Today': return { period: 'Custom', customStart: y.start, customEnd: y.end };
    case 'Week': return { period: 'Custom', customStart: w.start, customEnd: w.end };
    case 'Month': return { period: 'Custom', customStart: m.start, customEnd: m.end };
    case 'Custom': return { period: 'Custom', customStart: c.start, customEnd: c.end };
  }
}

function findMetric(metrics: MetricCardDto[], label: string): MetricCardDto | undefined {
  return metrics.find((m) => m.label.toLowerCase().includes(label.toLowerCase()));
}

function pctChange(current: string, previous?: string): { pct: string; direction: 'up' | 'down' | 'same' } | null {
  if (!previous) return null;
  const c = parseFloat(current.replace(/[^0-9.-]/g, ''));
  const p = parseFloat(previous.replace(/[^0-9.-]/g, ''));
  if (isNaN(c) || isNaN(p) || p === 0) return null;
  const diff = ((c - p) / Math.abs(p)) * 100;
  return {
    pct: `${Math.abs(diff).toFixed(1)}%`,
    direction: diff > 0 ? 'up' : diff < 0 ? 'down' : 'same',
  };
}

function formatDuration(seconds: number): string {
  if (!seconds && seconds !== 0) return '--:--';
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${s}s`;
}

function animateIn(style: React.CSSProperties = {}): React.CSSProperties {
  return {
    opacity: 0,
    transform: 'translateY(12px)',
    animation: 'fadeSlideIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) forwards',
    ...style,
  };
}

type SortKey = 'user' | 'calls' | 'sales' | 'contacts' | 'conv' | 'aht';
type SortDir = 'asc' | 'desc';

export default function AnalyticsPage() {
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
  const [prevSummary, setPrevSummary] = useState<DashboardSummaryDto | null>(null);
  const [dispositions, setDispositions] = useState<ReportDto | null>(null);
  const [contactReport, setContactReport] = useState<ReportDto | null>(null);
  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sortKey, setSortKey] = useState<SortKey>('sales');
  const [sortDir, setSortDir] = useState<SortDir>('desc');
  const [exportingPdf, setExportingPdf] = useState(false);
  const [exportingExcel, setExportingExcel] = useState(false);
  const [showPeriodComparison, setShowPeriodComparison] = useState(false);
  const [vicidialRefreshKey, setVicidialRefreshKey] = useState(0);
  const [refreshing, setRefreshing] = useState(false);

  const filter = (p: Period): TimeFilterDto => {
    if (p === 'Custom') return { period: PERIOD_API[p], customStart: `${customStart}T00:00:00`, customEnd: `${customEnd}T23:59:59` };
    return { period: PERIOD_API[p] };
  };

  const fetchAnalytics = async (p: Period) => {
    setLoading(true);
    setError(null);
    try {
      const [s, prev, disp, contact, agents] = await Promise.all([
        getDashboardSummary(filter(p)),
        getDashboardSummary(previousPeriod(p)).catch(() => null),
        getReport('dispositions', filter(p)).catch(() => null),
        getReport('contact_vs_nocontact', filter(p)).catch(() => null),
        getReport('agent_performance', filter(p)).catch(() => null),
      ]);
      setSummary(s);
      setPrevSummary(prev);
      setDispositions(disp);
      setContactReport(contact);
      setAgentReport(agents);
    } catch {
      setError('Failed to load analytics data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchAnalytics(period); }, [period, customStart, customEnd]);

  const handleSort = (key: SortKey) => {
    if (sortKey === key) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortKey(key); setSortDir('desc'); }
  };

  const colMap: Record<SortKey, string> = {
    user: 'Name', calls: 'Calls_Handled', sales: 'Sales_Made',
    contacts: 'Contacts', conv: 'Conversion_Percentage', aht: 'AHT',
  };

  const sortedAgents = useMemo(() => {
    const rows = agentReport?.rows ?? [];
    const sorted = [...rows];
    sorted.sort((a, b) => {
      if (sortKey === 'user') {
        return sortDir === 'asc'
          ? String(a.Name ?? a.User ?? '').localeCompare(String(b.Name ?? b.User ?? ''))
          : String(b.Name ?? b.User ?? '').localeCompare(String(a.Name ?? a.User ?? ''));
      }
      const va = parseFloat(String(a[colMap[sortKey]] ?? 0));
      const vb = parseFloat(String(b[colMap[sortKey]] ?? 0));
      return sortDir === 'desc' ? vb - va : va - vb;
    });
    return sorted;
  }, [agentReport, sortKey, sortDir]);

  const sortArrow = (key: SortKey) => {
    if (sortKey !== key) return <UngroupItemsIcon size={14} className="text-muted-slate ml-1" />;
    return sortDir === 'asc'
      ? <ArrowUp02Icon size={14} className="ml-1" />
      : <ArrowDown02Icon size={14} className="ml-1" />;
  };

  const salesMetric = summary ? findMetric(summary.metrics, 'Sales Today') : undefined;
  const contactsMetric = summary ? findMetric(summary.metrics, 'Contacts') : undefined;
  const noContactsMetric = summary ? findMetric(summary.metrics, 'No Contacts') : undefined;
  const totalCallsMetric = summary ? findMetric(summary.metrics, 'Total Calls') : undefined;

  const prevSales = prevSummary ? findMetric(prevSummary.metrics, 'Sales Today') : undefined;
  const prevContacts = prevSummary ? findMetric(prevSummary.metrics, 'Contacts') : undefined;
  const prevNoContacts = prevSummary ? findMetric(prevSummary.metrics, 'No Contacts') : undefined;
  const prevTotalCalls = prevSummary ? findMetric(prevSummary.metrics, 'Total Calls') : undefined;

  const leadsDialed = summary ? findMetric(summary.metrics, 'Leads Dialed') : undefined;
  const leadsContacted = summary ? findMetric(summary.metrics, 'Leads Contacted') : undefined;
  const contactRateMetric = summary ? findMetric(summary.metrics, 'Contact Rate') : undefined;

  const kpiCards = [
    { title: 'Sales Today', value: salesMetric?.value ?? '--', change: pctChange(salesMetric?.value ?? '0', prevSales?.value), icon: PaymentSuccess01Icon, valueColor: 'var(--card-value-emerald)' },
    { title: 'Contacts', value: contactsMetric?.value ?? '--', change: pctChange(contactsMetric?.value ?? '0', prevContacts?.value), icon: CallOutgoing01Icon, valueColor: 'var(--card-value-emerald)' },
    { title: 'No Contacts', value: noContactsMetric?.value ?? '--', change: pctChange(noContactsMetric?.value ?? '0', prevNoContacts?.value), icon: CallReceived02Icon, valueColor: 'var(--card-value-red)' },
    { title: 'Total Calls', value: totalCallsMetric?.value ?? '--', change: pctChange(totalCallsMetric?.value ?? '0', prevTotalCalls?.value), icon: Call02Icon, valueColor: 'var(--card-value-dark)' },
  ];

  const dispoAreaData = useMemo(() => {
    if (!summary?.charts?.[0]?.series?.[0]) return [];
    return summary.charts[0].labels.map((label, i) => ({
      name: label,
      Total: summary.charts[0].series[0].data[i] ?? 0,
    }));
  }, [summary]);

  const contactAreaData = useMemo(() => {
    if (!contactReport?.rows?.[0]) return [];
    return [{
      name: period,
      Contact: Number(contactReport.rows[0].Contact ?? 0),
      'No Contact': Number(contactReport.rows[0].No_Contact ?? 0),
    }];
  }, [contactReport, period]);

  const sortableTh = (label: string, key: SortKey) => (
    <th
      className="p-3 font-medium cursor-pointer select-none hover:text-primary transition-colors"
      onClick={() => handleSort(key)}
    >
      <div className="flex items-center gap-1">
        {label}
        {sortArrow(key)}
      </div>
    </th>
  );

  return (
    <>
      <style>{`
        @keyframes fadeSlideIn {
          to { opacity: 1; transform: translateY(0); }
        }
      `}</style>

      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-end gap-4 border-b border-whisper-border pb-5">
        <div>
          <h1 className="font-headline-lg text-headline-lg font-bold text-primary tracking-tight">Analytics — ALTRX</h1>
          <p className="text-secondary text-sm mt-1 max-w-[65ch]">Deep dive into historical trends and agent performance</p>
        </div>
        <div className="flex gap-2 flex-wrap items-end">
          <div className="bg-surface-container-low border border-whisper-border rounded flex text-sm overflow-hidden">
            {(['Today', 'Week', 'Month', 'Custom'] as Period[]).map((p) => (
              <button
                key={p}
                onClick={() => setPeriod(p)}
                className={`px-4 py-1.5 border-r border-whisper-border last:border-r-0 transition-colors ${
                  period === p
                    ? 'bg-pure-surface text-primary font-medium'
                    : 'text-secondary hover:bg-surface-container'
                }`}
              >
                {p}
              </button>
            ))}
          </div>
          <button
            onClick={async () => {
              setRefreshing(true);
              setVicidialRefreshKey((k) => k + 1);
              try {
                await fetchAnalytics(period);
              } finally {
                setRefreshing(false);
              }
            }}
            disabled={refreshing || loading}
            className="bg-surface-container-low border border-whisper-border text-primary px-3 py-1.5 rounded text-sm font-medium flex items-center gap-1.5 hover:bg-surface-container transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            title="Refresh all analytics data"
          >
            <span className={`material-symbols-outlined text-sm ${refreshing ? 'animate-spin' : ''}`}>sync</span>
            <span>{refreshing ? 'Refreshing...' : 'Refresh'}</span>
          </button>
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
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/8 border border-deep-rose/15 rounded-xl p-4 text-deep-rose text-sm" style={animateIn()}>
          {error}
        </div>
      )}

      <section className="grid grid-cols-3 gap-5">
        {[
          { label: 'Leads Dialed', value: leadsDialed?.value ?? '--', valueColor: 'var(--card-value-dark)' },
          { label: 'Leads Contacted', value: leadsContacted?.value ?? '--', valueColor: 'var(--card-value-emerald)' },
          { label: 'Contact Rate', value: contactRateMetric?.value ?? '--%', valueColor: 'var(--card-value-emerald)' },
        ].map((l, i) => (
          <div key={l.label} className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-8 shadow-card" style={animateIn({ animationDelay: `${i * 60}ms` })}>
            <p className="text-card-label text-[13px] font-medium">{l.label}</p>
            <p className="text-[2.2rem] font-bold mt-1 leading-none" style={{ color: l.valueColor }}>
              {loading ? '--' : l.value}
            </p>
          </div>
        ))}
      </section>

      <section className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-5">
        {kpiCards.map((card, i) => (
          <div
            key={card.title}
            className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg py-8 px-7 shadow-card"
            style={animateIn({ animationDelay: `${i * 80}ms` })}
          >
            <div className="flex justify-between items-start mb-5">
              <p className="text-card-label text-[13px] font-medium">{card.title}</p>
<div className={`p-4 bg-card-icon-bg dark:bg-gray-800 rounded-xl`}>
                  <card.icon size={20} className="text-card-label" />
              </div>
            </div>
            {loading ? (
              <div className="h-8 w-28 bg-surface-container rounded animate-pulse" />
            ) : (
              <div className="flex items-baseline gap-3">
                <h2 className="text-[2.2rem] font-bold leading-none tracking-tight" style={{ color: card.valueColor }}>{card.value}</h2>
                {card.change && (
                  <span className={`text-sm font-medium flex items-center font-metadata-mono ${
                    card.change.direction === 'up' ? 'text-emerald-signal'
                    : card.change.direction === 'down' ? 'text-deep-rose'
                    : 'text-muted-slate'
                  }`}>
                    {card.change.direction === 'up' ? <AnalyticsUpIcon size={15} />
                        : card.change.direction === 'down' ? <AnalyticsDownIcon size={15} />
                        : <MinusSignCircleIcon size={15} />}
                    {card.change.pct}
                  </span>
                )}
              </div>
            )}
          </div>
        ))}
      </section>

      <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
        <div className="lg:col-span-2 border border-whisper-border rounded-xl p-8" style={animateIn({ animationDelay: '160ms' })}>
          <h3 className="font-bold text-lg text-primary mb-6">Dispositions</h3>
          {loading ? (
            <div className="h-80 bg-surface-container rounded animate-pulse" />
          ) : dispoAreaData.length > 0 ? (
            <div className="flex flex-col gap-5">
<ResponsiveContainer width="100%" height={260}>
                 <BarChart data={dispoAreaData}>
                   <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" />
                   <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#787774' }} />
                   <YAxis tick={{ fontSize: 11, fill: '#787774' }} />
                   <Tooltip content={<DarkTooltip />} />
                   <Bar dataKey="Total" fill="#3B82F6" radius={[4, 4, 0, 0]} maxBarSize={48} />
                 </BarChart>
               </ResponsiveContainer>
              <div className="flex flex-wrap justify-center gap-x-5 gap-y-1.5 text-sm">
                {dispoAreaData.map((d, i) => (
                  <div key={d.name} className="flex items-center gap-2">
                    <span
                      className="w-2.5 h-2.5 rounded-full shrink-0"
                      style={{ backgroundColor: DISPOSITION_COLORS[i % DISPOSITION_COLORS.length] }}
                    />
                    <span className="text-primary font-medium">{d.name}</span>
                    <span className="text-secondary font-metadata-mono">{d.Total}</span>
                  </div>
                ))}
              </div>
            </div>
          ) : (
            <div className="h-64 rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
              No disposition data available
            </div>
          )}
        </div>

        <div className="lg:col-span-3 border border-whisper-border rounded-xl p-8" style={animateIn({ animationDelay: '240ms' })}>
          <h3 className="font-bold text-lg text-primary mb-6">Disposition Details</h3>
          {loading ? (
            <div className="space-y-2 animate-pulse">{[1, 2, 3, 4, 5].map((i) => <div key={i} className="h-8 bg-surface-container rounded" />)}</div>
          ) : dispositions && dispositions.rows.length > 0 ? (
            <div className="overflow-x-auto max-h-[320px] overflow-y-auto scrollbar-thin">
              <table className="w-full text-left text-sm border-collapse">
                <thead className="text-xs uppercase tracking-wider text-secondary font-metadata-mono bg-surface-container-low sticky top-0">
                  <tr>
                    <th className="p-3 font-medium">Disposition</th>
                    <th className="p-3 font-medium text-right">Total</th>
                    <th className="p-3 font-medium text-right">%</th>
                  </tr>
                </thead>
                <tbody>
                  {dispositions.rows.map((r, i) => (
                    <tr key={i} className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors">
                      <td className="p-3 text-primary font-medium">{String(r.Disposition ?? '')}</td>
                      <td className="p-3 text-right font-metadata-mono">{String(r.Total ?? '')}</td>
                      <td className="p-3 text-right font-metadata-mono text-secondary">
                        {typeof r.Percentage === 'number' ? `${r.Percentage.toFixed(1)}%` : String(r.Percentage ?? '')}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="h-32 rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
              No disposition details
            </div>
          )}
        </div>
      </div>

      <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-8 shadow-card" style={animateIn({ animationDelay: '320ms' })}>
        <h3 className="font-bold text-lg text-primary mb-6">Contact vs No Contact</h3>
        {loading ? (
          <div className="h-64 bg-surface-container rounded animate-pulse" />
        ) : contactAreaData.length > 0 ? (
          <div className="flex flex-col gap-4">
            <ResponsiveContainer width="100%" height={260}>
              <PieChart>
                <Pie
                  data={[
                    { name: 'Contact', value: contactAreaData[0].Contact, color: '#10b981' },
                    { name: 'No Contact', value: contactAreaData[0]['No Contact'], color: '#ef4444' },
                  ]}
                  cx="50%"
                  cy="50%"
                  innerRadius={60}
                  outerRadius={100}
                  paddingAngle={2}
                  dataKey="value"
                  isAnimationActive={false}
                >
                  <Cell fill="#10b981" />
                  <Cell fill="#ef4444" />
                </Pie>
                <Tooltip content={<DarkTooltip />} />
              </PieChart>
            </ResponsiveContainer>
            <div className="flex items-center justify-center gap-8">
              <div className="flex items-center gap-3">
                <span className="w-3 h-3 rounded-full shrink-0" style={{ backgroundColor: '#10b981' }} />
                <div>
                  <p className="text-xl font-bold" style={{ color: '#10b981' }}>{contactAreaData[0].Contact}</p>
                  <p className="text-xs text-card-label">Contact</p>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="w-3 h-3 rounded-full shrink-0" style={{ backgroundColor: '#ef4444' }} />
                <div>
                  <p className="text-xl font-bold" style={{ color: '#ef4444' }}>{contactAreaData[0]['No Contact']}</p>
                  <p className="text-xs text-card-label">No Contact</p>
                </div>
              </div>
              {contactAreaData[0].Contact + contactAreaData[0]['No Contact'] > 0 && (
                <div className="flex flex-col items-center px-4 py-2 rounded-lg bg-[#F8FAFC] dark:bg-gray-800">
                  <p className="text-xl font-bold" style={{ color: '#2563EB' }}>
                    {((contactAreaData[0].Contact / (contactAreaData[0].Contact + contactAreaData[0]['No Contact'])) * 100).toFixed(0)}%
                  </p>
                  <p className="text-[11px] text-[#64748B]">Contact Rate</p>
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="h-64 rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
            No contact data available
          </div>
        )}
      </div>

      <section className="border border-whisper-border rounded-xl overflow-hidden" style={animateIn({ animationDelay: '400ms' })}>
        <div className="p-6 border-b border-whisper-border">
          <h3 className="font-bold text-lg text-primary">Agent Performance</h3>
        </div>
        {loading ? (
          <div className="p-6 space-y-3 animate-pulse">
            {[1, 2, 3, 4, 5, 6].map((i) => <div key={i} className="h-10 bg-surface-container rounded" />)}
          </div>
        ) : sortedAgents.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse text-sm">
              <thead>
                <tr className="bg-surface-container-low border-b border-whisper-border text-xs uppercase tracking-wider text-secondary font-metadata-mono">
                  {sortableTh('Agent', 'user')}
                  {sortableTh('Calls Handled', 'calls')}
                  {sortableTh('Sales Made', 'sales')}
                  {sortableTh('Contacts', 'contacts')}
                  {sortableTh('Conversion %', 'conv')}
                  {sortableTh('AHT', 'aht')}
                </tr>
              </thead>
              <tbody>
                {sortedAgents.map((agent, i) => (
                  <tr key={i} className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors">
                    <td className="p-3 font-medium text-primary">{String(agent.Name ?? agent.User ?? '')}</td>
                    <td className="p-3 font-metadata-mono">{String(agent.Calls_Handled ?? '0')}</td>
                    <td className="p-3 font-metadata-mono text-emerald-signal font-medium">{String(agent.Sales_Made ?? '0')}</td>
                    <td className="p-3 font-metadata-mono">{String(agent.Contacts ?? '0')}</td>
                    <td className="p-3 font-metadata-mono font-medium">
                      {agent.Conversion_Percentage != null ? `${Number(agent.Conversion_Percentage).toFixed(1)}%` : '--'}
                    </td>
                    <td className="p-3 font-metadata-mono text-secondary">
                      {agent.AHT ? formatDuration(parseInt(String(agent.AHT).split(':').reduce((acc, t) => acc * 60 + parseInt(t), 0).toString())) : '--:--'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="p-12 text-sm text-muted-slate text-center">No agent data available for this period</div>
        )}
      </section>

      <VicidialSalesSection refreshKey={vicidialRefreshKey} />

      <div className="flex justify-end gap-3" style={animateIn({ animationDelay: '480ms' })}>
        <button
          onClick={() => setShowPeriodComparison(true)}
          className="bg-electric-blue text-white px-4 py-2 rounded-[6px] font-medium text-sm hover:scale-[0.98] transition-transform flex items-center gap-2"
        >
          <span className="material-symbols-outlined text-sm">compare_arrows</span>
          Period Comparison
        </button>
        <button
          onClick={async () => {
            setExportingExcel(true);
            try {
              const blob = await exportDashboardExcel(filter(period));
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url;
              a.download = `ALTRX_Analytics_${period}_${new Date().toISOString().split('T')[0]}.xlsx`;
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
              URL.revokeObjectURL(url);
            } catch { setError('Failed to generate Excel'); }
            finally { setExportingExcel(false); }
          }}
          disabled={exportingExcel}
          className="bg-emerald-signal text-white px-4 py-2 rounded-[6px] font-medium text-sm hover:scale-[0.98] transition-transform flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <span className="material-symbols-outlined text-sm">table_chart</span>
          {exportingExcel ? 'Generating...' : 'Export Excel'}
        </button>
        <button
          onClick={async () => {
            setExportingPdf(true);
            try {
              const blob = await exportDashboardPdf(filter(period));
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url;
              a.download = `ALTRX_Analytics_${period}_${new Date().toISOString().split('T')[0]}.pdf`;
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
              URL.revokeObjectURL(url);
            } catch {
              setError('Failed to generate PDF');
            } finally {
              setExportingPdf(false);
            }
          }}
          disabled={exportingPdf}
          className="bg-deep-rose text-white px-4 py-2 rounded-[6px] font-medium text-sm hover:scale-[0.98] transition-transform flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <span className="material-symbols-outlined text-sm">picture_as_pdf</span>
          {exportingPdf ? 'Generating...' : 'Export PDF'}
        </button>
      </div>

      <PeriodComparisonModal isOpen={showPeriodComparison} onClose={() => setShowPeriodComparison(false)} />
    </>
  );
}
