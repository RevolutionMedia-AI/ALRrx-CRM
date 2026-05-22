import { useEffect, useState, useMemo } from 'react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
} from 'recharts';
import { getDashboardSummary, getReport, exportDashboardPdf, exportDashboardExcel } from '../services/api';
import { exportAnalyticsCSV } from '../utils/csv';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, MetricCardDto } from '../types';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';
const PERIOD_API: Record<Period, string> = { Today: 'Today', Week: 'ThisWeek', Month: 'ThisMonth', Custom: 'Custom' };

const DISPOSITION_COLORS = [
  '#3B82F6', '#6366F1', '#8B5CF6', '#A78BFA', '#C084FC',
  '#06B6D4', '#0EA5E9', '#14B8A6', '#10B981', '#84CC16',
  '#F59E0B', '#F97316', '#EAB308', '#D97706',
  '#EF4444', '#EC4899', '#F43F5E', '#E11D48',
  '#6B7280', '#78716C',
];

const PASTEL_BG: Record<string, string> = {
  'text-emerald-signal': 'bg-emerald-signal/8',
  'text-electric-blue': 'bg-electric-blue/8',
  'text-deep-rose': 'bg-deep-rose/8',
  'text-amber-warmth': 'bg-amber-warmth/8',
};

function Icon({ name, className = '' }: { name: string; className?: string }) {
  const paths: Record<string, string> = {
    payments: 'M2 8a2 2 0 012-2h12a2 2 0 012 2v8a2 2 0 01-2 2H4a2 2 0 01-2-2zm2 0v8h12V8zm3 4a1.5 1.5 0 113 0 1.5 1.5 0 01-3 0z',
    'call-made': 'M9.143 4.545a.714.714 0 01.714.714v9.089l2.555-2.555a.714.714 0 011.01 1.01l-3.75 3.75a.714.714 0 01-1.01 0l-3.75-3.75a.714.714 0 111.01-1.01L8.43 14.35V5.259a.714.714 0 01.714-.714z',
    'call-received': 'M6.857 4.545a.714.714 0 01.714.714v9.089l2.555-2.555a.714.714 0 011.01 1.01l-3.75 3.75a.714.714 0 01-1.01 0l-3.75-3.75a.714.714 0 111.01-1.01L6.143 14.35V5.259a.714.714 0 01.714-.714z',
    call: 'M7.073 2.5c.313.09.59.27.797.518l2.317 2.788a1.25 1.25 0 01-.175 1.725l-1.112.928a.179.179 0 00-.05.216c.27.568.664 1.093 1.163 1.53l2.356 2.06a.179.179 0 00.221.024l1.132-.672a1.25 1.25 0 011.627.305l1.87 2.452c.213.28.309.64.269.995a1.477 1.477 0 01-.695 1.066l-.485.32c-.77.508-1.703.705-2.636.557-1.581-.25-3.333-1.206-5.255-2.867C5.517 12.09 4.071 9.99 3.356 7.79c-.347-1.07-.384-2.177.212-3.146l.38-.622c.25-.409.68-.66 1.148-.672.278-.009.55.064.78.208l.507.313c.296.183.539.445.69.762v-.001z',
    'trending-up': 'M1.47 11.97a.75.75 0 011.06 0L6 15.44l3.72-3.72a.75.75 0 011.06 0l2.47 2.47V11.5a.75.75 0 011.5 0v5a.75.75 0 01-.75.75h-5a.75.75 0 010-1.5h2.69L9.78 13.2l-3.72 3.72a.75.75 0 01-1.06 0L1.47 13.03a.75.75 0 010-1.06z',
    'trending-down': 'M1.47 4.03a.75.75 0 011.06 0L6 7.44l3.72-3.72a.75.75 0 011.06 0l2.47 2.47V4.5a.75.75 0 011.5 0v5a.75.75 0 01-.75.75h-5a.75.75 0 010-1.5h2.69L9.78 6.2l-3.72 3.72a.75.75 0 01-1.06 0L1.47 5.09a.75.75 0 010-1.06z',
    'minus-small': 'M3.75 8a.75.75 0 01.75-.75h6.5a.75.75 0 010 1.5h-6.5A.75.75 0 013.75 8z',
    'unfold-more': 'M8 1.5a.5.5 0 01.5.5v10.793l2.646-2.647a.5.5 0 11.708.708l-3.5 3.5a.5.5 0 01-.708 0l-3.5-3.5a.5.5 0 11.708-.708L7.5 12.793V2a.5.5 0 01.5-.5z',
    'chevron-up': 'M8 3.5a.5.5 0 01.5.5v8.793l2.646-2.647a.5.5 0 11.708.708l-3.5 3.5a.5.5 0 01-.708 0l-3.5-3.5a.5.5 0 11.708-.708L7.5 12.793V4a.5.5 0 01.5-.5z',
    'chevron-down': 'M8 12.5a.5.5 0 01-.5-.5V3.207L4.854 5.854a.5.5 0 11-.708-.708l3.5-3.5a.5.5 0 01.708 0l3.5 3.5a.5.5 0 11-.708.708L8.5 3.207V12a.5.5 0 01-.5.5z',
    download: 'M8 1.5a.5.5 0 01.5.5v7.793l2.146-2.147a.5.5 0 11.708.708l-3 3a.5.5 0 01-.708 0l-3-3a.5.5 0 11.708-.708L7.5 9.793V2a.5.5 0 01.5-.5zM2 13a.5.5 0 01.5.5v.5a.5.5 0 00.5.5h9a.5.5 0 00.5-.5v-.5a.5.5 0 011 0v.5a1.5 1.5 0 01-1.5 1.5H3A1.5 1.5 0 011.5 14v-.5A.5.5 0 012 13z',
    'chart-pie': 'M8.5 1.5a.5.5 0 00-.5.5v5.5a.5.5 0 00.5.5H14a.5.5 0 00.5-.5A6.5 6.5 0 008.5 1.5zm.5 5.5V2.522A5.5 5.5 0 0113.478 7H9zM7.5 3.038V8a.5.5 0 00.5.5h4.962A5.5 5.5 0 117.5 3.037z',
    'chart-bar': 'M1 13a1 1 0 011-1h1a1 1 0 011 1v1a1 1 0 01-1 1H2a1 1 0 01-1-1zm4-4a1 1 0 011-1h1a1 1 0 011 1v5a1 1 0 01-1 1H6a1 1 0 01-1-1zm4-4a1 1 0 011-1h1a1 1 0 011 1v9a1 1 0 01-1 1h-1a1 1 0 01-1-1z',
    table: 'M2 3a1 1 0 011-1h10a1 1 0 011 1v10a1 1 0 01-1 1H3a1 1 0 01-1-1zm1 0v2h10V3zm0 3v2h4V6zm0 3v2h4V9zm5-3v2h5V6zm0 3v2h5V9z',
  };
  const d = paths[name] || paths.table;
  return (
    <svg className={className} width="1em" height="1em" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <path d={d} />
    </svg>
  );
}

