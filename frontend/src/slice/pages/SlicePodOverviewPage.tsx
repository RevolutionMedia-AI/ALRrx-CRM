import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  getSliceReports,
  getSliceReport,
} from '../../services/sliceReportsApi';
import type {
  SliceDailyGlobalRow,
  SliceReport,
  SliceReportSummary,
} from '../types';
import { formatInt } from '../utils/formatSlice';

type Period = 'Diaria' | 'Semanal' | 'Mensual';
type PodStatus = 'Critical' | 'Warning' | 'Healthy';

interface PodKpi {
  numberOfShops: number;
  queuedCalls: number;
  handledCalls: number;
  missedCalls: number;
  transferCalls: number;
}

interface PodCard {
  name: string;
  region: string;
  handledRate: number;
  errorRate: number;
  status: PodStatus;
  raw: SliceDailyGlobalRow;
}

const HANDLED_TARGET = 90;
const ERROR_TARGET = 2;
const CONVERSION_TARGET = 45;

function classifyPod(row: SliceDailyGlobalRow): PodStatus {
  const handledRate = row.handled + row.missedCalls > 0
    ? (row.handled / (row.handled + row.missedCalls)) * 100
    : 0;
  if (handledRate < HANDLED_TARGET || row.pctOrdersWithErrors > ERROR_TARGET) return 'Critical';
  if (handledRate < HANDLED_TARGET + 3 || row.pctOrdersWithErrors > ERROR_TARGET - 0.5) return 'Warning';
  return 'Healthy';
}

function aggregateKpis(rows: SliceDailyGlobalRow[]): PodKpi {
  return rows.reduce<PodKpi>(
    (acc, row) => ({
      numberOfShops: acc.numberOfShops + row.orderCount,
      queuedCalls: acc.queuedCalls + row.queued,
      handledCalls: acc.handledCalls + row.handled,
      missedCalls: acc.missedCalls + row.missedCalls,
      transferCalls: acc.transferCalls + row.transferredCalls,
    }),
    { numberOfShops: 0, queuedCalls: 0, handledCalls: 0, missedCalls: 0, transferCalls: 0 }
  );
}

function buildPodCards(rows: SliceDailyGlobalRow[]): PodCard[] {
  return rows.map((row) => {
    const handledRate = row.handled + row.missedCalls > 0
      ? (row.handled / (row.handled + row.missedCalls)) * 100
      : 0;
    return {
      name: row.pod || 'Unassigned',
      region: '—',
      handledRate,
      errorRate: row.pctOrdersWithErrors,
      status: classifyPod(row),
      raw: row,
    };
  });
}

function buildWeeklyConversionSeries(rows: SliceDailyGlobalRow[]) {
  const days = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  if (rows.length === 0) {
    return days.map((label) => ({ label, value: 0 }));
  }
  return days.map((label, idx) => {
    const row = rows[idx % rows.length];
    return { label, value: Math.round(row.convPct) };
  });
}

const STATUS_STYLES: Record<PodStatus, { border: string; pill: string; text: string; icon: string; iconColor: string }> = {
  Critical: {
    border: 'border-l-deep-rose',
    pill: 'bg-deep-rose/10 text-deep-rose',
    text: 'text-deep-rose',
    icon: 'report',
    iconColor: 'text-deep-rose',
  },
  Warning: {
    border: 'border-l-amber-warmth',
    pill: 'bg-amber-warmth/10 text-amber-warmth',
    text: 'text-amber-warmth',
    icon: 'warning',
    iconColor: 'text-amber-warmth',
  },
  Healthy: {
    border: 'border-l-emerald-signal',
    pill: 'bg-emerald-signal/10 text-emerald-signal',
    text: 'text-primary',
    icon: 'check_circle',
    iconColor: 'text-emerald-signal',
  },
};

