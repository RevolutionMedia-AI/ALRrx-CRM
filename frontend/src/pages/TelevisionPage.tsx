import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import {
  getAgentPerformanceWithSales,
  getDashboardSummary,
  getStaffing,
} from '../services/api';
import {
  getVicidialCallCounts,
  getVicidialCallTypeSales,
  getVicidialSalesSummary,
  listAllVicidialSales,
} from '../services/vicidialFormApi';
import { readSharedToken } from '../utils/sharedToken';
import type {
  DashboardSummaryDto,
  ReportDto,
  VicidialCallCounts,
  VicidialCallTypeSalesRow,
  VicidialSaleDto,
} from '../types';

interface TvSale {
  salesRep: string;
  bundle: string;
  todaysCount: number;
}

interface AgentRow {
  name: string;
  user: string;
  outboundSales: number;
  inboundSales: number;
  totalSales: number;
  revenue: number;
  callsHandled: number;
  conversion: number;
}

function num(value: unknown): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(amount);
}

function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).map((part) => part[0]).join('').slice(0, 2).toUpperCase() || '--';
}

function todayRange(): { from: string; to: string } {
  const date = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Tijuana',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  return { from: `${date} 00:00:00`, to: `${date} 23:59:59` };
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value) || value <= 0) return 0;
  if (value >= 100) return 100;
  return Math.round(value * 10) / 10;
}

export default function TelevisionPage() {
  const { has } = useAuth();
  const authorized = has('tv.view');

  const [report, setReport] = useState<ReportDto | null>(null);
  const [callTypeSales, setCallTypeSales] = useState<VicidialCallTypeSalesRow[]>([]);
  const [callCounts, setCallCounts] = useState<VicidialCallCounts | null>(null);
  const [salesSummary, setSalesSummary] = useState<{ totalSales: number; totalCount: number } | null>(null);
  const [recentSales, setRecentSales] = useState<VicidialSaleDto[]>([]);
  const [staffing, setStaffing] = useState<ReportDto | null>(null);
  const [dashboardSummary, setDashboardSummary] = useState<DashboardSummaryDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState('');
  const [tvSale, setTvSale] = useState<TvSale | null>(null);
  const saleTimeoutRef = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    try {
      const range = todayRange();
      const [
        agentReport,
        typeSales,
        counts,
        summary,
        sales,
        currentStaffing,
        dashboard,
      ] = await Promise.all([
        getAgentPerformanceWithSales({ period: 'Today' }),
        getVicidialCallTypeSales('Today').catch(() => [] as VicidialCallTypeSalesRow[]),
        getVicidialCallCounts('Today').catch(() => null),
        getVicidialSalesSummary(range.from, range.to, 500).catch(() => null),
        listAllVicidialSales(range.from, range.to, 500).catch(() => []),
        getStaffing().catch(() => null),
        getDashboardSummary({ period: 'Today' }).catch(() => null),
      ]);

      setReport(agentReport);
      setCallTypeSales(typeSales);
      setCallCounts(counts);
      setSalesSummary(summary ? { totalSales: summary.totalSales, totalCount: summary.totalCount } : null);
      setRecentSales(sales);
      setStaffing(currentStaffing);
      setDashboardSummary(dashboard);
      setLastUpdated(new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' }));
      setError(null);
    } catch {
      setError('Unable to load sales leaderboard');
    }
  }, []);

  useEffect(() => {
    if (!authorized) return;
    void refresh();
    const interval = window.setInterval(() => void refresh(), 30_000);
    return () => window.clearInterval(interval);
  }, [authorized, refresh]);

  useEffect(() => {
    if (!authorized) return;
    const token = readSharedToken();
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/dashboard', { accessTokenFactory: () => token ?? '' })
      .withAutomaticReconnect()
      .build();
    connection.on('BroadcastTvSaleAsync', (salesRep: string, bundle: string, _amount: number, todaysCount: number) => {
      void refresh();
      setTvSale({ salesRep, bundle, todaysCount });
      if (saleTimeoutRef.current) window.clearTimeout(saleTimeoutRef.current);
      saleTimeoutRef.current = window.setTimeout(() => setTvSale(null), 6000);
    });
    void connection.start();
    return () => {
      void connection.stop();
      if (saleTimeoutRef.current) window.clearTimeout(saleTimeoutRef.current);
    };
  }, [authorized, refresh]);

  const agents = useMemo<AgentRow[]>(() => {
    const performance = (report?.rows ?? []).map((row) => ({
      name: String(row.Name ?? row.User ?? ''),
      user: String(row.User ?? ''),
      outboundSales: 0,
      inboundSales: 0,
      totalSales: num(row.Form_Sales_Count),
      revenue: num(row.Form_Sales_Amount),
      callsHandled: num(row.Calls_Handled),
      conversion: num(row.Conversion_Percentage),
    }));
    const lookup = new Map(performance.map((agent) => [agent.name.toLowerCase(), agent]));
    for (const split of callTypeSales) {
      const key = String(split.agentName ?? '').toLowerCase();
      const existing = lookup.get(key) ?? lookup.get(String(split.agentId ?? '').toLowerCase());
      if (existing) {
        existing.outboundSales = split.outboundSales;
        existing.inboundSales = split.inboundSales;
        existing.totalSales = split.outboundSales + split.inboundSales;
      } else if (split.agentName) {
        performance.push({
          name: split.agentName,
          user: split.agentId,
          outboundSales: split.outboundSales,
          inboundSales: split.inboundSales,
          totalSales: split.outboundSales + split.inboundSales,
          revenue: 0,
          callsHandled: 0,
          conversion: 0,
        });
      }
    }
    return performance
      .filter((agent) => agent.name)
      .sort((a, b) => b.totalSales - a.totalSales || a.name.localeCompare(b.name));
  }, [report, callTypeSales]);

  const totals = useMemo(() => agents.reduce((result, agent) => ({
    sales: result.sales + agent.totalSales,
    outbound: result.outbound + agent.outboundSales,
    inbound: result.inbound + agent.inboundSales,
    calls: result.calls + agent.callsHandled,
  }), { sales: 0, outbound: 0, inbound: 0, calls: 0 }), [agents]);

