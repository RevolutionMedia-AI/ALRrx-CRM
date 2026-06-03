import { useEffect, useState } from 'react';
import { getGoogleSheetsSales, exportDashboardPdf, exportDashboardExcel } from '../../services/api';
import type { SalesSummary, TimeFilterDto } from '../../types';

interface SalesSectionProps {
  filter: TimeFilterDto;
  periodLabel: string;
  onError: (msg: string) => void;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
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

function formatDateTime(iso: string): string {
  if (!iso) return '--';
  try {
    const d = new Date(iso);
    const dateStr = d.toLocaleDateString('en-US', { month: 'short', day: '2-digit', year: 'numeric' });
    const timeStr = d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false });
    return `${dateStr} ${timeStr}`;
  } catch {
    return '--';
  }
}

function getInitials(name: string): string {
  if (!name) return '--';
  return name.split(' ').map((n) => n[0]).join('').substring(0, 2).toUpperCase();
}

export default function SalesSection({ filter, periodLabel, onError }: SalesSectionProps) {
  const [data, setData] = useState<SalesSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [sellerFilter, setSellerFilter] = useState('all');
  const [packageFilter, setPackageFilter] = useState('all');
  const [lastUpdated, setLastUpdated] = useState<string>('');
  const [exportingPdf, setExportingPdf] = useState(false);
  const [exportingExcel, setExportingExcel] = useState(false);

  const load = async () => {
    try {
      setLoading(true);
      const result = await getGoogleSheetsSales(filter, sellerFilter, packageFilter);
      setData(result);
      setLastUpdated(new Date().toLocaleTimeString());
    } catch {
      setData(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, [filter.period, filter.customStart, filter.customEnd, sellerFilter, packageFilter]);

  const handleExportPdf = async () => {
    setExportingPdf(true);
    try {
      const blob = await exportDashboardPdf(filter);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `ALTRX_Dashboard_${periodLabel}_${new Date().toISOString().split('T')[0]}.pdf`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      onError('Failed to export PDF');
    } finally {
      setExportingPdf(false);
    }
  };

  const handleExportExcel = async () => {
    setExportingExcel(true);
    try {
      const blob = await exportDashboardExcel(filter);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `ALTRX_Dashboard_${periodLabel}_${new Date().toISOString().split('T')[0]}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      onError('Failed to export Excel');
    } finally {
      setExportingExcel(false);
    }
  };

  const hasSales = data && data.allSales.length > 0;

  return (
    <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
      <div className="p-6 border-b border-whisper-border flex flex-col gap-4">
        <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-3">
          <div>
            <h3 className="font-bold text-lg text-primary flex items-center gap-2">
              <span className="material-symbols-outlined text-emerald-signal text-xl">receipt_long</span>
              Sales — {periodLabel}
            </h3>
            <p className="text-[12px] text-secondary mt-1 flex items-center gap-2">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-signal" />
              <span>
                Synced from Google Forms · Click refresh to update{lastUpdated && ` • ${lastUpdated}`}
              </span>
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <div className="bg-surface-container-low border border-whisper-border rounded-lg px-3 py-1.5 text-right">
              <p className="text-[10px] font-medium text-secondary uppercase tracking-wider">Count</p>
              <p className="text-base font-bold text-electric-blue font-metadata-mono leading-none">
                {loading ? '--' : String(data?.totalCount ?? 0)}
              </p>
            </div>
            <div className="bg-surface-container-low border border-whisper-border rounded-lg px-3 py-1.5 text-right">
              <p className="text-[10px] font-medium text-secondary uppercase tracking-wider">Total</p>
              <p className="text-base font-bold text-emerald-signal font-metadata-mono leading-none">
                {loading ? '--' : formatCurrency(data?.totalSales ?? 0)}
              </p>
            </div>
            <button
              onClick={load}
              disabled={loading}
              className="flex items-center gap-1.5 px-3 py-2 border border-whisper-border rounded-lg bg-pure-surface text-secondary hover:text-primary transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed"
              title="Refresh sales data"
            >
              <span className={`material-symbols-outlined text-[18px] ${loading ? 'animate-spin' : ''}`}>sync</span>
              <span className="hidden sm:inline">Refresh</span>
            </button>
            <button
              onClick={handleExportExcel}
              disabled={exportingExcel}
              className="flex items-center gap-1.5 px-3 py-2 bg-emerald-signal text-white rounded-lg font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span className="material-symbols-outlined text-[18px]">table_chart</span>
              <span className="hidden sm:inline">{exportingExcel ? 'Generating...' : 'Excel'}</span>
            </button>
            <button
              onClick={handleExportPdf}
              disabled={exportingPdf}
              className="flex items-center gap-1.5 px-3 py-2 bg-deep-rose text-white rounded-lg font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span className="material-symbols-outlined text-[18px]">picture_as_pdf</span>
              <span className="hidden sm:inline">{exportingPdf ? 'Generating...' : 'PDF'}</span>
            </button>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3 bg-surface-container-low border border-whisper-border rounded-lg px-4 py-3">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-secondary text-base">person</span>
            <span className="text-xs font-medium text-secondary uppercase tracking-wider">Seller</span>
          </div>
          <select
            value={sellerFilter}
            onChange={(e) => setSellerFilter(e.target.value)}
            className="flex-1 min-w-[160px] max-w-[260px] bg-pure-surface border border-whisper-border rounded px-3 py-1.5 text-sm text-primary focus:outline-none focus:border-electric-blue"
          >
            <option value="all">All sellers</option>
            {data?.availableSellers.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>

          <div className="h-6 w-px bg-whisper-border mx-1" />

          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-secondary text-base">package_2</span>
            <span className="text-xs font-medium text-secondary uppercase tracking-wider">Package</span>
          </div>
          <select
            value={packageFilter}
            onChange={(e) => setPackageFilter(e.target.value)}
            className="flex-1 min-w-[180px] max-w-[300px] bg-pure-surface border border-whisper-border rounded px-3 py-1.5 text-sm text-primary focus:outline-none focus:border-electric-blue"
          >
            <option value="all">All packages</option>
            {data?.availablePackages.map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>
      </div>

      <div>
        {loading ? (
          <div className="p-6 space-y-3 animate-pulse">
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="h-9 bg-surface-container rounded" />
            ))}
          </div>
        ) : hasSales ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-surface-container-low border-b border-whisper-border text-xs uppercase tracking-wider text-secondary font-metadata-mono">
                  <th className="p-4 font-medium">Date</th>
                  <th className="p-4 font-medium">Seller</th>
                  <th className="p-4 font-medium">Client Email</th>
                  <th className="p-4 font-medium">Package</th>
                  <th className="p-4 font-medium text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {data!.allSales.slice(0, 50).map((sale, i) => (
                  <tr
                    key={i}
                    className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors"
                  >
                    <td className="p-4 font-metadata-mono text-primary whitespace-nowrap">
                      {formatDate(sale.saleDate)}
                    </td>
                    <td className="p-4">
                      <div className="flex items-center gap-2">
                        <div className="w-6 h-6 rounded-full bg-surface-container flex items-center justify-center text-[10px] font-bold text-primary">
                          {getInitials(sale.sellerName)}
                        </div>
                        <span className="font-medium text-primary">{sale.sellerName || '--'}</span>
                      </div>
                    </td>
                    <td className="p-4 text-secondary font-metadata-mono text-xs">
                      {sale.customerEmail || '--'}
                    </td>
                    <td className="p-4">
                      <span className="px-2 py-1 bg-surface-container rounded text-xs text-primary font-medium border border-whisper-border whitespace-nowrap">
                        {sale.package || '--'}
                      </span>
                    </td>
                    <td className="p-4 font-bold text-emerald-signal font-metadata-mono text-right whitespace-nowrap">
                      {formatCurrency(sale.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {data!.allSales.length > 50 && (
              <div className="p-4 text-center text-xs text-muted-slate border-t border-whisper-border">
                Showing 50 of {data!.allSales.length} sales
              </div>
            )}
          </div>
        ) : (
          <div className="p-10 flex flex-col items-center justify-center text-center">
            <div className="w-14 h-14 rounded-full bg-surface-container-low flex items-center justify-center mb-3">
              <span className="material-symbols-outlined text-3xl text-muted-slate/50">inventory_2</span>
            </div>
            <p className="text-sm font-medium text-primary">No sales yet today</p>
            <p className="text-xs text-muted-slate mt-1">
              {sellerFilter !== 'all' || packageFilter !== 'all'
                ? 'Try changing the seller or package filter'
                : 'Sales will appear here once they are synced from Google Forms'}
            </p>
          </div>
        )}
      </div>

      {data?.lastSale && (
        <div className="px-6 py-3 border-t border-whisper-border bg-surface-container-low flex flex-wrap items-center gap-x-6 gap-y-1 text-xs">
          <div className="flex items-center gap-1.5">
            <span className="material-symbols-outlined text-amber-warmth text-[16px]">trending_up</span>
            <span className="text-secondary uppercase tracking-wider font-semibold">Last Sale</span>
          </div>
          <span className="font-bold text-amber-warmth font-metadata-mono">
            {formatCurrency(data.lastSale.amount)}
          </span>
          <span className="text-primary font-medium">{data.lastSale.sellerName}</span>
          <span className="text-secondary font-metadata-mono">{formatDateTime(data.lastSale.timestamp)}</span>
        </div>
      )}
    </section>
  );
}
