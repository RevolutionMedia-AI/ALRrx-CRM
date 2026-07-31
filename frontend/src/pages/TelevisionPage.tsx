import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import {
  FlashIcon,
  Tick01Icon,
  StarIcon,
  RefreshIcon,
  RankingIcon,
  PackageIcon,
} from 'hugeicons-react';
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

const TITLE = 'text-black dark:text-white font-bold';
const BODY = 'text-black dark:text-white';
const MUTED = 'text-black/70 dark:text-white/80';

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
          <p className={`${BODY}`}>No tienes acceso a esta vista.</p>
        </div>
      </div>
    );
  }

  const periodBtn = (p: Period) => (
    <button
      key={p}
      onClick={() => setPeriod(p)}
      className={`px-4 py-1.5 text-sm border-r border-whisper-border dark:border-gray-700 last:border-r-0 ${TITLE} ${
        period === p
          ? 'bg-pure-surface dark:bg-gray-800'
          : 'hover:bg-surface-container transition-colors'
      }`}
    >
      {p}
    </button>
  );

  return (
    <>
      <div className="flex flex-col md:flex-row justify-between items-start md:items-end gap-4 border-b border-whisper-border dark:border-gray-700 pb-4">
        <div>
          <h1 className={`font-headline-lg text-headline-lg tracking-tight ${TITLE}`}>
            TV — Sales Leaderboard
          </h1>
          <p className={`mt-1 flex items-center gap-2 text-sm ${TITLE}`}>
            <span className="w-2 h-2 rounded-full bg-emerald-signal" />
            <span>
              Live from Analytics{lastUpdated && ` • Last updated: ${lastUpdated}`}
            </span>
          </p>
        </div>
        <div className="flex flex-col gap-2 items-end">
          <div className="flex gap-2 flex-wrap items-center">
            <div className={`bg-surface-container-low dark:bg-gray-800 border border-whisper-border dark:border-gray-700 rounded flex text-sm overflow-hidden ${TITLE}`}>
              {periodBtn('Today')}
              {periodBtn('Week')}
              {periodBtn('Month')}
              {periodBtn('Custom')}
            </div>
            {period === 'Custom' && (
              <div className={`flex gap-2 items-center bg-surface-container-low dark:bg-gray-800 border border-whisper-border dark:border-gray-700 rounded px-3 py-1 ${BODY}`}>
                <input
                  type="date"
                  value={customStart}
                  onChange={(e) => setCustomStart(e.target.value)}
                  className={`text-xs bg-transparent border-none outline-none w-[120px] ${BODY}`}
                />
                <span className="text-xs">to</span>
                <input
                  type="date"
                  value={customEnd}
                  onChange={(e) => setCustomEnd(e.target.value)}
                  className={`text-xs bg-transparent border-none outline-none w-[120px] ${BODY}`}
                />
              </div>
            )}
            <button
              onClick={() => void refresh(true)}
              disabled={refreshing || loading}
              className={`flex items-center gap-2 px-3 py-1.5 border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed ${TITLE}`}
              title="Refresh sales leaderboard"
            >
              <RefreshIcon size={18} className={refreshing ? 'animate-spin' : ''} />
              <span>{refreshing ? 'Refreshing...' : 'Refresh'}</span>
            </button>
          </div>
        </div>
      </div>

      <Ticker sales={tickerSales} />

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2 font-bold">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      {loading && podium.length === 0 ? (
        <div className="h-64 bg-surface-container dark:bg-gray-800 rounded-xl animate-pulse" />
      ) : podium.length >= 1 ? (
        <Podium podium={podium} />
      ) : (
        <div className={`bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl p-12 text-center text-sm ${BODY}`}>
          <RankingIcon size={48} className="mx-auto mb-2 opacity-40" />
          No hay ventas registradas en este periodo.
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        <section className="lg:col-span-8 bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden">
          <div className="p-6 border-b border-whisper-border dark:border-gray-700">
            <h3 className={`font-headline-md text-lg ${TITLE}`}>Top Sellers</h3>
            <p className={`text-[11px] mt-0.5 font-metadata-mono uppercase tracking-wider ${MUTED}`}>
              Updates automatically when a sale is registered
            </p>
          </div>
          {topTable.length > 0 ? (
            <table className="w-full text-left text-sm border-collapse table-fixed">
              <thead className={`text-xs uppercase tracking-wider font-metadata-mono bg-surface-container-low dark:bg-gray-800 ${TITLE}`}>
                <tr>
                  <th className="p-3 w-12">#</th>
                  <th className="p-3">Agent</th>
                  <th className="p-3 text-right">Form Sales</th>
                  <th className="p-3 text-right">Form Revenue</th>
                  <th className="p-3 text-right">VICI Sales</th>
                  <th className="p-3 text-right w-24">Conv %</th>
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
                      <td className="p-3">
                        <RankBadge rank={rank} />
                      </td>
                      <td className={`p-3 font-medium truncate ${BODY}`}>{agent.name}</td>
                      <td className={`p-3 font-metadata-mono font-bold text-right text-emerald-signal`}>{agent.formSales}</td>
                      <td className={`p-3 font-metadata-mono font-bold text-right text-emerald-signal`}>{formatCurrency(agent.formRevenue)}</td>
                      <td className={`p-3 font-metadata-mono text-right ${BODY}`}>{agent.vicidialSales}</td>
                      <td className={`p-3 font-metadata-mono font-bold text-right ${BODY}`}>
                        {agent.conversion ? `${agent.conversion.toFixed(1)}%` : '--'}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          ) : (
            <div className={`p-12 text-sm text-center ${BODY}`}>
              Aun no hay datos de ventas.
            </div>
          )}
        </section>

        <section className="lg:col-span-4 bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden flex flex-col">
          <div className="p-6 border-b border-whisper-border dark:border-gray-700 flex justify-between items-center">
            <div>
              <h3 className={`font-headline-md text-lg ${TITLE}`}>Top Bundle</h3>
              <p className={`text-[11px] mt-0.5 font-metadata-mono uppercase tracking-wider ${MUTED}`}>
                Producto estrella del periodo
              </p>
            </div>
            <StarIcon size={28} className="text-amber-warmth" />
          </div>
          <div className="p-6 flex-1 flex flex-col items-center justify-center text-center gap-3">
            {topBundle ? (
              <>
                <div className="px-4 py-2 rounded-full bg-amber-warmth/15 border border-amber-warmth/30">
                  <span className={`font-metadata-mono text-[11px] uppercase tracking-wider font-bold text-amber-warmth`}>
                    #1 Bundle
                  </span>
                </div>
                <h4 className={`font-headline-lg text-xl leading-tight ${TITLE}`}>
                  {topBundle.name}
                </h4>
                <div className="grid grid-cols-2 gap-4 w-full pt-3 border-t border-whisper-border dark:border-gray-700">
                  <div>
                    <p className={`font-metadata-mono text-[10px] uppercase tracking-wider ${MUTED}`}>Vendidos</p>
                    <p className="font-headline-lg text-2xl font-bold text-emerald-signal font-metadata-mono mt-1">
                      {topBundle.count}
                    </p>
                  </div>
                  <div>
                    <p className={`font-metadata-mono text-[10px] uppercase tracking-wider ${MUTED}`}>Revenue</p>
                    <p className="font-headline-lg text-2xl font-bold text-emerald-signal font-metadata-mono mt-1">
                      {formatCurrency(topBundle.revenue)}
                    </p>
                  </div>
                </div>
              </>
            ) : (
              <div className={`text-sm flex flex-col items-center gap-2 ${BODY}`}>
                <PackageIcon size={48} className="opacity-40" />
                Sin ventas en este periodo
              </div>
            )}
          </div>
        </section>
      </div>
    </>
  );
}

