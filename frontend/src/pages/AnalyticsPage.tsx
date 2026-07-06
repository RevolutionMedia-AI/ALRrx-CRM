import { useEffect, useState, useMemo } from 'react';
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell,
} from 'recharts';
import { getDashboardSummary, getReport, exportDashboardPdf, exportDashboardExcel, getStaffing, getAgentPerformanceWithSales } from '../services/api';
import { getVicidialSalesSummary } from '../services/vicidialFormApi';
import { exportAgentPerformanceExcel } from '../utils/agentPerformanceExcel';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, MetricCardDto, SalesSummary } from '../types';
import DispositionLegend, { type DispositionItem } from '../components/dashboard/DispositionLegend';
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

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(amount);
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

const BUSINESS_TZ = 'America/Tijuana';

const TIJUANA_FMT = new Intl.DateTimeFormat('en-CA', {
  timeZone: BUSINESS_TZ,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
});

const TIJUANA_DATE_FMT = new Intl.DateTimeFormat('en-CA', {
  timeZone: BUSINESS_TZ,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

function formatTijuanaDateTime(d: Date): string {
  const parts = TIJUANA_FMT.formatToParts(d);
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? '00';
  return `${get('year')}-${get('month')}-${get('day')} ${get('hour')}:${get('minute')}:${get('second')}`;
}

function getTijuanaDateParts(d: Date): { year: number; month: number; day: number; dayOfWeek: number } {
  const dateStr = TIJUANA_DATE_FMT.format(d);
  const [y, m, day] = dateStr.split('-').map(Number);
  const dayOfWeek = new Date(y, m - 1, day).getDay();
  return { year: y, month: m, day, dayOfWeek };
}

function buildVicidialParams(p: Period, customStartDate: string, customEndDate: string): { from?: string; to?: string } {
  if (p === 'Custom') {
    return { from: `${customStartDate} 00:00:00`, to: `${customEndDate} 23:59:59` };
  }
  const now = new Date();
  const tp = getTijuanaDateParts(now);
  const todayLocalMidnight = new Date(tp.year, tp.month - 1, tp.day, 0, 0, 0);
  const tomorrow = new Date(todayLocalMidnight.getTime() + 24 * 60 * 60 * 1000);
  if (p === 'Today') {
    return { from: formatTijuanaDateTime(todayLocalMidnight), to: formatTijuanaDateTime(tomorrow) };
  }
  if (p === 'Week') {
    const daysSinceMonday = tp.dayOfWeek === 0 ? 6 : tp.dayOfWeek - 1;
    const startLocalMidnight = new Date(todayLocalMidnight.getTime() - daysSinceMonday * 24 * 60 * 60 * 1000);
    return { from: formatTijuanaDateTime(startLocalMidnight), to: formatTijuanaDateTime(tomorrow) };
  }
  if (p === 'Month') {
    const startLocalMidnight = new Date(tp.year, tp.month - 1, 1, 0, 0, 0);
    const nextMonthStart = new Date(tp.year, tp.month, 1, 0, 0, 0);
    return { from: formatTijuanaDateTime(startLocalMidnight), to: formatTijuanaDateTime(nextMonthStart) };
  }
  return {};
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

type SortKey = 'user' | 'calls' | 'sales' | 'contacts' | 'conv' | 'aht' | 'formSales' | 'formRevenue';
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
  const [contactReport, setContactReport] = useState<ReportDto | null>(null);
  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [staffingReport, setStaffingReport] = useState<ReportDto | null>(null);
  const [salesSummary, setSalesSummary] = useState<SalesSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [salesLoading, setSalesLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string>('');
  const [sortKey, setSortKey] = useState<SortKey>('sales');
  const [sortDir, setSortDir] = useState<SortDir>('desc');
  const [exportingPdf, setExportingPdf] = useState(false);
  const [exportingExcel, setExportingExcel] = useState(false);
  const [exportingAgentExcel, setExportingAgentExcel] = useState(false);
  const [showPeriodComparison, setShowPeriodComparison] = useState(false);
  const [vicidialRefreshKey, setVicidialRefreshKey] = useState(0);
  const [refreshing, setRefreshing] = useState(false);

  const filter = (p: Period): TimeFilterDto => {
    if (p === 'Custom') return { period: PERIOD_API[p], customStart: `${customStart}T00:00:00`, customEnd: `${customEnd}T23:59:59` };
    return { period: PERIOD_API[p] };
  };

  const fetchAnalytics = async (p: Period) => {
    setLoading(true);
    setSalesLoading(true);
    setError(null);
    try {
      const filterResult = filter(p);
      const vicidialParams = buildVicidialParams(p, customStart, customEnd);
      const [s, prev, contact, agents, st, sales] = await Promise.all([
        getDashboardSummary(filterResult),
        getDashboardSummary(previousPeriod(p)).catch(() => null),
        getReport('contact_vs_nocontact', filterResult).catch(() => null),
        getAgentPerformanceWithSales(filterResult).catch(() => null),
        getStaffing().catch(() => null),
        getVicidialSalesSummary(vicidialParams.from, vicidialParams.to, 500).catch(() => null),
      ]);
      setSummary(s);
      setPrevSummary(prev);
      setContactReport(contact);
      setAgentReport(agents);
      setStaffingReport(st);
      setSalesSummary(sales);
    } catch {
      setError('Failed to load analytics data');
    } finally {
      setLoading(false);
      setSalesLoading(false);
    }
  };

  useEffect(() => {
    fetchAnalytics(period);
    setLastUpdated(new Date().toLocaleTimeString());
  }, [period, customStart, customEnd]);

  const handleManualRefresh = async () => {
    setRefreshing(true);
    setVicidialRefreshKey((k) => k + 1);
    try {
      await fetchAnalytics(period);
      setLastUpdated(new Date().toLocaleTimeString());
    } finally {
      setRefreshing(false);
    }
  };

  const handleExportAgentExcel = async () => {
    if (sortedAgents.length === 0 || exportingAgentExcel) return;
    setExportingAgentExcel(true);
    try {
      const stamp = new Date().toISOString().split('T')[0];
      const filename = `ALTRX_AgentPerformance_${period}_${stamp}.xlsx`;
      const range = buildVicidialParams(period, customStart, customEnd);
      await exportAgentPerformanceExcel(sortedAgents, filename, {
        period,
        from: range.from,
        to: range.to,
      });
    } catch (err) {
      console.error('[AnalyticsPage] Agent Performance export failed', err);
      setError('Failed to export Agent Performance to Excel');
    } finally {
      setExportingAgentExcel(false);
    }
  };

  const handleSort = (key: SortKey) => {
    if (sortKey === key) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortKey(key); setSortDir('desc'); }
  };

  const colMap: Record<SortKey, string> = {
    user: 'Name', calls: 'Calls_Handled', sales: 'Sales_Made',
    contacts: 'Contacts', conv: 'Conversion_Percentage', aht: 'AHT',
    formSales: 'Form_Sales_Count', formRevenue: 'Form_Sales_Amount',
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

  const totalCallsMetric = summary ? findMetric(summary.metrics, 'Total Calls') : undefined;
  const salesMetric = summary ? findMetric(summary.metrics, 'Sales Today') : undefined;
  const contactsMetric = summary ? findMetric(summary.metrics, 'Contacts') : undefined;
  const noContactsMetric = summary ? findMetric(summary.metrics, 'No Contacts') : undefined;
  const leadsDialed = summary ? findMetric(summary.metrics, 'Leads Dialed') : undefined;
  const leadsContacted = summary ? findMetric(summary.metrics, 'Leads Contacted') : undefined;
  const contactRateMetric = summary ? findMetric(summary.metrics, 'Contact Rate') : undefined;
  const ahtMetric = summary ? findMetric(summary.metrics, 'Handle Time') : undefined;
  const occupancyMetric = summary ? findMetric(summary.metrics, 'Occupancy') : undefined;

  const staffRows = staffingReport?.rows ?? [];
  const activeAgents = staffRows.filter((r) => {
    const s = String(r.Status ?? '').toUpperCase();
    return ['READY', 'INCALL', 'QUEUE', 'PAUSED'].includes(s);
  }).length;

  const dialedNum = parseMetricNumber(leadsDialed?.value);
  const salesNum = parseMetricNumber(salesMetric?.value);
  const conversionRate = dialedNum > 0 ? ((salesNum / dialedNum) * 100).toFixed(2) + '%' : '--';
  const revenueNum = salesSummary?.totalSales ?? 0;
  const averageSaleValue = salesNum > 0 && revenueNum > 0
    ? new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(revenueNum / salesNum)
    : '--';

  const prevSales = prevSummary ? findMetric(prevSummary.metrics, 'Sales Today') : undefined;
  const prevContacts = prevSummary ? findMetric(prevSummary.metrics, 'Contacts') : undefined;
  const prevNoContacts = prevSummary ? findMetric(prevSummary.metrics, 'No Contacts') : undefined;
  const prevTotalCalls = prevSummary ? findMetric(prevSummary.metrics, 'Total Calls') : undefined;

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
      className="px-5 py-4 font-medium cursor-pointer select-none hover:text-primary dark:hover:text-white transition-colors whitespace-nowrap"
      onClick={() => handleSort(key)}
    >
      <div className="flex items-center gap-1">
        {label}
        {sortArrow(key)}
      </div>
    </th>
  );

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
            Analytics — ALTRX
          </h1>
          <p className="text-secondary mt-1 flex items-center gap-2 text-sm">
            <span className="w-2 h-2 rounded-full bg-emerald-signal" />
            <span>
              Click refresh to update{lastUpdated && ` • Last updated: ${lastUpdated}`}
            </span>
          </p>
        </div>
        <div className="flex flex-col gap-2 items-end">
          <div className="flex gap-2 flex-wrap items-center">
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
              disabled={refreshing || loading}
              className="flex items-center gap-2 px-3 py-1.5 border border-whisper-border rounded bg-pure-surface text-secondary hover:text-primary transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed"
              title="Refresh analytics data"
            >
              <span className={`material-symbols-outlined text-[20px] ${refreshing ? 'animate-spin' : ''}`}>sync</span>
              <span>{refreshing ? 'Refreshing...' : 'Refresh'}</span>
            </button>
          </div>
          {/* Export Analytics ALTRX - botones horizontales justo debajo del periodo */}
          <div className="flex gap-2 flex-wrap">
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
              className="flex items-center justify-center gap-2 px-3 py-1.5 bg-emerald-signal text-white rounded-lg font-medium text-sm hover:scale-[0.98] transition-transform disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span className="material-symbols-outlined text-[18px]">table_chart</span>
              {exportingExcel ? 'Generating...' : 'Export Analytics ALTRX Excel'}
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
                } catch { setError('Failed to generate PDF'); }
                finally { setExportingPdf(false); }
              }}
              disabled={exportingPdf}
              className="flex items-center justify-center gap-2 px-3 py-1.5 bg-deep-rose text-white rounded-lg font-medium text-sm hover:scale-[0.98] transition-transform disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span className="material-symbols-outlined text-[18px]">picture_as_pdf</span>
              {exportingPdf ? 'Generating...' : 'Export Analytics ALTRX PDF'}
            </button>
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      {/* ========== SECTION 1: Operational Summary (6 cards) ========== */}
      <section>
        <h2 className="font-bold text-sm text-secondary uppercase tracking-wider font-metadata-mono mb-3">
          Operational Summary
        </h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
          <KpiCard
            title="Leads Dialed"
            value={leadsDialed?.value ?? '0'}
            icon="phone_forwarded"
            valueColor="var(--card-value-blue)"
            loading={loading}
          />
          <KpiCard
            title="Total Calls"
            value={totalCallsMetric?.value ?? '0'}
            icon="call"
            valueColor="var(--card-value-dark)"
            loading={loading}
          />
          <KpiCard
            title="No Contacts"
            value={noContactsMetric?.value ?? '0'}
            icon="call_end"
            valueColor="var(--card-value-red)"
            loading={loading}
          />
          <KpiCard
            title="Contacted"
            value={leadsContacted?.value ?? '0'}
            icon="contact_page"
            valueColor="var(--card-value-emerald)"
            loading={loading}
          />
          <KpiCard
            title="Contact Rate"
            value={contactRateMetric?.value ?? '0%'}
            icon="percent"
            valueColor="#8B5CF6"
            loading={loading}
          />
          <KpiCard
            title="Active Agents"
            value={String(activeAgents)}
            icon="groups"
            valueColor="var(--card-value-emerald)"
            loading={loading}
          />
        </div>
      </section>

      {/* ========== SECTION 2: Sales (3 cards) ========== */}
      <section>
        <h2 className="font-bold text-sm text-secondary uppercase tracking-wider font-metadata-mono mb-3">
          Sales
        </h2>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <KpiCard
            title="Sales"
            value={salesMetric?.value ?? '0'}
            icon="confirmation_number"
            valueColor="var(--card-value-emerald)"
            loading={loading}
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
          <KpiCard
            title="Avg Sale Value"
            value={averageSaleValue}
            icon="trending_up"
            valueColor="var(--card-value-emerald)"
            loading={salesLoading}
            isCurrency
          />
        </div>
      </section>

      {/* ========== SECTION 3: Performance (3 cards + Conversion Rate explanation) ========== */}
      <section>
        <h2 className="font-bold text-sm text-secondary uppercase tracking-wider font-metadata-mono mb-3">
          Performance
        </h2>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <ConversionRateCard
            value={conversionRate}
            loading={loading}
          />
          <KpiCard
            title="Avg Handle Time"
            value={ahtMetric?.value ?? '--'}
            icon="timer"
            valueColor="var(--card-value-blue)"
            loading={loading}
          />
          <KpiCard
            title="Occupancy"
            value={occupancyMetric?.value ?? '--'}
            icon="pie_chart"
            valueColor="var(--card-value-blue)"
            loading={loading}
          />
        </div>
      </section>

      {/* ========== 2-COLUMN BODY ========== */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* LEFT COLUMN (8/12) */}
        <div className="lg:col-span-8 flex flex-col gap-6">
          {/* 1. Dispositions chart */}
          <section className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-xl shadow-card">
            <div className="p-6 border-b border-whisper-border flex justify-between items-center">
              <div>
                <h3 className="font-bold text-lg text-primary">Dispositions</h3>
                <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
                  Distribution over the period
                </p>
              </div>
              <span className="material-symbols-outlined text-electric-blue text-2xl">monitoring</span>
            </div>
            <div className="p-8 flex flex-col gap-6">
              <div className="w-full h-80">
                {chartData.length > 0 ? (
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={chartData}>
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
                        <Bar
                          key={s.name}
                          dataKey={s.name}
                          fill={DISPOSITION_PALETTE[idx % DISPOSITION_PALETTE.length]}
                          radius={[4, 4, 0, 0]}
                          maxBarSize={48}
                        />
                      ))}
                    </BarChart>
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

          {/* 2. Disposition Details table */}
          <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
            <div className="p-6 border-b border-whisper-border flex justify-between items-center">
              <div>
                <h3 className="font-bold text-lg text-primary">Disposition Details</h3>
                <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
                  Total and percentage per disposition
                </p>
              </div>
              <span className="material-symbols-outlined text-electric-blue text-2xl">format_list_bulleted</span>
            </div>
            {loading ? (
              <div className="p-6 space-y-2 animate-pulse">
                {[1, 2, 3, 4, 5].map((i) => (
                  <div key={i} className="h-8 bg-surface-container rounded" />
                ))}
              </div>
            ) : dispositionItems.length > 0 ? (
              <div className="overflow-x-auto max-h-[360px] overflow-y-auto scrollbar-thin">
                <table className="w-full text-left text-sm border-collapse">
                  <thead className="text-xs uppercase tracking-wider text-secondary font-metadata-mono bg-surface-container-low sticky top-0">
                    <tr>
                      <th className="p-3 font-medium">Disposition</th>
                      <th className="p-3 font-medium text-right">Total</th>
                      <th className="p-3 font-medium text-right">%</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dispositionItems.map((d, i) => {
                      const grandTotal = dispositionItems.reduce((sum, x) => sum + x.value, 0);
                      const pct = grandTotal > 0 ? ((d.value / grandTotal) * 100).toFixed(1) : '0.0';
                      return (
                        <tr key={d.name} className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors">
                          <td className="p-3 text-primary font-medium">
                            <div className="flex items-center gap-2">
                              <span className="w-2.5 h-2.5 rounded-full shrink-0 ring-1 ring-black/5" style={{ backgroundColor: d.color }} />
                              <span className="font-metadata-mono uppercase tracking-wider">{d.name}</span>
                            </div>
                          </td>
                          <td className="p-3 text-right font-metadata-mono">{d.value}</td>
                          <td className="p-3 text-right font-metadata-mono text-secondary">{pct}%</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="p-10 flex flex-col items-center justify-center text-center">
                <span className="material-symbols-outlined text-4xl text-muted-slate/40 mb-2">inbox</span>
                <p className="text-sm font-medium text-primary">No disposition details</p>
                <p className="text-xs text-muted-slate mt-1">Click refresh to load data</p>
              </div>
            )}
          </section>

          {/* 3. Agent Performance table */}
          <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
            <div className="p-6 border-b border-whisper-border flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
              <div>
                <h3 className="font-bold text-lg text-primary">Agent Performance <span className="text-sm font-normal text-secondary">(VICIdial + Form Sales)</span></h3>
                <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
                  Click column headers to sort
                </p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={handleExportAgentExcel}
                  disabled={loading || exportingAgentExcel || sortedAgents.length === 0}
                  className="text-xs px-2.5 py-1 border border-whisper-border dark:border-gray-700 rounded text-secondary dark:text-gray-300 hover:text-primary dark:hover:text-gray-100 hover:bg-surface-container-low dark:hover:bg-gray-800 transition-colors flex items-center gap-1.5 disabled:opacity-50 disabled:cursor-not-allowed"
                  title="Export current view to Excel"
                >
                  <span className={`material-symbols-outlined text-sm ${exportingAgentExcel ? 'animate-spin' : ''}`}>
                    {exportingAgentExcel ? 'progress_activity' : 'download'}
                  </span>
                  <span>{exportingAgentExcel ? 'Generating...' : 'Export Excel'}</span>
                </button>
                <span className="material-symbols-outlined text-electric-blue text-2xl">groups</span>
              </div>
            </div>
            {loading ? (
              <div className="p-6 space-y-3 animate-pulse">
                {[1, 2, 3, 4, 5, 6].map((i) => (
                  <div key={i} className="h-14 bg-surface-container rounded" />
                ))}
              </div>
            ) : sortedAgents.length > 0 ? (
              <div className="overflow-x-auto max-h-[600px] overflow-y-auto scrollbar-thin">
                <table className="w-full text-left text-lg border-collapse">
                  <thead className="text-sm uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800 sticky top-0">
                    <tr>
                      {sortableTh('Agent', 'user')}
                      {sortableTh('Calls Handled', 'calls')}
                      {sortableTh('VICI Sales', 'sales')}
                      {sortableTh('Form Sales', 'formSales')}
                      {sortableTh('Form Revenue', 'formRevenue')}
                      {sortableTh('Contacts', 'contacts')}
                      {sortableTh('Conversion %', 'conv')}
                      {sortableTh('AHT', 'aht')}
                    </tr>
                  </thead>
                  <tbody>
                    {sortedAgents.map((agent, i) => (
                      <tr key={i} className="border-b border-whisper-border dark:border-gray-700 hover:bg-surface-container-lowest dark:hover:bg-gray-800/50">
                        <td className="px-5 py-4 text-primary dark:text-gray-200 font-medium whitespace-nowrap">{String(agent.Name ?? agent.User ?? '')}</td>
                        <td className="px-5 py-4 font-metadata-mono text-primary dark:text-gray-200 whitespace-nowrap">{String(agent.Calls_Handled ?? '0')}</td>
                        <td className="px-5 py-4 font-metadata-mono text-primary dark:text-gray-200 whitespace-nowrap">{String(agent.Sales_Made ?? '0')}</td>
                        <td className="px-5 py-4 font-metadata-mono text-emerald-signal font-semibold whitespace-nowrap" title="Sales registered through the ALTRX Sales Form">
                          {String(agent.Form_Sales_Count ?? '0')}
                        </td>
                        <td className="px-5 py-4 text-emerald-signal font-semibold whitespace-nowrap" title="Total revenue from sales registered through the ALTRX Sales Form">
                          {formatCurrency(Number(agent.Form_Sales_Amount ?? 0))}
                        </td>
                        <td className="px-5 py-4 font-metadata-mono text-primary dark:text-gray-200 whitespace-nowrap">{String(agent.Contacts ?? '0')}</td>
                        <td className="px-5 py-4 font-metadata-mono font-medium text-primary dark:text-gray-200 whitespace-nowrap">
                          {agent.Conversion_Percentage != null ? `${Number(agent.Conversion_Percentage).toFixed(1)}%` : '--'}
                        </td>
                        <td className="px-5 py-4 font-metadata-mono text-secondary dark:text-gray-400 whitespace-nowrap">
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
        </div>

        {/* RIGHT SIDEBAR (4/12) */}
        <div className="lg:col-span-4 flex flex-col gap-6">
          {/* Contact vs No Contact */}
          <section className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused">
            <h3 className="font-bold text-lg text-primary mb-5 flex items-center gap-2">
              <span className="material-symbols-outlined text-electric-blue">pie_chart</span>
              Contact vs No Contact
            </h3>
            {loading ? (
              <div className="h-64 bg-surface-container rounded animate-pulse" />
            ) : contactAreaData.length > 0 ? (
              <div className="flex flex-col gap-4">
                <ResponsiveContainer width="100%" height={220}>
                  <PieChart>
                    <Pie
                      data={[
                        { name: 'Contact', value: contactAreaData[0].Contact, color: '#10b981' },
                        { name: 'No Contact', value: contactAreaData[0]['No Contact'], color: '#ef4444' },
                      ]}
                      cx="50%"
                      cy="50%"
                      innerRadius={55}
                      outerRadius={90}
                      paddingAngle={2}
                      dataKey="value"
                      stroke="#000000"
                      strokeWidth={2}
                      isAnimationActive={false}
                    >
                      <Cell fill="#10b981" />
                      <Cell fill="#ef4444" />
                    </Pie>
                    <Tooltip content={<DarkTooltip />} />
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-3">
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2">
                      <span className="w-2.5 h-2.5 rounded-full shrink-0" style={{ backgroundColor: '#10b981' }} />
                      <span className="text-primary font-medium">Contact</span>
                    </div>
                    <span className="font-metadata-mono text-primary font-bold">{contactAreaData[0].Contact}</span>
                  </div>
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2">
                      <span className="w-2.5 h-2.5 rounded-full shrink-0" style={{ backgroundColor: '#ef4444' }} />
                      <span className="text-primary font-medium">No Contact</span>
                    </div>
                    <span className="font-metadata-mono text-primary font-bold">{contactAreaData[0]['No Contact']}</span>
                  </div>
                  {contactAreaData[0].Contact + contactAreaData[0]['No Contact'] > 0 && (
                    <div className="pt-3 mt-1 border-t border-whisper-border flex items-center justify-between text-sm">
                      <span className="text-secondary">Contact Rate</span>
                      <span className="font-metadata-mono font-bold text-electric-blue">
                        {((contactAreaData[0].Contact / (contactAreaData[0].Contact + contactAreaData[0]['No Contact'])) * 100).toFixed(1)}%
                      </span>
                    </div>
                  )}
                </div>
              </div>
            ) : (
              <div className="h-48 rounded-lg border border-whisper-border bg-surface-container-low flex items-center justify-center text-muted-slate text-sm">
                No contact data available
              </div>
            )}
          </section>

          {/* Period-over-Period snapshot */}
          <section className="bg-pure-surface border border-whisper-border rounded-xl p-6 shadow-diffused">
            <h3 className="font-bold text-lg text-primary mb-5 flex items-center gap-2">
              <span className="material-symbols-outlined text-amber-warmth">compare_arrows</span>
              Comparison for the selected period
            </h3>
            <div className="space-y-4">
              <DeltaRow
                label="Sales"
                current={salesMetric?.value}
                previous={prevSales?.value}
                positiveIsGood
                loading={loading}
              />
              <DeltaRow
                label="Contacts"
                current={contactsMetric?.value}
                previous={prevContacts?.value}
                positiveIsGood
                loading={loading}
              />
              <DeltaRow
                label="No Contacts"
                current={noContactsMetric?.value}
                previous={prevNoContacts?.value}
                positiveIsGood={false}
                loading={loading}
              />
              <DeltaRow
                label="Total Calls"
                current={totalCallsMetric?.value}
                previous={prevTotalCalls?.value}
                positiveIsGood
                loading={loading}
              />
            </div>
            <button
              onClick={() => setShowPeriodComparison(true)}
              className="mt-5 w-full flex items-center justify-center gap-2 px-3 py-2 bg-electric-blue text-white rounded-lg font-medium text-sm hover:scale-[0.98] transition-transform"
            >
              <span className="material-symbols-outlined text-base">compare_arrows</span>
              Open Period Comparison
            </button>
          </section>

          {/* Quick actions */}
        </div>
      </div>

      {/* Vicidial Sales Section (full width) */}
      <VicidialSalesSection
        refreshKey={vicidialRefreshKey}
        pagePeriod={period}
        pageCustomStart={customStart}
        pageCustomEnd={customEnd}
      />

      <PeriodComparisonModal isOpen={showPeriodComparison} onClose={() => setShowPeriodComparison(false)} />
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

function ConversionRateCard({ value, loading }: { value: string; loading?: boolean }) {
  return (
    <div className="relative overflow-hidden bg-gradient-to-br from-electric-blue/15 via-electric-blue/5 to-transparent border-2 border-electric-blue/30 dark:border-electric-blue/40 rounded-lg p-5 shadow-card transition-transform hover:scale-[1.01]">
      <div className="absolute -right-6 -top-6 w-24 h-24 rounded-full bg-electric-blue/10 blur-2xl pointer-events-none" />
      <div className="relative flex justify-between items-start mb-3">
        <div>
          <p className="text-card-label text-[12px] font-bold uppercase tracking-wider text-electric-blue">Conversion Rate</p>
          <p className="text-[10px] text-secondary font-metadata-mono mt-0.5">Sales ÷ Leads Dialed</p>
        </div>
        <div className="p-1.5 bg-electric-blue/15 rounded-md">
          <span className="material-symbols-outlined text-[16px] text-electric-blue">percent</span>
        </div>
      </div>
      {loading ? (
        <div className="h-7 w-24 bg-surface-container rounded animate-pulse" />
      ) : (
        <h2 className="text-[1.8rem] font-bold leading-none tracking-tight text-electric-blue font-metadata-mono">
          {value}
        </h2>
      )}
      <div className="relative mt-3 pt-3 border-t border-electric-blue/20">
        <p className="text-[11px] text-secondary leading-relaxed">
          <span className="font-semibold text-primary">How it's calculated:</span>{' '}
          Total sales divided by total leads dialed for the selected period. Measures the percentage of dialed leads that became paying customers.
        </p>
      </div>
    </div>
  );
}

function DeltaRow({
  label, current, previous, positiveIsGood, loading,
}: {
  label: string;
  current?: string;
  previous?: string;
  positiveIsGood: boolean;
  loading?: boolean;
}) {
  if (loading) {
    return <div className="h-9 bg-surface-container rounded animate-pulse" />;
  }
  if (!current) return null;
  const change = pctChange(current, previous);
  if (!change) {
    return (
      <div className="flex items-center justify-between text-sm">
        <span className="text-secondary">{label}</span>
        <span className="font-metadata-mono text-primary font-medium">{current}</span>
      </div>
    );
  }
  const isUp = change.direction === 'up';
  const isFlat = change.direction === 'same';
  const isGood = positiveIsGood ? isUp : !isUp && !isFlat;
  const color = isFlat ? 'text-muted-slate' : isGood ? 'text-emerald-signal' : 'text-deep-rose';
  const Icon = isFlat ? MinusSignCircleIcon : isUp ? AnalyticsUpIcon : AnalyticsDownIcon;
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-secondary">{label}</span>
      <div className="flex items-center gap-2">
        <span className="font-metadata-mono text-primary font-medium">{current}</span>
        <span className={`flex items-center gap-0.5 text-xs font-medium font-metadata-mono ${color}`}>
          <Icon size={13} />
          {change.pct}
        </span>
      </div>
    </div>
  );
}
