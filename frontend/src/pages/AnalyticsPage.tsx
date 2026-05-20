import { useEffect, useState, useMemo } from 'react';
import {
  PieChart, Pie, Cell, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend,
} from 'recharts';
import { getDashboardSummary, getReport, exportReport } from '../services/api';
import { exportCombinedCSV } from '../utils/csv';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, MetricCardDto } from '../types';

type Period = 'Today' | 'Week' | 'Month';

const DISPOSITION_COLORS = ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#EC4899', '#14B8A6', '#6B7280'];

function previousPeriod(p: Period): TimeFilterDto {
  switch (p) {
    case 'Today': return { period: 'Today', customStart: new Date(Date.now() - 86400000).toISOString(), customEnd: new Date(Date.now() - 86400000).toISOString() };
    case 'Week': return { period: 'Custom', customStart: new Date(Date.now() - 14 * 86400000).toISOString(), customEnd: new Date(Date.now() - 7 * 86400000).toISOString() };
    case 'Month': return { period: 'Custom', customStart: new Date(Date.now() - 60 * 86400000).toISOString(), customEnd: new Date(Date.now() - 30 * 86400000).toISOString() };
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

type SortKey = 'user' | 'calls' | 'sales' | 'contacts' | 'conv' | 'aht';
type SortDir = 'asc' | 'desc';

export default function AnalyticsPage() {
  const [period, setPeriod] = useState<Period>('Today');
  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [prevSummary, setPrevSummary] = useState<DashboardSummaryDto | null>(null);
  const [dispositions, setDispositions] = useState<ReportDto | null>(null);
  const [contactReport, setContactReport] = useState<ReportDto | null>(null);
  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sortKey, setSortKey] = useState<SortKey>('sales');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  const filter = (p: Period): TimeFilterDto => ({ period: p });

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

  useEffect(() => { fetchAnalytics(period); }, [period]);

  const handleSort = (key: SortKey) => {
    if (sortKey === key) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortKey(key); setSortDir('desc'); }
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

  const colMap: Record<SortKey, string> = {
    user: 'Name',
    calls: 'Calls_Handled',
    sales: 'Sales_Made',
    contacts: 'Contacts',
    conv: 'Conversion_Percentage',
    aht: 'AHT',
  };

  const sortArrow = (key: SortKey) => {
    if (sortKey !== key) return <span className="text-muted-slate ml-1 material-symbols-outlined text-[14px]">unfold_more</span>;
    return <span className="ml-1 material-symbols-outlined text-[14px]">{sortDir === 'asc' ? 'expand_less' : 'expand_more'}</span>;
  };

  const salesMetric = summary ? findMetric(summary.metrics, 'Sales Today') : undefined;
  const contactsMetric = summary ? findMetric(summary.metrics, 'Contacts') : undefined;
  const noContactsMetric = summary ? findMetric(summary.metrics, 'No Contacts') : undefined;
  const totalCallsMetric = summary ? findMetric(summary.metrics, 'Total Calls') : undefined;

  const prevSales = prevSummary ? findMetric(prevSummary.metrics, 'Sales Today') : undefined;
  const prevContacts = prevSummary ? findMetric(prevSummary.metrics, 'Contacts') : undefined;
  const prevNoContacts = prevSummary ? findMetric(prevSummary.metrics, 'No Contacts') : undefined;
  const prevTotalCalls = prevSummary ? findMetric(prevSummary.metrics, 'Total Calls') : undefined;

  const kpiCards = [
    { title: 'Sales Today', value: salesMetric?.value ?? '--', change: pctChange(salesMetric?.value ?? '0', prevSales?.value), icon: 'payments', color: 'text-emerald-signal' },
    { title: 'Contacts', value: contactsMetric?.value ?? '--', change: pctChange(contactsMetric?.value ?? '0', prevContacts?.value), icon: 'call_made', color: 'text-electric-blue' },
    { title: 'No Contacts', value: noContactsMetric?.value ?? '--', change: pctChange(noContactsMetric?.value ?? '0', prevNoContacts?.value), icon: 'call_received', color: 'text-deep-rose' },
    { title: 'Total Calls', value: totalCallsMetric?.value ?? '--', change: pctChange(totalCallsMetric?.value ?? '0', prevTotalCalls?.value), icon: 'call', color: 'text-amber-warmth' },
  ];

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
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
        <div>
          <h1 className="font-headline-lg text-headline-lg font-bold text-primary tracking-tight">Analytics</h1>
          <p className="text-secondary text-sm mt-1">Deep dive into historical trends and agent performance</p>
        </div>
        <div className="flex gap-2">
          <div className="bg-surface-container-low border border-whisper-border rounded flex text-sm overflow-hidden">
            {(['Today', 'Week', 'Month'] as Period[]).map((p) => (
              <button
                key={p}
                onClick={() => setPeriod(p)}
                className={`px-4 py-1.5 border-r border-whisper-border last:border-r-0 ${
                  period === p
                    ? 'bg-pure-surface text-primary font-medium'
                    : 'text-secondary hover:bg-surface-container transition-colors'
                }`}
              >
                {p}
              </button>
            ))}
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm">{error}</div>
      )}

      {/* KPI Comparison Cards */}
      <section className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {kpiCards.map((card) => (
          <div key={card.title} className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused">
            <div className="flex justify-between items-start mb-4">
              <p className="text-secondary text-sm font-medium">{card.title}</p>
              <div className={`p-1.5 bg-surface-container rounded-lg ${card.color}`}>
                <span className="material-symbols-outlined text-sm">{card.icon}</span>
              </div>
            </div>
            {loading ? (
              <div className="h-8 w-24 bg-surface-container rounded animate-pulse" />
            ) : (
              <div className="flex items-baseline gap-3">
                <h2 className="text-3xl font-bold text-primary">{card.value}</h2>
                {card.change && (
                  <span className={`text-sm font-medium flex items-center font-metadata-mono ${
                    card.change.direction === 'up' ? 'text-emerald-signal'
                    : card.change.direction === 'down' ? 'text-deep-rose'
                    : 'text-muted-slate'
                  }`}>
                    <span className="material-symbols-outlined text-[16px]">
                      {card.change.direction === 'up' ? 'trending_up'
                      : card.change.direction === 'down' ? 'trending_down'
                      : 'remove'}
                    </span>
                    {card.change.pct}
                  </span>
                )}
              </div>
            )}
          </div>
        ))}
      </section>

      {/* Dispositions Pie Chart + Table */}
      <section className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused p-6">
          <h3 className="font-bold text-lg text-primary mb-4">Dispositions Breakdown</h3>
          {loading ? (
            <div className="h-64 bg-surface-container rounded animate-pulse" />
          ) : summary?.charts?.[0] ? (
            <div className="flex flex-col items-center">
              <ResponsiveContainer width="100%" height={280}>
                <PieChart>
                  <Pie
                    data={summary.charts[0].labels.map((label, i) => ({
                      name: label,
                      value: summary.charts[0].series[0]?.data[i] ?? 0,
                    }))}
                    cx="50%"
                    cy="50%"
                    innerRadius={60}
                    outerRadius={100}
                    paddingAngle={2}
                    dataKey="value"
                  >
                    {summary.charts[0].labels.map((_, i) => (
                      <Cell key={i} fill={DISPOSITION_COLORS[i % DISPOSITION_COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip />
                  <Legend />
                </PieChart>
              </ResponsiveContainer>
            </div>
          ) : (
            <div className="h-64 rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
              No disposition data available
            </div>
          )}
        </div>
        <div className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused p-6">
          <h3 className="font-bold text-lg text-primary mb-4">Disposition Details</h3>
          {loading ? (
            <div className="space-y-2 animate-pulse">{[1, 2, 3, 4, 5].map((i) => <div key={i} className="h-8 bg-surface-container rounded" />)}</div>
          ) : dispositions && dispositions.rows.length > 0 ? (
            <div className="overflow-x-auto max-h-[320px] overflow-y-auto">
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
                    <tr key={i} className="border-b border-whisper-border hover:bg-surface-container-lowest transition-colors">
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
      </section>

      {/* Contact vs No Contact Bar Chart */}
      <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused p-6">
        <h3 className="font-bold text-lg text-primary mb-4">Contact vs No Contact</h3>
        {loading ? (
          <div className="h-64 bg-surface-container rounded animate-pulse" />
        ) : contactReport && contactReport.rows.length > 0 ? (
          <ResponsiveContainer width="100%" height={280}>
            <BarChart
              data={[{
                name: period,
                Contact: Number(contactReport.rows[0]?.Contact ?? 0),
                'No Contact': Number(contactReport.rows[0]?.No_Contact ?? 0),
              }]}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#e5e2e1" />
              <XAxis dataKey="name" tick={{ fontSize: 12, fill: '#64748B' }} />
              <YAxis tick={{ fontSize: 12, fill: '#64748B' }} />
              <Tooltip />
              <Legend />
              <Bar dataKey="Contact" fill="#10B981" radius={[4, 4, 0, 0]} name="Contact" />
              <Bar dataKey="No Contact" fill="#EF4444" radius={[4, 4, 0, 0]} name="No Contact" />
            </BarChart>
          </ResponsiveContainer>
        ) : (
          <div className="h-64 rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
            No contact data available
          </div>
        )}
      </section>

      {/* Agent Performance Table */}
      <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
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
                  <tr key={i} className="border-b border-whisper-border hover:bg-surface-container-lowest transition-colors">
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

      {/* Export */}
      <div className="flex justify-end">
        <button
          onClick={async () => {
            const sections: { name: string; columns: string[]; rows: Record<string, unknown>[] }[] = [];
            if (summary) {
              sections.push({
                name: 'KPI Metrics',
                columns: ['Label', 'Value'],
                rows: summary.metrics.map((m) => ({ Label: m.label, Value: m.value })),
              });
            }
            if (dispositions) sections.push({ name: 'Dispositions', columns: dispositions.columns, rows: dispositions.rows });
            if (contactReport) sections.push({ name: 'Contact vs No Contact', columns: contactReport.columns, rows: contactReport.rows });
            if (agentReport) sections.push({ name: 'Agent Performance', columns: agentReport.columns, rows: agentReport.rows });
            try {
              const blob = await exportReport({ reportId: 'dashboard', format: 'csv', timeFilter: filter(period) });
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url;
              a.download = `analytics-${period.toLowerCase()}.csv`;
              a.click();
              URL.revokeObjectURL(url);
            } catch {
              exportCombinedCSV(sections);
            }
          }}
          className="bg-primary text-on-primary px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm flex items-center gap-2"
        >
          <span className="material-symbols-outlined text-sm">download</span>
          Export Analytics CSV
        </button>
      </div>
    </>
  );
}
