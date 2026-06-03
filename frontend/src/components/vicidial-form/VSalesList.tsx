import { useEffect, useState } from 'react';
import { listVicidialSales } from '../../services/vicidialFormApi';
import type { VicidialSaleDto } from '../../types';
import { extractErrorMessage } from '../../utils/extractErrorMessage';

interface VSalesListProps {
  refreshKey: number;
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

export default function VSalesList({ refreshKey }: VSalesListProps) {
  const [sales, setSales] = useState<VicidialSaleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const data = await listVicidialSales();
        if (!cancelled) setSales(data);
      } catch (err: unknown) {
        if (!cancelled) {
          setError(extractErrorMessage(err, 'Could not load your registered sales'));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [refreshKey]);

  const totalAmount = sales.reduce((sum, s) => sum + Number(s.amount), 0);

  return (
    <section className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-2xl shadow-card overflow-hidden">
      <header className="px-6 py-4 border-b border-whisper-border dark:border-gray-700 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
        <div className="flex items-center gap-2">
          <span className="material-symbols-outlined text-emerald-signal">receipt_long</span>
          <h2 className="text-base font-bold text-primary dark:text-gray-100">My Registered Sales</h2>
          <span className="text-[11px] text-secondary dark:text-gray-400">({sales.length})</span>
        </div>
        <div className="flex items-center gap-3">
          <div className="bg-surface-container-low dark:bg-gray-800 border border-whisper-border dark:border-gray-700 rounded-lg px-3 py-1.5">
            <p className="text-[10px] font-medium text-secondary dark:text-gray-400 uppercase tracking-wider">Total</p>
            <p className="text-sm font-bold text-emerald-signal font-metadata-mono">{formatCurrency(totalAmount)}</p>
          </div>
        </div>
      </header>

      {loading ? (
        <div className="p-6 space-y-3 animate-pulse">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-9 bg-surface-container dark:bg-gray-800 rounded" />
          ))}
        </div>
      ) : error ? (
        <div className="px-6 py-6 text-deep-rose text-sm">{error}</div>
      ) : sales.length === 0 ? (
        <div className="px-6 py-10 text-center text-sm text-muted-slate">
          You haven't registered any sales yet.
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-surface-container-low dark:bg-gray-800 border-b border-whisper-border dark:border-gray-700 text-[11px] uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono">
                <th className="p-3 font-medium">Date</th>
                <th className="p-3 font-medium">Client</th>
                <th className="p-3 font-medium">Email</th>
                <th className="p-3 font-medium">Bundle</th>
                <th className="p-3 font-medium text-right">Amount</th>
              </tr>
            </thead>
            <tbody className="text-sm">
              {sales.map((s) => (
                <tr key={s.id} className="border-b border-whisper-border dark:border-gray-700 last:border-b-0 hover:bg-surface-container-lowest dark:hover:bg-gray-800/50">
                  <td className="p-3 font-metadata-mono text-primary dark:text-gray-200 whitespace-nowrap">
                    {formatDate(s.saleDate)}
                  </td>
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
                  <td className="p-3 font-bold text-emerald-signal font-metadata-mono text-right whitespace-nowrap">
                    {formatCurrency(s.amount)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