function Podium({ podium }: { podium: AgentRow[] }) {
  const first = podium[0];
  const second = podium[1];
  const third = podium[2];
  return (
    <section className="bg-pure-surface dark:bg-gray-900 border border-whisper-border dark:border-gray-700 rounded-xl shadow-diffused overflow-hidden">
      <div className="p-6 border-b border-whisper-border dark:border-gray-700 flex justify-between items-start">
        <div>
          <h3 className={`font-headline-md text-lg ${TITLE}`}>Podium</h3>
          <p className={`text-[11px] mt-0.5 font-metadata-mono uppercase tracking-wider ${MUTED}`}>
            Top 3 vendedores del periodo
          </p>
        </div>
        <RankingIcon size={28} className="text-amber-warmth" />
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
  const tones: Record<typeof tone, { medalBg: string; medalBorder: string; medalText: string; blockBorder: string; accent: string }> = {
    gold: {
      medalBg: 'bg-amber-warmth/20',
      medalBorder: 'border-amber-warmth',
      medalText: 'text-amber-warmth',
      blockBorder: 'border-amber-warmth',
      accent: 'text-amber-warmth',
    },
    silver: {
      medalBg: 'bg-cyan-500/15 dark:bg-cyan-500/20',
      medalBorder: 'border-cyan-500',
      medalText: 'text-cyan-600 dark:text-cyan-400',
      blockBorder: 'border-cyan-500',
      accent: 'text-cyan-600 dark:text-cyan-400',
    },
    bronze: {
      medalBg: 'bg-orange-600/15 dark:bg-orange-600/20',
      medalBorder: 'border-orange-600',
      medalText: 'text-orange-700 dark:text-orange-400',
      blockBorder: 'border-orange-600',
      accent: 'text-orange-700 dark:text-orange-400',
    },
  };
  const t = tones[tone];
  if (!agent) {
    return (
      <div className={`${heightClass} rounded-xl border-2 border-dashed border-whisper-border dark:border-gray-700 flex items-center justify-center ${MUTED} text-xs`}>—</div>
    );
  }
  return (
    <div className="flex flex-col items-center gap-3">
      <div className={`inline-flex items-center justify-center w-14 h-14 rounded-full border-2 ${t.medalBg} ${t.medalBorder} ${t.medalText} font-headline-lg font-bold text-2xl shadow-card`}>
        {rank}
      </div>
      <div className={`w-full ${heightClass} rounded-xl border-2 ${t.blockBorder} flex flex-col items-center justify-center p-4 text-center shadow-card bg-surface-container-lowest dark:bg-gray-800/50`}>
        <span className={`font-metadata-mono text-[10px] uppercase tracking-wider font-bold ${t.accent}`}>
          #{rank}
        </span>
        <p className={`font-bold text-base leading-tight mt-1 truncate w-full ${TITLE}`}>
          {agent.name}
        </p>
        <p className={`font-metadata-mono text-3xl font-bold text-emerald-signal mt-2`}>
          {agent.formSales}
        </p>
        <p className={`font-metadata-mono text-[10px] uppercase tracking-wider ${MUTED}`}>Ventas</p>
        <p className={`font-metadata-mono text-sm font-bold text-emerald-signal mt-1`}>
          {formatCurrency(agent.formRevenue)}
        </p>
      </div>
      <div className={`w-full rounded-lg border border-whisper-border dark:border-gray-700 bg-pure-surface dark:bg-gray-800 px-3 py-2 text-center`}>
        <p className={`font-metadata-mono text-[10px] uppercase tracking-wider ${MUTED}`}>Conv %</p>
        <p className={`font-metadata-mono text-sm font-bold ${TITLE}`}>
          {agent.conversion ? `${agent.conversion.toFixed(1)}%` : '--'}
        </p>
      </div>
    </div>
  );
}

function RankBadge({ rank }: { rank: number }) {
  const styles: Record<number, string> = {
    1: 'bg-amber-warmth/20 text-amber-warmth border-amber-warmth/40',
    2: 'bg-cyan-500/15 text-cyan-600 dark:text-cyan-400 border-cyan-500/30',
    3: 'bg-orange-600/15 text-orange-700 dark:text-orange-400 border-orange-600/30',
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
      <div className="p-4 border-b border-whisper-border dark:border-gray-700 flex items-center gap-3">
        <FlashIcon size={22} className="text-amber-warmth" />
        <h3 className={`text-sm uppercase tracking-wider font-bold ${TITLE}`}>
          Live Sales Ticker
        </h3>
        <span className="ml-auto px-2 py-0.5 rounded-full bg-emerald-signal/15 text-emerald-signal font-metadata-mono text-[10px] uppercase tracking-wider font-bold flex items-center gap-1">
          <span className="w-1.5 h-1.5 rounded-full bg-emerald-signal animate-pulse" />
          en vivo
        </span>
      </div>
      <div className="relative h-12 overflow-hidden">
        <div className="absolute inset-0 flex items-center">
          <div className="flex gap-8 animate-ticker whitespace-nowrap">
            {looped.map((item, i) => (
              <span key={`${item.id}-${i}`} className={`font-metadata-mono text-sm flex items-center gap-2 ${TITLE}`}>
                <Tick01Icon size={16} className="text-emerald-signal" />
                {item.text}
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