function findDashboardMetric(metrics: { label: string; value: string }[] | undefined, hint: string): string | undefined {
  if (!metrics) return undefined;
  const match = metrics.find((m) => m.label.toLowerCase().includes(hint.toLowerCase()));
  return match?.value;
}

const conversion = useMemo(() => {
  if (dashboardSummary) {
    const value = findDashboardMetric(dashboardSummary.metrics, 'conversion');
    const parsed = value ? Number(value.replace(/[^0-9.\-]/g, '')) : 0;
    if (Number.isFinite(parsed) && parsed > 0) return parsed;
  }
  if (callCounts?.outboundCalls) {
    const outboundSales = callCounts.outboundSales ?? totals.outbound;
    return (outboundSales / callCounts.outboundCalls) * 100;
  }
  return totals.calls ? (totals.sales / totals.calls) * 100 : 0;
}, [dashboardSummary, callCounts, totals]);

const revenue = useMemo(() => {
  if (dashboardSummary) {
    const value = findDashboardMetric(dashboardSummary.metrics, 'revenue');
    if (value) {
      const parsed = Number(value.replace(/[^0-9.\-]/g, ''));
      if (Number.isFinite(parsed)) return parsed;
    }
  }
  return salesSummary?.totalSales ?? 0;
}, [dashboardSummary, salesSummary]);

  const activeAgents = useMemo(() => {
    if (!staffing?.rows?.length) return 0;
    return staffing.rows.filter((row) => String(row.Status ?? '').toUpperCase() !== 'OFFLINE').length;
  }, [staffing]);

  if (!authorized) return <div className="p-8 text-center">You don't have access to this view.</div>;

  return (
    <main className="flex min-h-[calc(100dvh-4rem)] flex-col gap-4 overflow-hidden bg-white p-4 text-[#111827] dark:bg-slate-950 dark:text-white">
      {tvSale ? <SaleAnnouncement sale={tvSale} /> : null}

      <section className="grid grid-cols-1 gap-[1vw] lg:grid-cols-[3fr_1fr]">
        <article className="overflow-hidden rounded-xl border border-cyan-500/40 bg-white shadow-[0_0_30px_rgba(34,211,238,.18)] dark:bg-slate-900/60">
          <header className="flex items-center justify-between border-b border-cyan-500/30 px-5 py-3">
            <h3 className="font-mono text-sm font-bold uppercase tracking-[0.25em] text-cyan-700 dark:text-cyan-300">Ranking de Agentes</h3>
            <span className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">Updated {lastUpdated || '--:--'}</span>
          </header>
          <div className="grid grid-cols-[3.5rem_minmax(0,1fr)_6rem_6rem_6rem_6rem_5rem] items-center gap-2 px-4 py-2 font-mono text-[11px] uppercase tracking-[0.22em] text-[#111827] dark:text-white">
            <span>Pos.</span>
            <span>Agent</span>
            <span className="text-right">Outbound</span>
            <span className="text-right">Inbound</span>
            <span className="text-right">Sales</span>
            <span className="text-right">Revenue</span>
            <span className="text-right">% Total</span>
          </div>
          <div className="min-h-0 flex-1 space-y-1 overflow-y-auto px-4 pb-4">
            {agents.length ? agents.map((agent, index) => (
              <RankingRow
                key={`${agent.user}-${agent.name}`}
                agent={agent}
                rank={index + 1}
                teamTotal={totals.sales}
                teamRevenue={revenue}
              />
            )) : (
              <div className="grid place-items-center py-10 font-mono text-sm uppercase tracking-[0.25em] text-[#111827] dark:text-white">Awaiting data</div>
            )}
          </div>
        </article>

        <aside className="flex flex-col gap-4">
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-3 lg:grid-cols-1">
            <Metric icon="sales" label="Sales" value={String(totals.sales)} sub="Today" tone="lime" />
            <Metric icon="conversion" label="Conversion" value={`${conversion.toFixed(2)}%`} sub="Calls → Sale" tone="orange" />
            <Metric icon="revenue" label="Revenue" value={formatCurrency(revenue)} sub="Today" tone="purple" />
          </section>
          <TopPerformer agents={agents} recentSales={recentSales} />
          <DailyGoalCard current={totals.sales} />
          <PulseBoard activeAgents={activeAgents} recentSales={recentSales} />
        </aside>
      </section>

      <footer className="flex h-[5vh] min-h-10 items-center justify-between border-t border-cyan-500/30 px-[2vw] font-mono text-[clamp(.55rem,.75vw,.75rem)] uppercase tracking-[0.3em] text-[#111827] dark:text-white">
        <span>{agents.length} agents ranked</span>
        <span className="text-cyan-700 dark:text-cyan-300">Every sale moves the board</span>
        <span>Updated {lastUpdated || '--:--'}</span>
      </footer>
    </main>
  );
}