export default function SlicePodOverviewPage() {
  const [period, setPeriod] = useState<Period>('Diaria');
  const [reports, setReports] = useState<SliceReportSummary[]>([]);
  const [selectedReportId, setSelectedReportId] = useState<string | null>(null);
  const [report, setReport] = useState<SliceReport | null>(null);
  const [search, setSearch] = useState('');

  const [loadingReports, setLoadingReports] = useState(false);
  const [loadingReport, setLoadingReport] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadReports = useCallback(async () => {
    setLoadingReports(true);
    setError(null);
    try {
      const data = await getSliceReports();
      setReports(data);
      if (data.length > 0 && !selectedReportId) {
        setSelectedReportId(data[0].id);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load reports');
    } finally {
      setLoadingReports(false);
    }
  }, [selectedReportId]);

  useEffect(() => {
    loadReports();
  }, [loadReports]);

  const loadReport = useCallback(async (id: string) => {
    setLoadingReport(true);
    setError(null);
    try {
      const data = await getSliceReport(id);
      setReport(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load report');
    } finally {
      setLoadingReport(false);
    }
  }, []);

  useEffect(() => {
    if (selectedReportId) loadReport(selectedReportId);
  }, [selectedReportId, loadReport]);

  const globalRows = useMemo<SliceDailyGlobalRow[]>(
    () => report?.dailyGlobal ?? [],
    [report]
  );

  const filteredGlobalRows = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return globalRows;
    return globalRows.filter((r) => r.pod.toLowerCase().includes(q));
  }, [globalRows, search]);

  const kpis = useMemo(() => aggregateKpis(filteredGlobalRows), [filteredGlobalRows]);

  const podCards = useMemo(() => buildPodCards(filteredGlobalRows), [filteredGlobalRows]);

  const conversionSeries = useMemo(
    () => buildWeeklyConversionSeries(filteredGlobalRows),
    [filteredGlobalRows]
  );

  const currentConversion = conversionSeries[conversionSeries.length - 1]?.value ?? 0;
  const peakConversion = conversionSeries.reduce(
    (max, p) => (p.value > max ? p.value : max),
    0
  );

  const refundedPct = filteredGlobalRows.length
    ? filteredGlobalRows.reduce(
        (acc, r) => acc + (r.orderCount > 0 ? (r.refundedOrders / r.orderCount) * 100 : 0),
        0
      ) / filteredGlobalRows.length
    : 0;

  const avgErrorPct = filteredGlobalRows.length
    ? filteredGlobalRows.reduce((acc, r) => acc + r.pctOrdersWithErrors, 0) /
      filteredGlobalRows.length
    : 0;

  return (
    <>
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-4 shrink-0">
        <div>
          <h2 className="font-headline-lg text-3xl font-bold text-primary mb-1">POD Overview</h2>
          <p className="text-secondary">
            Aggregated call-center health, conversion trends and live POD status.
          </p>
        </div>
        <div className="flex gap-3 flex-wrap">
          <div className="relative">
            <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-secondary text-lg">
              search
            </span>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 pr-4 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none w-full md:w-64 transition-all shadow-sm"
              placeholder="Search POD..."
              type="text"
            />
          </div>
          <select
            value={selectedReportId ?? ''}
            onChange={(e) => setSelectedReportId(e.target.value || null)}
            disabled={loadingReports || reports.length === 0}
            className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none transition-all shadow-sm"
          >
            {reports.length === 0 && <option value="">No reports yet</option>}
            {reports.map((r) => (
              <option key={r.id} value={r.id}>
                {new Date(r.reportDate).toLocaleDateString()} — {r.podCount} pods / {r.agentCount} agents
              </option>
            ))}
          </select>
          <div className="flex border border-whisper-border rounded-lg overflow-hidden bg-surface">
            {(['Diaria', 'Semanal', 'Mensual'] as Period[]).map((p) => (
              <button
                key={p}
                onClick={() => setPeriod(p)}
                className={`px-3 py-2 text-sm border-r last:border-r-0 border-whisper-border transition-colors ${
                  period === p
                    ? 'bg-primary text-on-primary font-semibold'
                    : 'text-secondary hover:bg-surface-container'
                }`}
              >
                {p}
              </button>
            ))}
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      {loadingReports && !report && (
        <div className="bg-surface border border-whisper-border rounded-xl p-10 text-center text-secondary text-sm">
          Loading reports...
        </div>
      )}

      {!loadingReports && reports.length === 0 && !error && (
        <div className="bg-surface border border-whisper-border rounded-xl p-10 flex flex-col items-center text-center">
          <span className="material-symbols-outlined text-4xl text-muted-slate/40 mb-2">dashboard</span>
          <p className="text-sm font-medium text-primary">No reports yet</p>
          <p className="text-xs text-muted-slate mt-1">
            Upload an Excel or ZIP file to populate the POD overview.
          </p>
        </div>
      )}

      {report && (
        <>
          <section className="grid grid-cols-1 md:grid-cols-5 gap-4">
            <KpiCard
              label="Number of Shops"
              value={formatInt(kpis.numberOfShops)}
              icon="storefront"
              trend={kpis.numberOfShops > 0 ? 'up' : 'flat'}
              trendLabel={kpis.numberOfShops > 0 ? 'Live data' : 'No data'}
              trendTone="emerald"
            />
            <KpiCard
              label="Queued Calls"
              value={formatInt(kpis.queuedCalls)}
              icon="queue"
              trend="flat"
              trendLabel="Live queue"
              trendTone="muted"
            />
            <KpiCard
              label="Handled Calls"
              value={formatInt(kpis.handledCalls)}
              icon="support_agent"
              trend={kpis.handledCalls > 0 ? 'up' : 'flat'}
              trendLabel={kpis.handledCalls > 0 ? 'In progress' : 'No data'}
              trendTone="emerald"
            />
            <KpiCard
              label="Missed Calls"
              value={formatInt(kpis.missedCalls)}
              icon="phone_missed"
              trend={kpis.missedCalls > 0 ? 'down' : 'flat'}
              trendLabel={kpis.missedCalls > 0 ? 'Watch' : 'No misses'}
              trendTone="emerald"
            />
            <KpiCard
              label="Transfer Calls"
              value={formatInt(kpis.transferCalls)}
              icon="phone_forwarded"
              trend={kpis.transferCalls > 0 ? 'up' : 'flat'}
              trendLabel={kpis.transferCalls > 0 ? 'Tracked' : 'No transfers'}
              trendTone="amber"
            />
          </section>

          <section className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            <div
              className="lg:col-span-2 bg-pure-surface border border-whisper-border rounded-2xl p-6 lg:p-8 flex flex-col"
              style={{ boxShadow: '0 20px 40px -15px rgba(0,0,0,0.05)' }}
            >
              <div className="flex justify-between items-center mb-6">
                <div>
                  <h3 className="font-display-hero text-2xl font-bold text-primary tracking-tight">
                    Conversion Rate
                  </h3>
                  <p className="font-metadata-mono text-xs text-secondary uppercase tracking-widest">
                    Historical performance (Daily)
                  </p>
                </div>
                <span
                  className={`font-metadata-mono text-xs flex items-center gap-2 border px-3 py-1.5 rounded-full ${
                    currentConversion >= CONVERSION_TARGET
                      ? 'border-emerald-signal/20 text-emerald-signal bg-emerald-signal/5'
                      : 'border-amber-warmth/20 text-amber-warmth bg-amber-warmth/5'
                  }`}
                >
                  <span
                    className={`w-1.5 h-1.5 rounded-full animate-pulse ${
                      currentConversion >= CONVERSION_TARGET ? 'bg-emerald-signal' : 'bg-amber-warmth'
                    }`}
                  />
                  Target: &gt; {CONVERSION_TARGET}%
                </span>
              </div>

              <div className="relative flex-1">
                <div className="absolute inset-0 flex flex-col justify-between py-2 pointer-events-none">
                  <div className="border-t border-whisper-border/50 w-full" />
                  <div className="border-t border-whisper-border/50 w-full" />
                  <div className="border-t border-whisper-border/50 w-full" />
                  <div className="border-t border-whisper-border/50 w-full" />
                </div>
                <div
                  className="absolute left-0 right-0 border-t-2 border-dashed border-secondary/20 z-10 flex items-center justify-end px-2"
                  style={{ bottom: `${CONVERSION_TARGET}%` }}
                >
                  <span className="bg-pure-surface px-2 font-metadata-mono text-[10px] text-secondary -translate-y-1/2">
                    THRESHOLD
                  </span>
                </div>
                <div className="h-64 w-full flex items-end gap-3 pt-4 px-2 relative">
                  {conversionSeries.map((point) => {
                    const height = Math.max(2, Math.min(100, point.value));
                    const aboveThreshold = point.value >= CONVERSION_TARGET;
                    return (
                      <div
                        key={point.label}
                        className={`flex-1 rounded-sm relative group/bar ${
                          aboveThreshold ? 'bg-emerald-signal' : 'bg-emerald-signal/20'
                        }`}
                        style={{ height: `${height}%` }}
                      >
                        <div className="absolute -top-7 left-1/2 -translate-x-1/2 bg-primary text-white text-[10px] px-2 py-1 rounded opacity-0 group-hover/bar:opacity-100 transition-opacity font-metadata-mono">
                          {point.value}%
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
              <div className="flex justify-between mt-6 px-2 font-metadata-mono text-[11px] text-secondary uppercase tracking-widest">
                {conversionSeries.map((p) => (
                  <span key={p.label}>{p.label}</span>
                ))}
              </div>

              <div className="mt-4 pt-4 border-t border-whisper-border flex items-center justify-between text-xs text-secondary font-metadata-mono">
                <span>
                  Current: <span className="text-primary">{currentConversion}%</span>
                </span>
                <span>
                  Peak: <span className="text-primary">{peakConversion}%</span>
                </span>
              </div>
            </div>

            <div className="flex flex-col gap-6">
              <ThresholdCard
                label="Refunded Orders %"
                value={`${refundedPct.toFixed(1)}%`}
                widthPct={Math.min(100, refundedPct * 8)}
                tone={refundedPct > 3 ? 'rose' : 'emerald'}
                note={refundedPct > 3 ? 'Above threshold' : 'Below threshold'}
                thresholdNote="Target < 3%"
              />
              <ThresholdCard
                label="% Orders with Errors"
                value={`${avgErrorPct.toFixed(1)}%`}
                widthPct={Math.min(100, avgErrorPct * 20)}
                tone={avgErrorPct > ERROR_TARGET ? 'rose' : 'emerald'}
                note={avgErrorPct > ERROR_TARGET ? 'Above threshold' : `Target < ${ERROR_TARGET}%`}
              />
            </div>
          </section>

          <section>
            <h3 className="font-headline-lg text-2xl font-bold text-primary mb-4">Live POD Status</h3>
            {podCards.length === 0 ? (
              <div className="bg-surface border border-whisper-border rounded-xl p-10 text-center text-secondary text-sm">
                No POD data for the current report.
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {podCards.map((pod) => {
                  const style = STATUS_STYLES[pod.status];
                  return (
                    <div
                      key={pod.name}
                      className={`bg-pure-surface border border-whisper-border border-l-4 ${style.border} rounded-xl p-6 relative flex flex-col justify-between transition-all`}
                      style={{ boxShadow: '0 20px 40px -15px rgba(0,0,0,0.05)' }}
                    >
                      <div
                        className={`absolute top-3 right-3 ${style.pill} px-2 py-0.5 font-metadata-mono text-[10px] font-bold rounded uppercase tracking-wider`}
                      >
                        {pod.status}
                      </div>
                      <div>
                        <div className="flex justify-between items-start mb-6">
                          <div>
                            <h4 className="font-headline-lg text-xl font-bold text-primary tracking-tight">
                              {pod.name}
                            </h4>
                            <p className="font-metadata-mono text-xs text-secondary">
                              Region: {pod.region}
                            </p>
                          </div>
                          <span
                            className={`material-symbols-outlined ${style.iconColor} text-2xl`}
                            style={{ fontVariationSettings: "'FILL' 1" }}
                          >
                            {style.icon}
                          </span>
                        </div>
                        <div className="space-y-4">
                          <div className="flex justify-between items-end pb-3 border-b border-whisper-border">
                            <span className="text-xs text-secondary uppercase font-bold tracking-widest">
                              Handled Rate
                            </span>
                            <span
                              className={`font-metadata-mono text-lg font-bold leading-none tracking-tight ${
                                pod.handledRate < HANDLED_TARGET ? 'text-deep-rose' : style.text
                              }`}
                            >
                              {pod.handledRate.toFixed(0)}%
                              {pod.handledRate < HANDLED_TARGET && (
                                <span className="text-[10px] opacity-60 font-normal ml-1">
                                  &lt; {HANDLED_TARGET}%
                                </span>
                              )}
                            </span>
                          </div>
                          <div className="flex justify-between items-end">
                            <span className="text-xs text-secondary uppercase font-bold tracking-widest">
                              Error Rate
                            </span>
                            <span
                              className={`font-metadata-mono text-lg font-bold leading-none tracking-tight ${
                                pod.errorRate > ERROR_TARGET ? 'text-deep-rose' : style.text
                              }`}
                            >
                              {pod.errorRate.toFixed(1)}%
                              {pod.errorRate > ERROR_TARGET && (
                                <span className="text-[10px] opacity-60 font-normal ml-1">
                                  &gt; {ERROR_TARGET}%
                                </span>
                              )}
                            </span>
                          </div>
                        </div>
                      </div>
                      {pod.status === 'Critical' && (
                        <button className="w-full mt-8 bg-deep-rose text-white py-2.5 rounded-lg font-metadata-mono text-xs font-bold hover:bg-opacity-90 transition-all shadow-md active:scale-95">
                          INVESTIGATE SUB-PODS
                        </button>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </section>
        </>
      )}
    </>
  );
}

interface KpiCardProps {
  label: string;
  value: string;
  icon: string;
  trend: 'up' | 'down' | 'flat';
  trendLabel: string;
  trendTone: 'emerald' | 'amber' | 'muted';
}

function KpiCard({ label, value, icon, trend, trendLabel, trendTone }: KpiCardProps) {
  const trendColor =
    trendTone === 'emerald'
      ? 'text-emerald-signal'
      : trendTone === 'amber'
      ? 'text-amber-warmth'
      : 'text-secondary';
  const trendIcon =
    trend === 'up' ? 'trending_up' : trend === 'down' ? 'trending_down' : 'trending_flat';
  return (
    <div
      className="bg-pure-surface rounded-xl p-6 border border-whisper-border flex flex-col justify-between"
      style={{ boxShadow: '0 20px 40px -15px rgba(0,0,0,0.05)' }}
    >
      <div className="flex justify-between items-start mb-4">
        <p className="font-metadata-mono text-metadata-mono text-secondary">{label}</p>
        <span className="material-symbols-outlined text-muted-slate">{icon}</span>
      </div>
      <div>
        <h3 className="font-display-hero text-headline-lg font-bold text-primary">{value}</h3>
        <div className={`flex items-center gap-1 mt-2 ${trendColor} font-metadata-mono text-metadata-mono`}>
          <span className="material-symbols-outlined text-sm">{trendIcon}</span>
          <span>{trendLabel}</span>
        </div>
      </div>
    </div>
  );
}

interface ThresholdCardProps {
  label: string;
  value: string;
  widthPct: number;
  tone: 'emerald' | 'rose';
  note: string;
  thresholdNote?: string;
}

function ThresholdCard({ label, value, widthPct, tone, note, thresholdNote }: ThresholdCardProps) {
  const barColor = tone === 'rose' ? 'bg-deep-rose' : 'bg-emerald-signal';
  const noteColor = tone === 'rose' ? 'text-deep-rose' : 'text-emerald-signal';
  return (
    <div
      className="bg-pure-surface border border-whisper-border rounded-xl p-6"
      style={{ boxShadow: '0 20px 40px -15px rgba(0,0,0,0.05)' }}
    >
      <h4 className="font-metadata-mono text-metadata-mono text-secondary mb-2">{label}</h4>
      <div className="flex items-baseline gap-3">
        <span className="font-display-hero text-headline-lg font-bold text-primary">{value}</span>
        <span className={`${noteColor} font-metadata-mono text-metadata-mono text-sm`}>{note}</span>
      </div>
      <div className="w-full bg-surface-container-highest h-2 mt-4 rounded-full overflow-hidden">
        <div className={`${barColor} h-full rounded-full`} style={{ width: `${widthPct}%` }} />
      </div>
      {thresholdNote && (
        <p className="text-[10px] text-muted-slate font-metadata-mono mt-2 uppercase tracking-widest">
          {thresholdNote}
        </p>
      )}
    </div>
  );
}
