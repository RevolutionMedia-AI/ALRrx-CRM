import { useEffect, useMemo, useState } from 'react';
import { listAllVicidialSales } from '../../services/vicidialFormApi';
import type { VicidialSaleDto } from '../../types';
import { extractErrorMessage } from '../../utils/extractErrorMessage';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';

interface VicidialSalesSectionProps {
  period: Period;
  customStart: string;
  customEnd: string;
  refreshKey?: number;
}

function getPeriodRange(period: Period, customStart: string, customEnd: string): { from: string; to: string } {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  if (period === 'Today') {
    const end = new Date(today.getTime() + 24 * 60 * 60 * 1000);
    return { from: today.toISOString(), to: end.toISOString() };
  }
  if (period === 'Week') {
    const dayOfWeek = today.getDay();
    const start = new Date(today.getTime() - dayOfWeek * 24 * 60 * 60 * 1000);
    const end = new Date(today.getTime() + 24 * 60 * 60 * 1000);
    return { from: start.toISOString(), to: end.toISOString() };
  }
  if (period === 'Month') {
    const start = new Date(today.getFullYear(), today.getMonth(), 1);
    const end = new Date(today.getFullYear(), today.getMonth() + 1, 1);
    return { from: start.toISOString(), to: end.toISOString() };
  }
  return {
    from: new Date(`${customStart}T00:00:00`).toISOString(),
    to: new Date(`${customEnd}T23:59:59`).toISOString(),
  };
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(amount);
}

function formatDate(iso: string): string {
  if (!iso) return '--';
  try {
    return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: '2-digit', year: 'numeric' });
  } catch {
    return '--';
  }
}