function Metric({ icon, label, value, sub, tone }: { icon: 'sales' | 'conversion' | 'revenue'; label: string; value: string; sub: string; tone: 'lime' | 'orange' | 'purple' }) {
  const tones: Record<typeof tone, { text: string; ring: string; glow: string }> = {
    lime: { text: 'text-lime-700 dark:text-lime-300', ring: 'border-lime-500/60 dark:border-lime-300/40', glow: 'shadow-[0_0_25px_rgba(132,204,22,.18)]' },
    orange: { text: 'text-orange-700 dark:text-orange-300', ring: 'border-orange-500/60 dark:border-orange-300/40', glow: 'shadow-[0_0_25px_rgba(251,146,60,.18)]' },
    purple: { text: 'text-purple-700 dark:text-purple-300', ring: 'border-purple-500/60 dark:border-purple-300/40', glow: 'shadow-[0_0_25px_rgba(192,132,252,.18)]' },
  };
  const Icon = icon === 'sales' ? ShoppingCartIcon : icon === 'conversion' ? TargetIcon : DollarIcon;
  const accent = tones[tone];
  return (
    <div className={`flex items-center gap-4 rounded-xl border ${accent.ring} bg-white px-5 py-3 dark:bg-slate-900/60 ${accent.glow}`}>
      <span className={`grid h-14 w-14 place-items-center rounded-full border ${accent.ring} ${accent.text}`}>
        <Icon />
      </span>
      <div className="min-w-0">
        <p className={`font-mono text-[11px] font-bold uppercase tracking-[0.3em] ${accent.text}`}>{label}</p>
        <p className="text-[clamp(2rem,3.2vw,3.5rem)] font-black leading-none text-[#111827] dark:text-white">{value}</p>
        <p className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">{sub}</p>
      </div>
    </div>
  );
}

function ShoppingCartIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="h-7 w-7">
      <circle cx="9" cy="20" r="1.6" />
      <circle cx="17" cy="20" r="1.6" />
      <path d="M2 3h2l3 12h12l2-9H6" />
    </svg>
  );
}

function TargetIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="h-7 w-7">
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="5" />
      <circle cx="12" cy="12" r="1.5" fill="currentColor" />
    </svg>
  );
}

function DollarIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="h-7 w-7">
      <path d="M12 3v18" />
      <path d="M16 7H10a3 3 0 0 0 0 6h4a3 3 0 0 1 0 6H8" />
    </svg>
  );
}

function RankingRow({ agent, rank, teamTotal, teamRevenue }: { agent: AgentRow; rank: number; teamTotal: number; teamRevenue: number }) {
  const share = teamTotal > 0 ? clampPercent((agent.totalSales / teamTotal) * 100) : 0;
  const revenueShare = teamRevenue > 0 ? clampPercent((agent.revenue / teamRevenue) * 100) : 0;
  const highlightMedal = rank <= 3;
  const medalBg = rank === 1 ? 'bg-lime-100 border-l-lime-500 dark:bg-lime-400/10 dark:border-l-lime-300' :
    rank === 2 ? 'bg-cyan-100 border-l-cyan-500 dark:bg-cyan-400/10 dark:border-l-cyan-300' :
    rank === 3 ? 'bg-orange-100 border-l-orange-500 dark:bg-orange-400/10 dark:border-l-orange-300' :
    'bg-white border-l-[#d7dee8] dark:bg-slate-900/40 dark:border-l-slate-700';
  return (
    <article className={`relative overflow-hidden rounded-lg border-l-4 border border-[#d7dee8] dark:border-slate-800 ${medalBg}`}>
      <div className="grid grid-cols-[3.5rem_minmax(0,1fr)_6rem_6rem_6rem_6rem_5rem] items-center gap-2 px-3 py-2">
        <span className={`font-mono text-[clamp(1rem,1.4vw,1.5rem)] font-black ${highlightMedal ? 'text-lime-700 dark:text-lime-300' : 'text-[#111827] dark:text-white'}`}>{rank}</span>
        <div className="flex min-w-0 items-center gap-3">
          <span className="grid aspect-square w-9 shrink-0 place-items-center rounded-full bg-[#e5e7eb] font-mono text-xs font-black text-[#111827] dark:bg-slate-800 dark:text-cyan-200">{initials(agent.name)}</span>
          <div className="min-w-0">
            <p className="truncate text-[clamp(.85rem,1.1vw,1.1rem)] font-bold leading-tight text-[#111827] dark:text-white">{agent.name}</p>
            <p className="truncate font-mono text-[10px] uppercase tracking-[0.2em] text-[#111827] dark:text-white">{agent.user ? `#${agent.user}` : 'Agent'}</p>
          </div>
        </div>
        <span className="text-right font-mono text-[clamp(.9rem,1.1vw,1.1rem)] font-bold text-cyan-700 dark:text-cyan-300">{agent.outboundSales}</span>
        <span className="text-right font-mono text-[clamp(.9rem,1.1vw,1.1rem)] font-bold text-purple-700 dark:text-purple-300">{agent.inboundSales}</span>
        <span className="text-right font-mono text-[clamp(1rem,1.3vw,1.4rem)] font-black text-lime-700 dark:text-lime-300">{agent.totalSales}</span>
        <span className="text-right font-mono text-[clamp(.85rem,1vw,1rem)] font-bold text-[#111827] dark:text-white">{formatCurrency(agent.revenue)}</span>
        <span className="text-right font-mono text-[clamp(.8rem,1vw,1rem)] font-bold text-[#111827] dark:text-white">{share.toFixed(1)}%</span>
      </div>
      <span
        aria-hidden
        className="absolute bottom-0 left-0 h-1 bg-gradient-to-r from-lime-500 via-cyan-500 to-purple-500 dark:from-lime-400 dark:via-cyan-400 dark:to-purple-400"
        style={{ width: `${Math.max(share, revenueShare)}%`, transition: 'width .7s ease' }}
      />
    </article>
  );
}

