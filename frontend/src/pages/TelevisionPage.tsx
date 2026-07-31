import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import { tvApi, type TvAgentSales } from '../services/tvApi';
import { readSharedToken } from '../utils/sharedToken';

const MAX_BARS = 10;

export default function TelevisionPage() {
  const { user, has } = useAuth();
  const [rows, setRows] = useState<TvAgentSales[]>([]);
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null);
  const [flash, setFlash] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const flashTimeoutRef = useRef<number | null>(null);

  const authorized = has('tv.view');

  const refresh = useCallback(async () => {
    try {
      const data = await tvApi.getSalesByAgentToday();
      setRows(data);
      setLastUpdate(new Date());
      setError(null);
    } catch {
      setError('No se pudo cargar el reporte de ventas.');
    }
  }, []);

  useEffect(() => {
    if (!authorized) return;
    void refresh();
    const id = window.setInterval(refresh, 30_000);
    return () => window.clearInterval(id);
  }, [authorized, refresh]);

  useEffect(() => {
    if (!authorized) return;
    const token = readSharedToken();
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/dashboard', { accessTokenFactory: () => token ?? '' })
      .build();
    connection.on('BroadcastTvSaleAsync', (salesRep: string) => {
      void refresh();
      setFlash(salesRep);
      if (flashTimeoutRef.current) window.clearTimeout(flashTimeoutRef.current);
      flashTimeoutRef.current = window.setTimeout(() => setFlash(null), 4000);
    });
    void connection.start();
    return () => {
      void connection.stop();
      if (flashTimeoutRef.current) window.clearTimeout(flashTimeoutRef.current);
    };
  }, [authorized, refresh]);

  const totalCount = useMemo(() => rows.reduce((acc, r) => acc + r.count, 0), [rows]);
  const totalAmount = useMemo(() => rows.reduce((acc, r) => acc + r.amount, 0), [rows]);
  const topCount = rows[0]?.count ?? 0;
  const visible = rows.slice(0, MAX_BARS);

  if (!authorized) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-canvas-white text-on-surface p-8">
        <div className="max-w-md text-center">
          <p className="text-secondary">No tienes acceso a esta vista.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-canvas-white text-on-surface p-8 font-body-md">
      <div className="max-w-6xl mx-auto">
        <header className="flex items-baseline justify-between mb-8">
          <div>
            <h1 className="font-display-hero text-display-hero text-primary">Ventas del día</h1>
            <p className="text-secondary mt-1">
              Actualizado en vivo · {lastUpdate ? lastUpdate.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '—'}
            </p>
          </div>
          <div className="flex gap-6 text-right">
            <div>
              <p className="font-metadata-mono text-xs uppercase tracking-wider text-secondary">Ventas</p>
              <p className="font-display-hero text-3xl text-electric-blue">{totalCount}</p>
            </div>
            <div>
              <p className="font-metadata-mono text-xs uppercase tracking-wider text-secondary">Monto</p>
              <p className="font-display-hero text-3xl text-emerald-signal">${totalAmount.toFixed(0)}</p>
            </div>
          </div>
        </header>

        {error && (
          <div className="mb-6 px-4 py-3 rounded-lg bg-deep-rose/10 text-deep-rose text-sm">{error}</div>
        )}

        <div className="space-y-4">
          {visible.length === 0 && (
            <p className="text-secondary text-center py-16">Aún no hay ventas hoy.</p>
          )}
          {visible.map((row) => {
            const pct = topCount > 0 ? Math.round((row.count / topCount) * 100) : 0;
            const highlight = flash && row.salesRep === flash;
            return (
              <div
                key={row.salesRep}
                className={`flex items-center gap-4 p-4 rounded-xl border border-whisper-border transition-all ${
                  highlight ? 'bg-electric-blue/10 ring-2 ring-electric-blue' : 'bg-pure-surface'
                }`}
              >
                <div className="w-48 shrink-0 truncate font-display-hero text-xl text-primary">{row.salesRep}</div>
                <div className="flex-1 h-10 rounded-full bg-card-icon-bg overflow-hidden">
                  <div
                    className="h-full bg-electric-blue transition-all"
                    style={{ width: `${pct}%` }}
                  />
                </div>
                <div className="w-20 text-right font-display-hero text-2xl text-primary">{row.count}</div>
              </div>
            );
          })}
        </div>

        <footer className="mt-10 flex items-center justify-between text-secondary text-sm">
          <span>Mostrando top {visible.length} de {rows.length} agentes</span>
          <span className="font-metadata-mono">{user?.fullName ?? user?.email}</span>
        </footer>
      </div>
    </div>
  );
}