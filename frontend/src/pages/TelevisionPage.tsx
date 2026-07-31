import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import { getAgentPerformanceWithSales } from '../services/api';
import { listAllVicidialSales } from '../services/vicidialFormApi';
import { readSharedToken } from '../utils/sharedToken';
import type { ReportDto, TimeFilterDto, VicidialSaleDto } from '../types';

type Period = 'Today' | 'Week' | 'Month' | 'Custom';
const PERIOD_API: Record<Period, string> = { Today: 'Today', Week: 'ThisWeek', Month: 'ThisMonth', Custom: 'Custom' };
const TICKER_LIMIT = 30;
const TOP_TABLE_LIMIT = 5;

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

export default function TelevisionPage() {
  const { has } = useAuth();
  const [period, setPeriod] = useState<Period>('Today');
  const [customStart, setCustomStart] = useState(todayIso);
  const [customEnd, setCustomEnd] = useState(todayIso);
  const [report, setReport] = useState<ReportDto | null>(null);
  const [recentSales, setRecentSales] = useState<VicidialSaleDto[]>([]);
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
      const f = filter();
      const salesRange = buildVicidialRange(f);
      const [data, sales] = await Promise.all([
        getAgentPerformanceWithSales(f),
        listAllVicidialSales(salesRange.from, salesRange.to, 500).catch(() => []),
      ]);
      setReport(data);
      setRecentSales(sales);
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
    connection.on('BroadcastTvSaleAsync', (salesRep: string, _bundle: string, _amount: number) => {
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

  const podium = useMemo(() => sortedAgents.slice(0, 3), [sortedAgents]);
  const topTable = useMemo(() => sortedAgents.slice(0, TOP_TABLE_LIMIT), [sortedAgents]);

  const topBundle = useMemo(() => {
    if (recentSales.length === 0) return null;
    const counts = new Map<string, { count: number; revenue: number }>();
    for (const s of recentSales) {
      const key = s.bundle || 'Sin bundle';
      const prev = counts.get(key) ?? { count: 0, revenue: 0 };
      counts.set(key, { count: prev.count + 1, revenue: prev.revenue + Number(s.amount) });
    }
    let topKey = '';
    let topVal = { count: 0, revenue: 0 };
    for (const [k, v] of counts) {
      if (v.count > topVal.count) { topKey = k; topVal = v; }
    }
    return topKey ? { name: topKey, count: topVal.count, revenue: topVal.revenue } : null;
  }, [recentSales]);

  const tickerSales = useMemo(() => recentSales.slice(0, TICKER_LIMIT), [recentSales]);

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

      {loading && podium.length === 0 ? (
        <div className="h-64 bg-surface-container dark:bg-gray-800 rounded-xl animate-pulse" />
      ) : podium.length >= 1 ? (
        <Podium podium={podium} />
      ) : (
        <div className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl p-12 text-center text-muted-slate text-sm">
          <span className="material-symbols-outlined text-4xl text-muted-slate/40 block mb-2">leaderboard</span>
          No hay ventas registradas en este periodo.
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        <section className="lg:col-span-8 bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden">
          <div className="p-6 border-b border-whisper-border dark:border-gray-700 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
            <div>
              <h3 className="font-bold text-lg text-primary dark:text-white">Top Sellers</h3>
              <p className="text-[11px] text-secondary dark:text-gray-400 mt-0.5 font-metadata-mono uppercase tracking-wider">
                Updates automatically when a sale is registered
              </p>
            </div>
            <span className="material-symbols-outlined text-electric-blue text-2xl">leaderboard</span>
          </div>
          {topTable.length > 0 ? (
            <table className="w-full text-left text-sm border-collapse table-fixed">
              <thead className="text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800">
                <tr>
                  <th className="p-3 font-medium w-12">#</th>
                  <th className="p-3 font-medium">Agent</th>
                  <th className="p-3 font-medium text-right">Form Sales</th>
                  <th className="p-3 font-medium text-right">Form Revenue</th>
                  <th className="p-3 font-medium text-right">VICI Sales</th>
                  <th className="p-3 font-medium text-right w-24">Conv %</th>
                </tr>
              </thead>
              <tbody>
                {topTable.map((agent, i) => {
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
                      <td className="p-3 font-medium text-primary dark:text-white truncate">{agent.name}</td>
                      <td className="p-3 font-metadata-mono text-emerald-signal font-semibold text-right">{agent.formSales}</td>
                      <td className="p-3 font-metadata-mono text-emerald-signal font-medium text-right">{formatCurrency(agent.formRevenue)}</td>
                      <td className="p-3 font-metadata-mono text-primary dark:text-gray-200 text-right">{agent.vicidialSales}</td>
                      <td className="p-3 font-metadata-mono font-medium text-right">
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

        <section className="lg:col-span-4 bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden flex flex-col">
          <div className="p-6 border-b border-whisper-border dark:border-gray-700 flex justify-between items-center">
            <div>
              <h3 className="font-bold text-lg text-primary dark:text-white">Top Bundle</h3>
              <p className="text-[11px] text-secondary dark:text-gray-400 mt-0.5 font-metadata-mono uppercase tracking-wider">
                Producto estrella del periodo
              </p>
            </div>
            <span className="material-symbols-outlined text-amber-warmth text-2xl" style={{ fontVariationSettings: "'FILL' 1" }}>
              star
            </span>
          </div>
          <div className="p-6 flex-1 flex flex-col items-center justify-center text-center gap-3">
            {topBundle ? (
              <>
                <div className="px-4 py-2 rounded-full bg-amber-warmth/15 border border-amber-warmth/30">
                  <span className="font-metadata-mono text-[11px] uppercase tracking-wider font-bold text-amber-warmth">
                    #{1} Bundle
                  </span>
                </div>
                <h4 className="font-headline-lg text-xl text-primary dark:text-white leading-tight">
                  {topBundle.name}
                </h4>
                <div className="grid grid-cols-2 gap-4 w-full pt-3 border-t border-whisper-border dark:border-gray-700">
                  <div>
                    <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">Vendidos</p>
                    <p className="font-headline-lg text-2xl font-bold text-emerald-signal font-metadata-mono mt-1">
                      {topBundle.count}
                    </p>
                  </div>
                  <div>
                    <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">Revenue</p>
                    <p className="font-headline-lg text-2xl font-bold text-emerald-signal font-metadata-mono mt-1">
                      {formatCurrency(topBundle.revenue)}
                    </p>
                  </div>
                </div>
              </>
            ) : (
              <div className="text-muted-slate text-sm flex flex-col items-center gap-2">
                <span className="material-symbols-outlined text-4xl text-muted-slate/40">inventory_2</span>
                Sin ventas en este periodo
              </div>
            )}
          </div>
        </section>
      </div>

      <Ticker sales={tickerSales} />
    </>
  );
}

function Podium({ podium }: { podium: AgentRow[] }) {
  const first = podium[0];
  const second = podium[1];
  const third = podium[2];
  return (
    <section className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden">
      <div className="p-6 border-b border-whisper-border dark:border-gray-700">
        <h3 className="font-bold text-lg text-primary dark:text-white">Podium</h3>
        <p className="text-[11px] text-secondary dark:text-gray-400 mt-0.5 font-metadata-mono uppercase tracking-wider">
          Top 3 vendedores del periodo
        </p>
      </div>
      <div className="p-8 grid grid-cols-3 gap-4 items-end">
        <PodiumColumn agent={third} rank={3} tone="bronze" heightClass="h-32" />
        <PodiumColumn agent={first} rank={1} tone="gold" heightClass="h-48" />
        <PodiumColumn agent={second} rank={2} tone="silver" heightClass="h-40" />
      </div>
    </section>
  );
}

function PodiumColumn({ agent, rank, tone, heightClass }: {
  agent: AgentRow | undefined;
  rank: number;
  tone: 'gold' | 'silver' | 'bronze';
  heightClass: string;
}) {
  const tones: Record<typeof tone, { medal: string; block: string; ring: string; text: string }> = {
    gold: {
      medal: 'bg-amber-warmth/15 text-amber-warmth border-amber-warmth/40',
      block: 'bg-gradient-to-b from-amber-warmth/30 to-amber-warmth/10 border-amber-warmth/40',
      ring: 'ring-amber-warmth/30',
      text: 'text-amber-warmth',
    },
    silver: {
      medal: 'bg-gray-400/15 text-gray-400 border-gray-400/40',
      block: 'bg-gradient-to-b from-gray-400/25 to-gray-400/5 border-gray-400/40',
      ring: 'ring-gray-400/30',
      text: 'text-gray-400',
    },
    bronze: {
      medal: 'bg-orange-700/15 text-orange-600 dark:text-orange-500 border-orange-700/40',
      block: 'bg-gradient-to-b from-orange-700/25 to-orange-700/5 border-orange-700/40',
      ring: 'ring-orange-700/30',
      text: 'text-orange-600 dark:text-orange-500',
    },
  };
  const t = tones[tone];
  if (!agent) {
    return <div className={`${heightClass} rounded-xl border border-dashed border-whisper-border dark:border-gray-700 flex items-center justify-center text-muted-slate text-xs`}>—</div>;
  }
  return (
    <div className="flex flex-col items-center gap-3">
      <div className={`inline-flex items-center justify-center w-12 h-12 rounded-full border-2 ${t.medal} ${t.text} font-headline-lg font-bold text-lg shadow-card`}>
        {rank}
      </div>
      <div className={`w-full ${heightClass} rounded-xl border-2 ${t.block} flex flex-col items-center justify-center p-4 text-center shadow-card`}>
        <span className={`font-metadata-mono text-[10px] uppercase tracking-wider font-bold ${t.text}`}>
          #{rank}
        </span>
        <p className="font-bold text-base text-primary dark:text-white mt-1 truncate w-full">
          {agent.name}
        </p>
        <p className="font-metadata-mono text-2xl font-bold text-emerald-signal mt-2">
          {agent.formSales}
        </p>
        <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">Ventas</p>
        <p className="font-metadata-mono text-sm text-emerald-signal font-medium mt-1">
          {formatCurrency(agent.formRevenue)}
        </p>
      </div>
      <div className={`w-full rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 px-3 py-2 text-center`}>
        <p className="font-metadata-mono text-[10px] uppercase tracking-wider text-secondary dark:text-gray-400">Conv %</p>
        <p className="font-metadata-mono text-sm font-bold text-primary dark:text-white">
          {agent.conversion ? `${agent.conversion.toFixed(1)}%` : '--'}
        </p>
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

function Ticker({ sales }: { sales: VicidialSaleDto[] }) {
  if (sales.length === 0) return null;
  const items = sales.map((s, idx) => ({
    id: `${s.salesRep}-${idx}-${s.saleDate}`,
    text: `${s.salesRep} cerro ${s.bundle || 'un paquete'} por ${formatCurrency(Number(s.amount))}`,
  }));
  const looped = [...items, ...items];
  return (
    <section className="relative overflow-hidden bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused">
      <div className="p-4 border-b border-whisper-border dark:border-gray-700 flex items-center gap-2">
        <span className="material-symbols-outlined text-emerald-signal" style={{ fontSize: '20px', fontVariationSettings: "'FILL' 1" }}>
          bolt
        </span>
        <h3 className="font-bold text-sm text-primary dark:text-white uppercase tracking-wider font-metadata-mono">
          Live Sales Ticker
        </h3>
        <span className="ml-2 px-2 py-0.5 rounded-full bg-emerald-signal/15 text-emerald-signal font-metadata-mono text-[10px] uppercase tracking-wider font-bold">
          en vivo
        </span>
      </div>
      <div className="relative h-12 overflow-hidden">
        <div className="absolute inset-0 flex items-center">
          <div className="flex gap-8 animate-ticker whitespace-nowrap">
            {looped.map((item, i) => (
              <span key={`${item.id}-${i}`} className="font-metadata-mono text-sm text-primary dark:text-gray-200 flex items-center gap-2">
                <span className="material-symbols-outlined text-emerald-signal" style={{ fontSize: '16px', fontVariationSettings: "'FILL' 1" }}>
                  check_circle
                </span>
                <span className="font-bold">{item.text}</span>
              </span>
            ))}
          </div>
        </div>
        <div className="pointer-events-none absolute inset-y-0 left-0 w-16 bg-gradient-to-r from-pure-surface dark:from-gray-900 to-transparent" />
        <div className="pointer-events-none absolute inset-y-0 right-0 w-16 bg-gradient-to-l from-pure-surface dark:from-gray-900 to-transparent" />
      </div>
      <style>{`
        @keyframes ticker-scroll {
          0% { transform: translateX(0); }
          100% { transform: translateX(-50%); }
        }
        .animate-ticker {
          animation: ticker-scroll 60s linear infinite;
        }
      `}</style>
    </section>
  );
}

function buildVicidialRange(filter: TimeFilterDto): { from?: string; to?: string } {
  if (filter.period === 'Custom' && filter.customStart && filter.customEnd) {
    return { from: `${filter.customStart} 00:00:00`, to: `${filter.customEnd} 23:59:59` };
  }
  const tz = 'America/Tijuana';
  const now = new Date();
  const fmt = new Intl.DateTimeFormat('en-CA', { timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit' });
  const today = fmt.format(now);
  const dateAt = (offsetDays: number): string => {
    const d = new Date(now.getTime() + offsetDays * 86400000);
    return fmt.format(d);
  };
  switch (filter.period) {
    case 'Today':
      return { from: `${today} 00:00:00`, to: `${today} 23:59:59` };
    case 'Week': {
      const parts = today.split('-');
      const y = Number(parts[0]);
      const m = Number(parts[1]);
      const d = Number(parts[2]);
      const cur = new Date(y, m - 1, d);
      const dow = cur.getDay();
      const daysSinceMonday = dow === 0 ? 6 : dow - 1;
      const monday = new Date(cur.getTime() - daysSinceMonday * 86400000);
      const fmt2 = new Intl.DateTimeFormat('en-CA', { timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit' });
      return { from: `${fmt2.format(monday)} 00:00:00`, to: `${today} 23:59:59` };
    }
    case 'Month': {
      const parts = today.split('-');
      const y = Number(parts[0]);
      const m = Number(parts[1]);
      const firstOfMonth = `${y}-${String(m).padStart(2, '0')}-01`;
      return { from: `${firstOfMonth} 00:00:00`, to: `${today} 23:59:59` };
    }
    default:
      return {};
  }
}