function TopPerformer({ agents, recentSales }: { agents: AgentRow[]; recentSales: VicidialSaleDto[] }) {
  const top = agents[0];
  const last = recentSales[0];
  return (
    <section className="overflow-hidden rounded-xl border border-purple-500/60 bg-white p-4 shadow-[0_0_25px_rgba(192,132,252,.18)] dark:bg-slate-900/60">
      <header className="flex items-center justify-between">
        <h3 className="font-mono text-xs font-bold uppercase tracking-[0.25em] text-purple-700 dark:text-purple-300">Top Performer</h3>
        <span className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">Today</span>
      </header>
      {top ? (
        <div className="mt-3 flex items-center gap-3">
          <span className="grid h-14 w-14 place-items-center rounded-full border border-purple-500/70 bg-purple-100 font-mono text-base font-black text-purple-700 dark:border-purple-300/60 dark:bg-purple-500/15 dark:text-purple-200">{initials(top.name)}</span>
          <div className="min-w-0">
            <p className="truncate text-lg font-black text-[#111827] dark:text-white">{top.name}</p>
            <p className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">{top.totalSales} sales · {formatCurrency(last?.amount ?? 0)} last</p>
          </div>
        </div>
      ) : (
        <p className="mt-3 font-mono text-xs uppercase tracking-[0.25em] text-[#111827] dark:text-white">Awaiting data</p>
      )}
    </section>
  );
}

function DailyGoalCard({ current }: { current: number }) {
  const target = 0;
  const percent = target > 0 ? clampPercent((current / target) * 100) : 0;
  const remaining = Math.max(target - current, 0);
  return (
    <section className="overflow-hidden rounded-xl border border-cyan-500/60 bg-white p-4 shadow-[0_0_25px_rgba(34,211,238,.18)] dark:bg-slate-900/60">
      <header className="flex items-center justify-between">
        <h3 className="font-mono text-xs font-bold uppercase tracking-[0.25em] text-cyan-700 dark:text-cyan-300">Daily Goal</h3>
        <span className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">Sales target</span>
      </header>
      <div className="relative mt-3 grid place-items-center">
        <ProgressDial percent={percent} />
        <p className="pointer-events-none absolute text-center">
          <span className="block text-2xl font-black text-[#111827] dark:text-white">{current}</span>
          <span className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">of {target || '—'}</span>
        </p>
      </div>
      <dl className="mt-3 grid grid-cols-3 gap-2 font-mono text-[10px] uppercase tracking-[0.2em] text-[#111827] dark:text-white">
        <div><dt className="text-cyan-700 dark:text-cyan-300">Current</dt><dd className="text-[#111827] dark:text-white">{current}</dd></div>
        <div><dt className="text-purple-700 dark:text-purple-300">Target</dt><dd className="text-[#111827] dark:text-white">{target || '—'}</dd></div>
        <div><dt className="text-orange-700 dark:text-orange-300">Remaining</dt><dd className="text-[#111827] dark:text-white">{remaining || '—'}</dd></div>
      </dl>
    </section>
  );
}

