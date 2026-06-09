import { useState, useEffect, useCallback, useMemo } from 'react';
import {
  getSliceReportsByDate,
  getSliceReportsByDateRange,
  getSliceReportsByMonth,
} from '../../services/sliceReportsApi';
import type { SliceReport } from '../types';

type PeriodKind = 'daily' | 'weekly' | 'monthly' | 'custom';

/**
 * Computes the inclusive [start, end] for the last complete week ending
 * yesterday (Mon..Sun) so the default range is meaningful.
 */
function defaultWeekRange(): { start: string; end: string } {
  const today = new Date();
  today.setUTCDate(today.getUTCDate() - 1); // yesterday
  const dow = today.getUTCDay(); // 0=Sun, 1=Mon
  const end = new Date(today);
  const start = new Date(today);
  const offsetToMonday = (dow + 6) % 7; // days since Monday
  start.setUTCDate(today.getUTCDate() - offsetToMonday);
  return {
    start: start.toISOString().slice(0, 10),
    end:   end.toISOString().slice(0, 10),
  };
}

function formatIsoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

export default function SliceReportsPeriodPage() {
  const [period, setPeriod] = useState<PeriodKind>('daily');
  const [dailyDate, setDailyDate] = useState<string>(formatIsoDate(new Date()));
  const [monthlyYear, setMonthlyYear] = useState<number>(new Date().getFullYear());
  const [monthlyMonth, setMonthlyMonth] = useState<number>(new Date().getMonth() + 1);
  const initialRange = useMemo(defaultWeekRange, []);
  const [customStart, setCustomStart] = useState<string>(initialRange.start);
  const [customEnd, setCustomEnd]   = useState<string>(initialRange.end);
  const [podFilter, setPodFilter]   = useState<string>('');

  const [reports, setReports]   = useState<SliceReport[]>([]);
  const [loading, setLoading]   = useState(false);
  const [error, setError]       = useState<string | null>(null);

  const fetchReports = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      let result: SliceReport[] = [];
      const pod = podFilter.trim() || undefined;
      if (period === 'daily') {
        result = await getSliceReportsByDate(dailyDate, pod);
      } else if (period === 'weekly') {
        // Weekly = 7-day range ending yesterday (already populated by defaultWeekRange)
        result = await getSliceReportsByDateRange(customStart, customEnd, pod);
      } else if (period === 'monthly') {
        result = await getSliceReportsByMonth(monthlyYear, monthlyMonth, pod);
      } else if (period === 'custom') {
        if (customEnd < customStart) {
          setError("'End' must be on or after 'Start'.");
          setLoading(false);
          return;
        }
        result = await getSliceReportsByDateRange(customStart, customEnd, pod);
      }
      setReports(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to fetch reports');
    } finally {
      setLoading(false);
    }
  }, [period, dailyDate, monthlyYear, monthlyMonth, customStart, customEnd, podFilter]);

  useEffect(() => { fetchReports(); }, [fetchReports]);

  // Quick metric: count reports + summarize by pod (uses any pod label we find)
  const summary = useMemo(() => {
    const podCounts: Record<string, number> = {};
    let totalRows = 0;
    for (const r of reports) {
      for (const g of r.dailyGlobal ?? []) {
        podCounts[g.pod] = (podCounts[g.pod] ?? 0) + 1;
        totalRows++;
      }
    }
    return { reportCount: reports.length, totalRows, podCounts };
  }, [reports]);

  return (
    <>
      <div className="flex flex-col gap-2 shrink-0">
        <h2 className="font-headline-lg text-3xl font-bold text-primary">Reports by Period</h2>
        <p className="text-secondary max-w-2xl">
          Slice reports are persisted in SQLite at <code>/data/slice.db</code> via EF Core.
          Pick a period to query the historical record — daily, weekly, monthly, or
          a custom date range.
        </p>
      </div>

      <div className="flex flex-col md:flex-row md:items-end gap-4 shrink-0">
        <div className="flex border border-whisper-border rounded-lg overflow-hidden bg-surface">
          {(['daily', 'weekly', 'monthly', 'custom'] as PeriodKind[]).map((p) => (
            <button
              key={p}
              onClick={() => setPeriod(p)}
              className={`px-4 py-2 text-sm border-r last:border-r-0 border-whisper-border transition-colors ${
                period === p
                  ? 'bg-primary text-on-primary font-semibold'
                  : 'text-secondary hover:bg-surface-container'
              }`}
            >
              {p[0].toUpperCase() + p.slice(1)}
            </button>
          ))}
        </div>

        {period === 'daily' && (
          <div className="flex flex-col">
            <label className="text-xs text-secondary font-metadata-mono mb-1">Date</label>
            <input
              type="date"
              value={dailyDate}
              onChange={(e) => setDailyDate(e.target.value)}
              className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
            />
          </div>
        )}

        {period === 'weekly' && (
          <div className="flex flex-col">
            <label className="text-xs text-secondary font-metadata-mono mb-1">Week (Mon..Sun, last complete week)</label>
            <div className="flex gap-2 items-center">
              <input
                type="date"
                value={customStart}
                onChange={(e) => setCustomStart(e.target.value)}
                className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              />
              <span className="text-secondary">to</span>
              <input
                type="date"
                value={customEnd}
                onChange={(e) => setCustomEnd(e.target.value)}
                className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              />
            </div>
          </div>
        )}

        {period === 'monthly' && (
          <>
            <div className="flex flex-col">
              <label className="text-xs text-secondary font-metadata-mono mb-1">Year</label>
              <input
                type="number"
                value={monthlyYear}
                onChange={(e) => setMonthlyYear(parseInt(e.target.value, 10) || new Date().getFullYear())}
                className="w-28 px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              />
            </div>
            <div className="flex flex-col">
              <label className="text-xs text-secondary font-metadata-mono mb-1">Month</label>
              <select
                value={monthlyMonth}
                onChange={(e) => setMonthlyMonth(parseInt(e.target.value, 10))}
                className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              >
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <option key={m} value={m}>
                    {new Date(2000, m - 1, 1).toLocaleString(undefined, { month: 'long' })}
                  </option>
                ))}
              </select>
            </div>
          </>
        )}

        {period === 'custom' && (
          <div className="flex flex-col">
            <label className="text-xs text-secondary font-metadata-mono mb-1">Custom range</label>
            <div className="flex gap-2 items-center">
              <input
                type="date"
                value={customStart}
                onChange={(e) => setCustomStart(e.target.value)}
                className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              />
              <span className="text-secondary">to</span>
              <input
                type="date"
                value={customEnd}
                onChange={(e) => setCustomEnd(e.target.value)}
                className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
              />
            </div>
          </div>
        )}

        <div className="flex flex-col flex-1 max-w-xs">
          <label className="text-xs text-secondary font-metadata-mono mb-1">Pod filter (optional)</label>
          <input
            type="text"
            value={podFilter}
            onChange={(e) => setPodFilter(e.target.value)}
            placeholder="e.g. ES-12"
            className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none"
          />
        </div>

        <button
          onClick={fetchReports}
          disabled={loading}
          className="px-4 py-2 bg-primary text-on-primary rounded-lg font-semibold hover:bg-inverse-surface transition-colors disabled:opacity-50 text-sm self-end"
        >
          {loading ? 'Loading...' : 'Refresh'}
        </button>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="bg-pure-surface border border-whisper-border rounded-xl p-4">
          <p className="font-metadata-mono text-metadata-mono text-secondary">Reports found</p>
          <p className="font-display-hero text-headline-lg font-bold text-primary">{summary.reportCount}</p>
        </div>
        <div className="bg-pure-surface border border-whisper-border rounded-xl p-4">
          <p className="font-metadata-mono text-metadata-mono text-secondary">Daily Global rows</p>
          <p className="font-display-hero text-headline-lg font-bold text-primary">{summary.totalRows}</p>
        </div>
        <div className="bg-pure-surface border border-whisper-border rounded-xl p-4">
          <p className="font-metadata-mono text-metadata-mono text-secondary">By pod</p>
          <div className="flex flex-wrap gap-2 mt-2">
            {Object.keys(summary.podCounts).length === 0 ? (
              <span className="text-secondary text-sm">No pod data</span>
            ) : (
              Object.entries(summary.podCounts).map(([pod, n]) => (
                <span
                  key={pod}
                  className="font-metadata-mono text-xs bg-electric-blue/10 text-electric-blue px-2 py-1 rounded"
                >
                  {pod}: {n}
                </span>
              ))
            )}
          </div>
        </div>
      </div>

      <div className="bg-pure-surface border border-whisper-border rounded-xl shadow-sm overflow-hidden">
        <div className="px-4 py-2 border-b border-whisper-border bg-surface-container/40 text-xs text-secondary font-metadata-mono">
          {reports.length} report{reports.length === 1 ? '' : 's'}
        </div>
        {loading ? (
          <div className="p-10 text-center text-secondary text-sm">Loading reports...</div>
        ) : reports.length === 0 ? (
          <div className="p-10 text-center text-muted-slate text-sm">
            No reports found for this period. Try a different range or upload a new file.
          </div>
        ) : (
          <ul className="divide-y divide-whisper-border">
            {reports.map((r) => (
              <li key={r.id} className="p-4 hover:bg-surface-container/30 transition-colors">
                <div className="flex items-center justify-between gap-4">
                  <div>
                    <p className="font-metadata-mono text-sm text-primary font-bold">
                      {new Date(r.reportDate).toLocaleDateString()} — {r.id.slice(0, 8)}
                    </p>
                    <p className="text-xs text-secondary mt-1">
                      Generated {new Date(r.generatedAt).toLocaleString()} by {r.generatedByEmail}
                    </p>
                  </div>
                  <div className="flex gap-2 text-xs font-metadata-mono text-secondary">
                    <span className="bg-surface-container px-2 py-1 rounded">
                      Global: {r.dailyGlobal?.length ?? 0}
                    </span>
                    <span className="bg-surface-container px-2 py-1 rounded">
                      Agents: {r.dailyAgents?.length ?? 0}
                    </span>
                    <span className="bg-surface-container px-2 py-1 rounded">
                      Shops: {r.shopDaily?.length ?? 0}
                    </span>
                    <a
                      href={`/slice/reports`}
                      onClick={(e) => { e.preventDefault(); window.location.hash = `#/slice/pod?reportId=${r.id}`; }}
                      className="text-electric-blue hover:underline px-2 py-1"
                    >
                      Open in Pod Overview →
                    </a>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </>
  );
}
