import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import { getAgentPerformanceWithSales } from '../services/api';
import { readSharedToken } from '../utils/sharedToken';
import type { ReportDto, TimeFilterDto } from '../types';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';
const PERIOD_API: Record<Period, string> = { Today: 'Today', Week: 'ThisWeek', Month: 'ThisMonth', Custom: 'Custom' };

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency', currency: 'USD', minimumFractionDigits: 0, maximumFractionDigits: 0,
  }).format(amount);
}

function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

interface AgentRow {
  name: string;
  formSales: number;
  formRevenue: number;
  vicidialSales: number;
  callsHandled: number;
  contacts: number;
  conversion: number;
}

function toAgentRow(r: Record<string, unknown>): AgentRow {
  return {
    name: String(r.Name ?? r.User ?? ''),
    formSales: Number(r.Form_Sales_Count ?? 0),
    formRevenue: Number(r.Form_Sales_Amount ?? 0),
    vicidialSales: Number(r.Sales_Made ?? 0),
    callsHandled: Number(r.Calls_Handled ?? 0),
    contacts: Number(r.Contacts ?? 0),
    conversion: Number(r.Conversion_Percentage ?? 0),
  };
}

const TOP_N = 5;

export default function TelevisionPage() {
  const { has } = useAuth();
  const [period, setPeriod] = useState<Period>('Today');
  const [customStart, setCustomStart] = useState(todayIso);
  const [customEnd, setCustomEnd] = useState(todayIso);
  const [report, setReport] = useState<ReportDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string>('');
  const [flash, setFlash] = useState<string | null>(null);
  const flashTimeoutRef = useRef<number | null>(null);
  const authorized = has('tv.view');

  const filter = useCallback((): TimeFilterDto => {
    if (period === 'Custom') {
      return { period: PERIOD_API[period], customStart: `${customStart}T00:00:00`, customEnd: `${customEnd}T23:59:59` };
    }
    return { period: PERIOD_API[period] };
  }, [period, customStart, customEnd]);

  const refresh = useCallback(async (manual = false) => {
    if (manual) setRefreshing(true);
    else setLoading(true);
    try {
      const data = await getAgentPerformanceWithSales(filter());
      setReport(data);
      setLastUpdated(new Date().toLocaleTimeString());
      setError(null);
    } catch {
      setError('Failed to load sales leaderboard');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [filter]);

  useEffect(() => {
    if (!authorized) return;
    void refresh();
    const id = window.setInterval(() => void refresh(), 30_000);
    return () => window.clearInterval(id);
  }, [authorized, refresh]);

  useEffect(() => {
    if (!authorized) return;
    const token = readSharedToken();
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/dashboard', { accessTokenFactory: () => token ?? '' })
      .build();
    connection.on('BroadcastTvSaleAsync', (salesRep: string) => {
      void refresh(true);
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

  const sortedAgents = useMemo<AgentRow[]>(() => {
    const rows = report?.rows ?? [];
    return rows
      .map(toAgentRow)
      .filter((a) => a.formSales > 0 || a.vicidialSales > 0 || a.callsHandled > 0)
      .sort((a, b) => b.formSales - a.formSales || a.name.localeCompare(b.name));
  }, [report]);

  const topSellers = useMemo(() => sortedAgents.slice(0, TOP_N), [sortedAgents]);
  const bestSeller = topSellers[0];

  if (!authorized) {
    return (
      <div className="min-h-[60vh] flex items-center justify-center bg-canvas-white dark:bg-gray-950">
        <div className="max-w-md text-center">
          <span className="material-symbols-outlined text-muted-slate text-5xl mb-3 block">lock</span>
          <p className="text-secondary dark:text-gray-400 text-sm">No tienes acceso a esta vista.</p>
        </div>
      </div>
    );
  }

  const periodBtn = (p: Period) => (
    <button
      key={p}
      onClick={() => setPeriod(p)}
      className={`px-4 py-1.5 text-sm border-r border-whisper-border dark:border-gray-700 last:border-r-0 ${
        period === p
          ? 'bg-pure-surface dark:bg-gray-800 text-primary dark:text-white font-medium'
          : 'text-secondary dark:text-gray-400 hover:bg-surface-container transition-colors'
      }`}
    >
      {p}
    </button>
  );

  return (
    <>
      <div className="flex flex-col md:flex-row justify-between items-start md:items-end gap-4 border-b border-whisper-border dark:border-gray-700 pb-4">
        <div>
          <h1 className="font-headline-lg text-headline-lg text-primary dark:text-white tracking-tight">
            TV — Sales Leaderboard
          </h1>
          <p className="text-secondary dark:text-gray-400 mt-1 flex items-center gap-2 text-sm">
            <span className="w-2 h-2 rounded-full bg-emerald-signal" />
            <span>
              Live from Analytics{lastUpdated && ` • Last updated: ${lastUpdated}`}
            </span>
          </p>
        </div>
        <div className="flex flex-col gap-2 items-end">
          <div className="flex gap-2 flex-wrap items-center">
            <div className="bg-surface-container-low dark:bg-gray-800 border border-whisper-border dark:border-gray-700 rounded flex text-sm overflow-hidden">
              {periodBtn('Today')}
              {periodBtn('Week')}
              {periodBtn('Month')}
              {periodBtn('Custom')}
            </div>
            {period === 'Custom' && (
              <div className="flex gap-2 items-center bg-surface-container-low dark:bg-gray-800 border border-whisper-border dark:border-gray-700 rounded px-3 py-1">
                <input
                  type="date"
                  value={customStart}
                  onChange={(e) => setCustomStart(e.target.value)}
                  className="text-xs text-primary dark:text-white bg-transparent border-none outline-none w-[120px]"
                />
                <span className="text-muted-slate text-xs">to</span>
                <input
                  type="date"
                  value={customEnd}
                  onChange={(e) => setCustomEnd(e.target.value)}
                  className="text-xs text-primary dark:text-white bg-transparent border-none outline-none w-[120px]"
                />
              </div>
            )}
            <button
              onClick={() => void refresh(true)}
              disabled={refreshing || loading}
              className="flex items-center gap-2 px-3 py-1.5 border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-secondary dark:text-gray-300 hover:text-primary dark:hover:text-white transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed"
              title="Refresh sales leaderboard"
            >
              <span className={`material-symbols-outlined text-[20px] ${refreshing ? 'animate-spin' : ''}`}>sync</span>
              <span>{refreshing ? 'Refreshing...' : 'Refresh'}</span>
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

      {loading && !bestSeller ? (
        <div className="h-48 bg-surface-container dark:bg-gray-800 rounded-xl animate-pulse" />
      ) : bestSeller ? (
        <BestSellerCard agent={bestSeller} />
      ) : (
        <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl p-12 text-center text-muted-slate text-sm">
          <span className="material-symbols-outlined text-4xl text-muted-slate/40 block mb-2">leaderboard</span>
          No hay ventas registradas en este periodo.
        </div>
      )}

      <section className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden">
        <div className="p-6 border-b border-whisper-border dark:border-gray-700 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
          <div>
            <h3 className="font-bold text-lg text-primary dark:text-white">
              Top {TOP_N} Sellers
            </h3>
            <p className="text-[11px] text-secondary dark:text-gray-400 mt-0.5 font-metadata-mono uppercase tracking-wider">
              Updates automatically when a sale is registered
            </p>
          </div>
          <span className="material-symbols-outlined text-electric-blue text-2xl">leaderboard</span>
        </div>
        {loading && topSellers.length === 0 ? (
          <div className="p-6 space-y-3 animate-pulse">
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="h-9 bg-surface-container dark:bg-gray-800 rounded" />
            ))}
          </div>
        ) : topSellers.length > 0 ? (
          <table className="w-full text-left text-sm border-collapse">
            <thead className="text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800">
              <tr>
                <th className="p-3 font-medium">#</th>
                <th className="p-3 font-medium">Agent</th>
                <th className="p-3 font-medium text-right">Form Sales</th>
                <th className="p-3 font-medium text-right">Form Revenue</th>
                <th className="p-3 font-medium text-right">VICI Sales</th>
                <th className="p-3 font-medium text-right">Conv %</th>
              </tr>
            </thead>
            <tbody>
              {topSellers.map((agent, i) => {
                const rank = i + 1;
                const highlight = flash && agent.name === flash;
                return (
                  <tr
                    key={agent.name}
                    className={`border-b border-whisper-border dark:border-gray-700 transition-colors ${
                      highlight ? 'bg-electric-blue/10 dark:bg-electric-blue/15' : 'hover:bg-surface-container-lowest dark:hover:bg-gray-800/50'
                    }`}
                  >
                    <td className="p-3 font-metadata-mono">
                      <RankBadge rank={rank} />
                    </td>
                    <td className="p-3 font-medium text-primary dark:text-white">{agent.name}</td>
                    <td className="p-3 font-metadata-mono text-emerald-signal font-semibold">{agent.formSales}</td>
                    <td className="p-3 font-metadata-mono text-emerald-signal font-medium">{formatCurrency(agent.formRevenue)}</td>
                    <td className="p-3 font-metadata-mono text-primary dark:text-gray-200">{agent.vicidialSales}</td>
                    <td className="p-3 font-metadata-mono font-medium">
                      {agent.conversion ? `${agent.conversion.toFixed(1)}%` : '--'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        ) : (
          <div className="p-12 text-sm text-muted-slate text-center">
            Aun no hay datos de ventas.
          </div>
        )}
      </section>
    </>
  );
}

function BestSellerCard({ agent }: { agent: AgentRow }) {
  return (
    <div className="relative overflow-hidden bg-gradient-to-br from-electric-blue/15 via-electric-blue/5 to-transparent border-2 border-electric-blue/30 dark:border-electric-blue/40 rounded-xl p-6 shadow-card">
      <div className="absolute -right-8 -top-8 w-32 h-32 rounded-full bg-electric-blue/10 blur-3xl pointer-events-none" />
      <div className="relative flex flex-col md:flex-row md:items-center md:justify-between gap-6">
        <div className="flex items-center gap-4">
          <div className="p-3 bg-electric-blue/15 dark:bg-electric-blue/20 rounded-xl">
            <span className="material-symbols-outlined text-electric-blue" style={{ fontSize: '32px', fontVariationSettings: "'FILL' 1" }}>
              emoji_events
            </span>
          </div>
          <div>
            <p className="font-metadata-mono text-[11px] uppercase tracking-wider font-bold text-electric-blue">
              Best Seller
            </p>
            <h2 className="font-headline-lg text-headline-md text-primary dark:text-white leading-tight mt-1">
              {agent.name}
            </h2>
          </div>
        </div>
        <div className="grid grid-cols-3 gap-6">
          <div>
            <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">Form Sales</p>
            <p className="font-headline-lg text-2xl font-bold text-emerald-signal font-metadata-mono mt-1">
              {agent.formSales}
            </p>
          </div>
          <div>
            <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">Form Revenue</p>
            <p className="font-headline-lg text-2xl font-bold text-emerald-signal font-metadata-mono mt-1">
              {formatCurrency(agent.formRevenue)}
            </p>
          </div>
          <div>
            <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">VICI Sales</p>
            <p className="font-headline-lg text-2xl font-bold text-primary dark:text-white font-metadata-mono mt-1">
              {agent.vicidialSales}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}

function RankBadge({ rank }: { rank: number }) {
  const styles: Record<number, string> = {
    1: 'bg-amber-warmth/15 text-amber-warmth border-amber-warmth/30',
    2: 'bg-gray-400/15 text-gray-500 dark:text-gray-300 border-gray-400/30',
    3: 'bg-orange-700/15 text-orange-700 dark:text-orange-500 border-orange-700/30',
  };
  const cls = styles[rank] ?? 'bg-surface-container text-secondary border-whisper-border';
  return (
    <span className={`inline-flex items-center justify-center w-7 h-7 rounded-full border font-metadata-mono text-xs font-bold ${cls}`}>
      {rank}
    </span>
  );
}