function ProgressDial({ percent }: { percent: number }) {
  const size = 132;
  const stroke = 12;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (clampPercent(percent) / 100) * circumference;
  return (
    <svg width={size} height={size} className="-rotate-90">
      <circle cx={size / 2} cy={size / 2} r={radius} stroke="rgba(148,163,184,0.25)" strokeWidth={stroke} fill="none" />
      <circle cx={size / 2} cy={size / 2} r={radius} stroke="url(#tvGoalGradient)" strokeWidth={stroke} fill="none" strokeLinecap="round" strokeDasharray={circumference} strokeDashoffset={offset} style={{ transition: 'stroke-dashoffset .7s ease' }} />
      <defs>
        <linearGradient id="tvGoalGradient" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#a3e635" />
          <stop offset="50%" stopColor="#22d3ee" />
          <stop offset="100%" stopColor="#c084fc" />
        </linearGradient>
      </defs>
    </svg>
  );
}

function PulseBoard({ activeAgents, recentSales }: { activeAgents: number; recentSales: VicidialSaleDto[] }) {
  const latest = recentSales.slice(0, 3);
  return (
    <section className="overflow-hidden rounded-xl border border-lime-500/60 bg-white p-4 shadow-[0_0_25px_rgba(132,204,22,.18)] dark:bg-slate-900/60">
      <header className="flex items-center justify-between">
        <h3 className="font-mono text-xs font-bold uppercase tracking-[0.25em] text-lime-700 dark:text-lime-300">Live Pulse</h3>
        <span className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#111827] dark:text-white">{activeAgents} active</span>
      </header>
      <ul className="mt-3 space-y-2 text-sm text-[#111827] dark:text-white">
        {latest.length ? latest.map((sale) => (
          <li key={sale.id} className="flex items-center justify-between gap-2">
            <span className="truncate text-[#111827] dark:text-slate-100">{sale.salesRep}</span>
            <span className="font-mono text-xs text-lime-700 dark:text-lime-300">{formatCurrency(Number(sale.amount))}</span>
          </li>
        )) : (
          <li className="font-mono text-xs uppercase tracking-[0.25em] text-[#111827] dark:text-white">Awaiting first sale</li>
        )}
      </ul>
    </section>
  );
}

function SaleAnnouncement({ sale }: { sale: TvSale }) {
  return (
    <div className="fixed inset-0 z-[100] grid place-items-center overflow-hidden bg-white text-center text-[#111827] dark:bg-slate-950 dark:text-white">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(132,204,22,.18),transparent_55%)] dark:bg-[radial-gradient(circle_at_center,rgba(132,204,22,.16),transparent_55%)]" />
      <div className="absolute inset-6 border border-lime-500/40 dark:border-lime-300/30" />
      <div className="relative max-w-[92vw]">
        <p className="mb-8 font-mono text-[clamp(1rem,2vw,2rem)] font-black uppercase tracking-[0.55em] text-lime-700 dark:text-lime-300">New Sale</p>
        <h2 className="text-[clamp(4rem,11vw,10rem)] font-black leading-[.85] tracking-tight text-[#111827] dark:text-white">{sale.salesRep}</h2>
        <p className="mt-10 font-mono text-[clamp(1rem,2.2vw,2.25rem)] uppercase tracking-[0.25em] text-cyan-700 dark:text-cyan-200">{sale.bundle}</p>
        <p className="mt-5 font-mono text-[clamp(1rem,2vw,2rem)] uppercase tracking-[0.3em] text-lime-700 dark:text-lime-300">{sale.todaysCount} {sale.todaysCount === 1 ? 'sale' : 'sales'} today</p>
      </div>
    </div>
  );
}