function previousPeriod(p: Period): TimeFilterDto {
  switch (p) {
    case 'Today': return { period: 'Custom', customStart: new Date(Date.now() - 86400000).toISOString(), customEnd: new Date(Date.now() - 86400000).toISOString() };
    case 'Week': return { period: 'Custom', customStart: new Date(Date.now() - 14 * 86400000).toISOString(), customEnd: new Date(Date.now() - 7 * 86400000).toISOString() };
    case 'Month': return { period: 'Custom', customStart: new Date(Date.now() - 60 * 86400000).toISOString(), customEnd: new Date(Date.now() - 30 * 86400000).toISOString() };
    case 'Custom': return { period: 'Custom', customStart: new Date(Date.now() - 7 * 86400000).toISOString(), customEnd: new Date(Date.now() - 86400000).toISOString() };
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
  const [customStart, setCustomStart] = useState(() => new Date().toISOString().split('T')[0]);
  const [customEnd, setCustomEnd] = useState(() => new Date().toISOString().split('T')[0]);
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
    if (sortKey !== key) return <Icon name="unfold-more" className="text-muted-slate ml-1" />;
    return (
      <Icon
        name={sortDir === 'asc' ? 'chevron-up' : 'chevron-down'}
        className="ml-1"
      />
    );
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
    { title: 'Sales Today', value: salesMetric?.value ?? '--', change: pctChange(salesMetric?.value ?? '0', prevSales?.value), icon: 'payments', color: 'text-emerald-signal' },
    { title: 'Contacts', value: contactsMetric?.value ?? '--', change: pctChange(contactsMetric?.value ?? '0', prevContacts?.value), icon: 'call-made', color: 'text-electric-blue' },
    { title: 'No Contacts', value: noContactsMetric?.value ?? '--', change: pctChange(noContactsMetric?.value ?? '0', prevNoContacts?.value), icon: 'call-received', color: 'text-deep-rose' },
    { title: 'Total Calls', value: totalCallsMetric?.value ?? '--', change: pctChange(totalCallsMetric?.value ?? '0', prevTotalCalls?.value), icon: 'call', color: 'text-amber-warmth' },
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
          { label: 'Leads Dialed', value: leadsDialed?.value ?? '--', bg: 'bg-electric-blue/5 dark:bg-electric-blue/10', border: 'border-electric-blue/15 dark:border-electric-blue/25', textColor: 'text-electric-blue dark:text-blue-300' },
          { label: 'Leads Contacted', value: leadsContacted?.value ?? '--', bg: 'bg-emerald-signal/5 dark:bg-emerald-signal/10', border: 'border-emerald-signal/15 dark:border-emerald-signal/25', textColor: 'text-emerald-signal dark:text-emerald-300' },
          { label: 'Contact Rate', value: contactRateMetric?.value ?? '--%', bg: 'bg-violet-500/5 dark:bg-violet-500/10', border: 'border-violet-500/15 dark:border-violet-500/25', textColor: 'text-violet-500 dark:text-violet-300' },
        ].map((l, i) => (
          <div key={l.label} className={`${l.bg} ${l.border} border rounded-xl p-8 transition-colors`} style={animateIn({ animationDelay: `${i * 60}ms` })}>
            <p className="text-secondary dark:text-gray-400 text-sm">{l.label}</p>
            <p className={`text-2xl font-bold mt-1 ${l.textColor}`}>
              {loading ? '--' : l.value}
            </p>
          </div>
        ))}
      </section>

      <section className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-5">
        {kpiCards.map((card, i) => (
          <div
            key={card.title}
            className="border border-whisper-border rounded-xl p-8"
            style={animateIn({ animationDelay: `${i * 80}ms` })}
          >
            <div className="flex justify-between items-start mb-5">
              <p className="text-secondary text-sm font-medium">{card.title}</p>
              <div className={`p-2.5 rounded-lg ${PASTEL_BG[card.color] || 'bg-surface-container'} ${card.color}`}>
                <Icon name={card.icon} className="text-base" />
              </div>
            </div>
            {loading ? (
              <div className="h-8 w-28 bg-surface-container rounded animate-pulse" />
            ) : (
              <div className="flex items-baseline gap-3">
                <h2 className="text-[2rem] font-bold text-primary leading-none tracking-tight">{card.value}</h2>
                {card.change && (
                  <span className={`text-sm font-medium flex items-center font-metadata-mono ${
                    card.change.direction === 'up' ? 'text-emerald-signal'
                    : card.change.direction === 'down' ? 'text-deep-rose'
                    : 'text-muted-slate'
                  }`}>
                    <Icon
                      name={card.change.direction === 'up' ? 'trending-up'
                        : card.change.direction === 'down' ? 'trending-down'
                        : 'minus-small'}
                      className="text-[15px]"
                    />
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
                <AreaChart data={dispoAreaData}>
                  <defs>
                    <linearGradient id="dispoGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#3B82F6" stopOpacity={0.25} />
                      <stop offset="95%" stopColor="#3B82F6" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#787774' }} />
                  <YAxis tick={{ fontSize: 11, fill: '#787774' }} />
                  <Tooltip />
                  <Area type="monotone" dataKey="Total" stroke="#3B82F6" fill="url(#dispoGrad)" strokeWidth={2} />
                </AreaChart>
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

      <div className="border border-whisper-border rounded-xl p-8" style={animateIn({ animationDelay: '320ms' })}>
        <h3 className="font-bold text-lg text-primary mb-6">Contact vs No Contact</h3>
        {loading ? (
          <div className="h-64 bg-surface-container rounded animate-pulse" />
        ) : contactAreaData.length > 0 ? (
          <div className="flex flex-col gap-4">
            <ResponsiveContainer width="100%" height={260}>
              <AreaChart data={contactAreaData}>
                <defs>
                  <linearGradient id="contactGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#4f46e5" stopOpacity={0.25} />
                    <stop offset="95%" stopColor="#4f46e5" stopOpacity={0} />
                  </linearGradient>
                  <linearGradient id="nocontactGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#c4b5fd" stopOpacity={0.25} />
                    <stop offset="95%" stopColor="#c4b5fd" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(0,0,0,0.06)" />
                <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#787774' }} />
                <YAxis tick={{ fontSize: 11, fill: '#787774' }} />
                <Tooltip />
                <Area type="monotone" dataKey="Contact" stroke="#4f46e5" fill="url(#contactGrad)" strokeWidth={2} />
                <Area type="monotone" dataKey="No Contact" stroke="#c4b5fd" fill="url(#nocontactGrad)" strokeWidth={2} />
              </AreaChart>
            </ResponsiveContainer>
            <div className="flex items-center justify-center gap-8">
              <div className="flex items-center gap-3">
                <span className="w-3 h-3 rounded-full shrink-0" style={{ backgroundColor: '#4f46e5' }} />
                <div>
                  <p className="text-xl font-bold text-primary">{contactAreaData[0].Contact}</p>
                  <p className="text-xs text-secondary">Contact</p>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <span className="w-3 h-3 rounded-full shrink-0" style={{ backgroundColor: '#c4b5fd' }} />
                <div>
                  <p className="text-xl font-bold text-primary">{contactAreaData[0]['No Contact']}</p>
                  <p className="text-xs text-secondary">No Contact</p>
                </div>
              </div>
              {contactAreaData[0].Contact + contactAreaData[0]['No Contact'] > 0 && (
                <div className="flex flex-col items-center px-4 py-2 rounded-lg bg-electric-blue/5 dark:bg-electric-blue/10">
                  <p className="text-xl font-bold text-electric-blue">
                    {((contactAreaData[0].Contact / (contactAreaData[0].Contact + contactAreaData[0]['No Contact'])) * 100).toFixed(0)}%
                  </p>
                  <p className="text-[11px] text-secondary">Contact Rate</p>
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

      <div className="flex justify-end gap-3" style={animateIn({ animationDelay: '480ms' })}>
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
            } catch { setError('Failed to export Excel'); }
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
              setError('Failed to export PDF');
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
        <button
          onClick={() => {
            exportAnalyticsCSV(
              agentReport ? { name: 'Agent Performance', columns: agentReport.columns, rows: agentReport.rows } : null,
              period,
              period === 'Custom' ? customStart : undefined,
              period === 'Custom' ? customEnd : undefined,
            );
          }}
          className="bg-primary text-on-primary px-4 py-2 rounded-[6px] font-medium text-sm hover:scale-[0.98] transition-transform flex items-center gap-2"
        >
          <Icon name="download" className="text-sm" />
          Export Analytics CSV
        </button>
      </div>
    </>
  );
}
