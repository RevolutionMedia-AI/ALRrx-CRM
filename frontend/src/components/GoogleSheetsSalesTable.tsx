import { useEffect, useState, useRef } from 'react';
import { getGoogleSheetsSales } from '../services/api';
import type { SalesSummary, TimeFilterDto } from '../types';

interface GoogleSheetsSalesTableProps {
  filter: TimeFilterDto;
  periodLabel: string;
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

export default function GoogleSheetsSalesTable({ filter, periodLabel }: GoogleSheetsSalesTableProps) {
  const [data, setData] = useState<SalesSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [sellerFilter, setSellerFilter] = useState('all');
  const [packageFilter, setPackageFilter] = useState('all');
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = async () => {
    try {
      setLoading(true);
      const result = await getGoogleSheetsSales(filter, sellerFilter, packageFilter);
      setData(result);
    } catch {
      setData(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    intervalRef.current = setInterval(load, 30000);
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [filter.period, filter.customStart, filter.customEnd, sellerFilter, packageFilter]);

  return (
    <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
      <div className="p-6 border-b border-whisper-border flex flex-col gap-4">
        <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-3">
          <div>
            <h3 className="font-bold text-lg text-primary flex items-center gap-2">
              <span className="material-symbols-outlined text-emerald-signal text-xl">receipt_long</span>
              Ventas Google Sheets — {periodLabel}
            </h3>
            <p className="text-[12px] text-secondary mt-1 flex items-center gap-2">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-signal animate-pulse" />
              <span>Sincronizado desde Google Forms · Auto-refresca cada 30s</span>
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3 bg-surface-container-low border border-whisper-border rounded-lg px-4 py-3">
          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-secondary text-base">person</span>
            <span className="text-xs font-medium text-secondary uppercase tracking-wider">Vendedor</span>
          </div>
          <select
            value={sellerFilter}
            onChange={(e) => setSellerFilter(e.target.value)}
            className="flex-1 min-w-[160px] max-w-[260px] bg-pure-surface border border-whisper-border rounded px-3 py-1.5 text-sm text-primary focus:outline-none focus:border-electric-blue"
          >
            <option value="all">Todos los vendedores</option>
            {data?.availableSellers.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>

          <div className="h-6 w-px bg-whisper-border mx-1" />

          <div className="flex items-center gap-2">
            <span className="material-symbols-outlined text-secondary text-base">package_2</span>
            <span className="text-xs font-medium text-secondary uppercase tracking-wider">Paquete</span>
          </div>
          <select
            value={packageFilter}
            onChange={(e) => setPackageFilter(e.target.value)}
            className="flex-1 min-w-[180px] max-w-[300px] bg-pure-surface border border-whisper-border rounded px-3 py-1.5 text-sm text-primary focus:outline-none focus:border-electric-blue"
          >
            <option value="all">Todos los paquetes</option>
            {data?.availablePackages.map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="p-6 border-b border-whisper-border grid grid-cols-1 sm:grid-cols-3 gap-4">
        <KpiTile
          label="Total de Ventas"
          value={loading ? '--' : formatCurrency(data?.totalSales ?? 0)}
          icon="payments"
          color="text-emerald-signal"
        />
        <KpiTile
          label="Cantidad de Ventas"
          value={loading ? '--' : String(data?.totalCount ?? 0)}
          icon="shopping_bag"
          color="text-electric-blue"
        />
        <KpiTile
          label="Última Venta"
          value={loading || !data?.lastSale ? '--' : formatCurrency(data.lastSale.amount)}
          subtitle={loading || !data?.lastSale ? '' : `${data.lastSale.sellerName} · ${formatDate(data.lastSale.saleDate)}`}
          icon="trending_up"
          color="text-amber-warmth"
        />
      </div>

      <div>
        {loading ? (
          <div className="p-6 space-y-3 animate-pulse">
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="h-9 bg-surface-container rounded" />
            ))}
          </div>
        ) : data && data.allSales.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-surface-container-low border-b border-whisper-border text-xs uppercase tracking-wider text-secondary font-metadata-mono">
                  <th className="p-4 font-medium">Fecha</th>
                  <th className="p-4 font-medium">Vendedor</th>
                  <th className="p-4 font-medium">Email del Cliente</th>
                  <th className="p-4 font-medium">Paquete</th>
                  <th className="p-4 font-medium text-right">Monto</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {data.allSales.slice(0, 50).map((sale, i) => (
                  <tr key={i} className="border-b border-whisper-border hover:bg-surface-container-lowest dark:hover:bg-gray-800 transition-colors">
                    <td className="p-4 font-metadata-mono text-primary whitespace-nowrap">{formatDate(sale.saleDate)}</td>
                    <td className="p-4">
                      <div className="flex items-center gap-2">
                        <div className="w-6 h-6 rounded-full bg-surface-container flex items-center justify-center text-[10px] font-bold text-primary">
                          {sale.sellerName?.split(' ').map((n) => n[0]).join('').substring(0, 2).toUpperCase() || '--'}
                        </div>
                        <span className="font-medium text-primary">{sale.sellerName || '--'}</span>
                      </div>
                    </td>
                    <td className="p-4 text-secondary font-metadata-mono text-xs">{sale.customerEmail || '--'}</td>
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
            {data.allSales.length > 50 && (
              <div className="p-4 text-center text-xs text-muted-slate border-t border-whisper-border">
                Mostrando 50 de {data.allSales.length} ventas
              </div>
            )}
          </div>
        ) : (
          <div className="p-6 text-sm text-muted-slate text-center">No hay ventas en este período con los filtros seleccionados</div>
        )}
      </div>
    </section>
  );
}

function KpiTile({
  label, value, subtitle, icon, color,
}: { label: string; value: string; subtitle?: string; icon: string; color: string }) {
  return (
    <div className="bg-surface-container-low border border-whisper-border rounded-lg p-5">
      <div className="flex items-center justify-between mb-2">
        <p className="text-[11px] font-medium text-secondary uppercase tracking-wider">{label}</p>
        <span className={`material-symbols-outlined text-lg ${color}`}>{icon}</span>
      </div>
      <p className={`text-[1.75rem] font-bold leading-none ${color}`}>{value}</p>
      {subtitle && <p className="text-xs text-secondary mt-2 font-metadata-mono">{subtitle}</p>}
    </div>
  );
}