export default function VicidialSalesSection({ period, customStart, customEnd, refreshKey = 0 }: VicidialSalesSectionProps) {
  const [sales, setSales] = useState<VicidialSaleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [repFilter, setRepFilter] = useState<string>('all');
  const [refreshNonce, setRefreshNonce] = useState(0);

  const range = useMemo(() => getPeriodRange(period, customStart, customEnd), [period, customStart, customEnd]);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const data = await listAllVicidialSales(range.from, range.to, 500);
        if (!cancelled) setSales(data);
      } catch (err: unknown) {
        if (!cancelled) {
          setError(extractErrorMessage(err, 'Could not load Vicidial sales'));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [range.from, range.to, refreshKey, refreshNonce]);

  const totalCount = sales.length;
  const totalAmount = sales.reduce((sum, s) => sum + Number(s.amount), 0);
  const uniqueAgents = new Set(sales.map((s) => s.salesRep)).size;

  const reps = useMemo(() => {
    const set = new Set(sales.map((s) => s.salesRep).filter(Boolean));
    return Array.from(set).sort((a, b) => a.localeCompare(b));
  }, [sales]);

  const filteredSales = useMemo(() => {
    if (repFilter === 'all') return sales;
    return sales.filter((s) => s.salesRep === repFilter);
  }, [sales, repFilter]);

  return (
    <section
      className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg shadow-card"
      style={{
        opacity: 0,
        transform: 'translateY(12px)',
        animation: 'fadeSlideIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) forwards',
      }}
    >
      <header className="p-6 border-b border-whisper-border dark:border-gray-700 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
        <div>
          <h3 className="font-bold text-lg text-primary dark:text-gray-100">Vicidial Form Sales</h3>
          <p className="text-xs text-secondary dark:text-gray-400 mt-0.5">Sales submitted by agents via the Vicidial standalone form</p>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          {reps.length > 0 && (
            <div className="flex items-center gap-2">
              <label className="text-xs text-secondary dark:text-gray-400">Agent:</label>
              <select
                value={repFilter}
                onChange={(e) => setRepFilter(e.target.value)}
                className="text-xs px-2 py-1 border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
              >
                <option value="all">All agents ({reps.length})</option>
                {reps.map((r) => (
                  <option key={r} value={r}>{r}</option>
                ))}
              </select>
            </div>
          )}
          <button
            onClick={() => setRefreshNonce((n) => n + 1)}
            disabled={loading}
            className="text-xs px-2.5 py-1 border border-whisper-border dark:border-gray-700 rounded text-secondary dark:text-gray-300 hover:text-primary dark:hover:text-gray-100 hover:bg-surface-container-low dark:hover:bg-gray-800 transition-colors flex items-center gap-1.5 disabled:opacity-50 disabled:cursor-not-allowed"
            title="Refresh sales"
          >
            <span className={`material-symbols-outlined text-sm ${loading ? 'animate-spin' : ''}`}>sync</span>
            <span>Refresh</span>
          </button>
        </div>
      </header>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-px bg-whisper-border dark:bg-gray-700 border-b border-whisper-border dark:border-gray-700">
        <KpiTile label="Total Sales" value={loading ? '--' : totalCount.toString()} valueColor="var(--card-value-emerald)" />
        <KpiTile label="Total Amount" value={loading ? '--' : formatCurrency(totalAmount)} valueColor="var(--card-value-emerald)" />
        <KpiTile label="Unique Agents" value={loading ? '--' : uniqueAgents.toString()} valueColor="var(--card-value-dark)" />
      </div>

      {error ? (
        <div className="p-6 text-deep-rose text-sm">{error}</div>
      ) : loading ? (
        <div className="p-6 space-y-3 animate-pulse">
          {[1, 2, 3].map((i) => <div key={i} className="h-9 bg-surface-container dark:bg-gray-800 rounded" />)}
        </div>
      ) : filteredSales.length === 0 ? (
        <div className="p-12 text-sm text-muted-slate text-center">
          {repFilter === 'all' ? 'No Vicidial sales recorded for this period' : `No sales by "${repFilter}" in this period`}
        </div>
      ) : (
        <div className="overflow-x-auto max-h-[420px] overflow-y-auto scrollbar-thin">
          <table className="w-full text-left text-sm border-collapse">
            <thead className="text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800 sticky top-0">
              <tr>
                <th className="p-3 font-medium">Date</th>
                <th className="p-3 font-medium">Agent</th>
                <th className="p-3 font-medium">Client</th>
                <th className="p-3 font-medium">Email</th>
                <th className="p-3 font-medium">Bundle</th>
                <th className="p-3 font-medium text-right">Amount</th>
              </tr>
            </thead>
            <tbody>
              {filteredSales.map((s) => (
                <tr key={s.id} className="border-b border-whisper-border dark:border-gray-700 hover:bg-surface-container-lowest dark:hover:bg-gray-800/50">
                  <td className="p-3 font-metadata-mono text-primary dark:text-gray-200 whitespace-nowrap">{formatDate(s.saleDate)}</td>
                  <td className="p-3 text-primary dark:text-gray-200 font-medium whitespace-nowrap">{s.salesRep}</td>
                  <td className="p-3 text-primary dark:text-gray-200">
                    <div className="font-medium">{s.clientName}</div>
                    <div className="text-[11px] text-secondary dark:text-gray-400 font-metadata-mono">{s.clientPhone}</div>
                  </td>
                  <td className="p-3 text-secondary dark:text-gray-400 font-metadata-mono text-xs">{s.clientEmail}</td>
                  <td className="p-3">
                    <span className="px-2 py-1 bg-surface-container dark:bg-gray-800 rounded text-[11px] text-primary dark:text-gray-200 font-medium border border-whisper-border dark:border-gray-700 whitespace-nowrap">
                      {s.bundle}
                    </span>
                  </td>
                  <td className="p-3 font-bold text-emerald-signal font-metadata-mono text-right whitespace-nowrap">{formatCurrency(s.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function KpiTile({ label, value, valueColor }: { label: string; value: string; valueColor: string }) {
  return (
    <div className="bg-pure-surface dark:bg-gray-900 p-6">
      <p className="text-card-label text-[12px] font-medium uppercase tracking-wider">{label}</p>
      <p className="text-[1.8rem] font-bold mt-1 leading-none" style={{ color: valueColor }}>{value}</p>
    </div>
  );
}
