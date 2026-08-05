import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import {
  getAgentPerformanceWithSales,
  getDashboardSummary,
} from '../services/api';
import {
  getVicidialCallTypeSales,
  getVicidialSalesSummary,
  listAllVicidialSales,
} from '../services/vicidialFormApi';
import { readSharedToken } from '../utils/sharedToken';
import type { ReportDto, VicidialSaleDto } from '../types';

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

function todayRange(): { from: string; to: string } {
  const date = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Tijuana',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  return { from: `${date} 00:00:00`, to: `${date} 23:59:59` };
}

const DAILY_GOAL_KEY = 'tv.dailyGoal';

function loadDailyGoal(): number {
  try {
    const raw = window.localStorage.getItem(DAILY_GOAL_KEY);
    if (!raw) return 0;
    const parsed = Number(raw);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
  } catch {
    return 0;
  }
}

function saveDailyGoal(value: number) {
  try {
    if (value > 0) window.localStorage.setItem(DAILY_GOAL_KEY, String(value));
    else window.localStorage.removeItem(DAILY_GOAL_KEY);
  } catch {
    return;
  }
}

function findMetric(metrics: { label: string; value: string }[] | undefined, hint: string): string | undefined {
  if (!metrics) return undefined;
  return metrics.find((m) => m.label.toLowerCase().includes(hint.toLowerCase()))?.value;
}

export default function TelevisionPage() {
  const { has, isAdmin } = useAuth();
  const authorized = has('tv.view');
  const [report, setReport] = useState<ReportDto | null>(null);
  const [salesSummary, setSalesSummary] = useState<{ totalSales: number; totalCount: number } | null>(null);
  const [callTypeSales, setCallTypeSales] = useState<{ agentName: string; agentId: string; outboundSales: number; inboundSales: number; outboundPct: number; inboundPct: number }[]>([]);
  const [recentSales, setRecentSales] = useState<VicidialSaleDto[]>([]);
  const [summaryMetrics, setSummaryMetrics] = useState<{ label: string; value: string }[] | null>(null);
  const [lastUpdated, setLastUpdated] = useState('');
  const [tvSale, setTvSale] = useState<TvSale | null>(null);
  const [dailyGoal, setDailyGoal] = useState<number>(() => loadDailyGoal());
  const saleTimeoutRef = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    try {
      const range = todayRange();
      const [agentReport, typeSales, summary, sales, metrics] = await Promise.all([
        getAgentPerformanceWithSales({ period: 'Today' }),
        getVicidialCallTypeSales('Today').catch(() => []),
        getVicidialSalesSummary(range.from, range.to, 500).catch(() => null),
        listAllVicidialSales(range.from, range.to, 500).catch(() => []),
        getDashboardSummary({ period: 'Today' }).catch(() => null),
      ]);
      setReport(agentReport);
      setCallTypeSales(typeSales);
      setSalesSummary(summary ? { totalSales: summary.totalSales, totalCount: summary.totalCount } : null);
      setRecentSales(sales);
      setSummaryMetrics(metrics?.metrics ?? null);
      setLastUpdated(new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' }));
    } catch {
      // silent fail; UI keeps prior values
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
    }));
    const lookup = new Map(performance.map((agent) => [agent.name.toLowerCase(), agent]));
    for (const split of callTypeSales) {
      const key = String(split.agentName ?? '').toLowerCase();
      const existing = lookup.get(key);
      if (existing) {
        existing.outboundSales = split.outboundSales;
        existing.inboundSales = split.inboundSales;
      } else if (split.agentName) {
        performance.push({
          name: split.agentName,
          user: split.agentId,
          outboundSales: split.outboundSales,
          inboundSales: split.inboundSales,
          totalSales: split.outboundSales + split.inboundSales,
          revenue: 0,
          callsHandled: 0,
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

  const conversion = useMemo(() => {
    const value = findMetric(summaryMetrics ?? undefined, 'conversion');
    if (value) {
      const parsed = Number(value.replace(/[^0-9.\-]/g, ''));
      if (Number.isFinite(parsed) && parsed > 0) return parsed;
    }
    if (totals.calls > 0) return (totals.sales / totals.calls) * 100;
    return 0;
  }, [summaryMetrics, totals]);

  const revenue = useMemo(() => {
    const value = findMetric(summaryMetrics ?? undefined, 'revenue');
    if (value) {
      const parsed = Number(value.replace(/[^0-9.\-]/g, ''));
      if (Number.isFinite(parsed)) return parsed;
    }
    return salesSummary?.totalSales ?? 0;
  }, [summaryMetrics, salesSummary]);

  const topPerformer = agents[0];
  const topRevenue = useMemo(() => {
    const map = new Map<string, number>();
    for (const sale of recentSales) {
      map.set(sale.salesRep, (map.get(sale.salesRep) ?? 0) + Number(sale.amount));
    }
    return Array.from(map.entries())
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5);
  }, [recentSales]);
  const topRevenueMax = topRevenue[0]?.[1] ?? 1;

  if (!authorized) return <div className="p-8 text-center text-[#f4f7fb]">You don&apos;t have access to this view.</div>;

  return (
    <main className="flex h-[calc(100dvh-4rem)] flex-col gap-3 overflow-hidden bg-[#0a0f17] px-3 py-3 text-[#f4f7fb]">
      {tvSale ? <SaleAnnouncement sale={tvSale} /> : null}

      <section className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        <Metric icon="revenue" label="Revenue" value={formatCurrency(revenue)} sub="Today" tone="purple" />
        <Metric icon="sales" label="Sales" value={String(totals.sales)} sub="Today" tone="lime" />
        <Metric icon="conversion" label="Conversion" value={`${conversion.toFixed(2)}%`} sub="Calls → Sale" tone="orange" />
        <TopPerformerCard agent={topPerformer} />
        <DailyGoalCard current={totals.sales} target={dailyGoal} canEdit={isAdmin} onSave={(value) => { setDailyGoal(value); saveDailyGoal(value); }} />
      </section>

      <section className="flex min-h-0 flex-1 flex-col">
        <article className="flex h-full flex-col overflow-hidden rounded-2xl border border-cyan-500/30 bg-[#0e141d]/80 shadow-[0_0_30px_rgba(34,211,238,.15)]">
          <header className="flex items-center justify-between border-b border-cyan-500/20 px-5 py-3">
            <h3 className="font-mono text-base font-black uppercase tracking-[0.25em] text-cyan-300">Agent Ranking</h3>
            <span className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#f4f7fb]">Updated {lastUpdated || '--:--'}</span>
          </header>
          <div className="grid grid-cols-[3rem_minmax(0,1fr)_5rem_5rem_5rem_5rem_5rem] items-center gap-2 px-4 py-2 font-mono text-[11px] uppercase tracking-[0.22em] text-[#f4f7fb]">
            <span>Rank</span>
            <span>Agent</span>
            <span className="text-right">Out</span>
            <span className="text-right">In</span>
            <span className="text-right">Sales</span>
            <span className="text-right">Calls</span>
            <span className="text-right">Conv</span>
          </div>
          <div className="grid flex-1 grid-cols-2 gap-3 overflow-y-auto px-4 pb-4">
            {(() => {
              const topAgents = agents.slice(0, 15);
              if (!topAgents.length) {
                return (
                  <div className="col-span-2 grid place-items-center py-10 font-mono text-sm uppercase tracking-[0.25em] text-[#f4f7fb]">Awaiting data</div>
                );
              }
              const midpoint = Math.ceil(topAgents.length / 2);
              const firstHalf = topAgents.slice(0, midpoint);
              const secondHalf = topAgents.slice(midpoint);
              return [firstHalf, secondHalf].map((group, groupIndex) => (
                <div key={groupIndex} className="flex flex-col gap-1">
                  {group.map((agent, index) => {
                    const rank = (groupIndex === 0 ? 0 : midpoint) + index + 1;
                    return <RankingRow key={`${agent.user}-${agent.name}`} agent={agent} rank={rank} />;
                  })}
                </div>
              ));
            })()}
          </div>
        </article>
      </section>

      <footer className="flex h-[5vh] min-h-9 items-center justify-between border-t border-cyan-500/20 px-4 font-mono text-[11px] uppercase tracking-[0.3em] text-[#f4f7fb]">
        <span>{agents.length} agents ranked</span>
        <span className="text-cyan-300">Top revenue: {topRevenue[0]?.[0] ?? '—'} · {topRevenue[0] ? formatCurrency(topRevenue[0][1]) : '—'}</span>
        <span>Updated {lastUpdated || '--:--'}</span>
      </footer>
    </main>
  );
}

function num2(value: unknown): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function Metric({ icon, label, value, sub, tone }: { icon: 'sales' | 'conversion' | 'revenue'; label: string; value: string; sub: string; tone: 'lime' | 'orange' | 'purple' }) {
  const tones: Record<typeof tone, { text: string; ring: string; glow: string }> = {
    lime: { text: 'text-lime-300', ring: 'border-lime-400/50', glow: 'shadow-[0_0_20px_rgba(132,204,22,.25)]' },
    orange: { text: 'text-orange-300', ring: 'border-orange-400/50', glow: 'shadow-[0_0_20px_rgba(251,146,60,.25)]' },
    purple: { text: 'text-purple-300', ring: 'border-purple-400/50', glow: 'shadow-[0_0_20px_rgba(192,132,252,.25)]' },
  };
  const Icon = icon === 'sales' ? ShoppingCartIcon : icon === 'conversion' ? TargetIcon : DollarIcon;
  const accent = tones[tone];
  return (
    <div className={`flex h-full items-center gap-3 rounded-xl border ${accent.ring} bg-[#0e141d]/80 px-4 py-2 ${accent.glow}`}>
      <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-full border ${accent.ring} ${accent.text}`}>
        <Icon />
      </span>
      <div className="min-w-0">
        <p className={`font-mono text-[10px] font-bold uppercase tracking-[0.3em] ${accent.text}`}>{label}</p>
        <p className="text-[clamp(1.4rem,2.2vw,2rem)] font-black leading-tight text-[#f4f7fb]">{value}</p>
        <p className="font-mono text-[9px] uppercase tracking-[0.25em] text-[#f4f7fb]">{sub}</p>
      </div>
    </div>
  );
}

function TopPerformerCard({ agent }: { agent?: AgentRow }) {
  return (
    <section className="flex h-full items-center gap-3 rounded-xl border border-purple-400/50 bg-[#0e141d]/80 px-4 py-2 shadow-[0_0_20px_rgba(192,132,252,.25)]">
      <span className="grid h-12 w-12 shrink-0 place-items-center rounded-full border border-purple-400/60 bg-purple-500/15 text-lg font-black text-purple-200">
        {agent ? initials(agent.name) : '—'}
      </span>
      <div className="min-w-0 flex-1">
        <p className="font-mono text-[10px] font-bold uppercase tracking-[0.3em] text-purple-300">Top Performer</p>
        <p className="truncate text-base font-black leading-tight text-[#f4f7fb]">{agent?.name ?? 'Awaiting data'}</p>
        <p className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#f4f7fb]">
          {agent ? `${agent.totalSales} sales · ${formatCurrency(agent.revenue)}` : 'No sales yet'}
        </p>
      </div>
    </section>
  );
}

function DailyGoalCard({ current, target, canEdit, onSave }: { current: number; target: number; canEdit: boolean; onSave: (value: number) => void }) {
  const [draft, setDraft] = useState('');
  const [editing, setEditing] = useState(false);
  const percent = target > 0 ? clampPercent((current / target) * 100) : 0;
  const remaining = target > 0 ? Math.max(target - current, 0) : 0;
  const handleSave = () => {
    const parsed = num2(draft);
    if (Number.isFinite(parsed) && parsed >= 0) onSave(parsed);
    setEditing(false);
    setDraft('');
  };
  return (
    <section className="flex h-full items-center gap-3 rounded-xl border border-cyan-400/50 bg-[#0e141d]/80 px-4 py-2 shadow-[0_0_20px_rgba(34,211,238,.25)]">
      <div className="relative grid h-12 w-12 shrink-0 place-items-center">
        <ProgressDial percent={percent} size={48} stroke={5} />
        <span className="absolute text-xs font-black text-[#f4f7fb]">{current}</span>
      </div>
      <div className="min-w-0 flex-1">
        <header className="flex items-center justify-between">
          <p className="font-mono text-[10px] font-bold uppercase tracking-[0.3em] text-cyan-300">Daily Goal</p>
          {canEdit ? (
            editing ? (
              <div className="flex items-center gap-1">
                <input
                  type="number"
                  min={0}
                  value={draft}
                  onChange={(event) => setDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') handleSave();
                    if (event.key === 'Escape') { setEditing(false); setDraft(''); }
                  }}
                  placeholder={target ? String(target) : '0'}
                  className="w-14 rounded border border-cyan-400/60 bg-[#0a0f17] px-1 py-0.5 text-right font-mono text-xs font-bold text-cyan-200 outline-none focus:ring-2 focus:ring-cyan-400"
                />
                <button type="button" onClick={handleSave} className="rounded bg-cyan-500 px-2 py-0.5 font-mono text-[9px] font-bold uppercase tracking-[0.2em] text-white shadow hover:bg-cyan-400">Save</button>
              </div>
            ) : (
              <button type="button" onClick={() => { setEditing(true); setDraft(target ? String(target) : ''); }} className="rounded bg-cyan-500 px-2 py-0.5 font-mono text-[9px] font-bold uppercase tracking-[0.2em] text-white shadow hover:bg-cyan-400">Edit</button>
            )
          ) : null}
        </header>
        <p className="font-mono text-[10px] uppercase tracking-[0.25em] text-[#f4f7fb]">of {target || '—'} · {remaining || 0} left</p>
      </div>
    </section>
  );
}

function RankingRow({ agent, rank }: { agent: AgentRow; rank: number }) {
  const highlightMedal = rank <= 3;
  const medalBg = rank === 1 ? 'bg-lime-400/10 border-l-lime-300' :
    rank === 2 ? 'bg-cyan-400/10 border-l-cyan-300' :
    rank === 3 ? 'bg-orange-400/10 border-l-orange-300' :
    'bg-[#0e141d]/40 border-l-slate-700';
  const conversion = agent.callsHandled > 0 ? clampPercent((agent.totalSales / agent.callsHandled) * 100) : 0;
  return (
    <article className={`grid grid-cols-[3rem_minmax(0,1fr)_5rem_5rem_5rem_5rem_5rem] items-center gap-2 rounded-lg border-l-4 border border-slate-800 ${medalBg} px-3 py-2`}>
      <span className="flex items-center gap-1 font-mono text-base font-black text-[#f4f7fb]">
        {rank === 1 ? <CrownIcon /> : null}
        {rank}
      </span>
      <div className="flex min-w-0 items-center gap-2">
        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-slate-800 font-mono text-xs font-black text-cyan-200">{initials(agent.name)}</span>
        <p className="truncate text-sm font-bold leading-tight text-[#f4f7fb]">{agent.name}</p>
      </div>
      <span className="text-right font-mono text-sm font-bold text-cyan-300">{agent.outboundSales}</span>
      <span className="text-right font-mono text-sm font-bold text-purple-300">{agent.inboundSales}</span>
      <span className={`text-right font-mono text-base font-black ${highlightMedal ? 'text-lime-300' : 'text-[#f4f7fb]'}`}>{agent.totalSales}</span>
      <span className="text-right font-mono text-xs font-bold text-[#f4f7fb]">{agent.callsHandled.toLocaleString('en-US')}</span>
      <span className="text-right font-mono text-xs font-bold text-[#f4f7fb]">{conversion.toFixed(2)}%</span>
    </article>
  );
}

function ProgressDial({ percent, size = 132, stroke = 12 }: { percent: number; size?: number; stroke?: number }) {
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

function SaleAnnouncement({ sale }: { sale: TvSale }) {
  return (
    <div className="fixed inset-0 z-[100] grid place-items-center overflow-hidden bg-[#0a0f17] text-center">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(132,204,22,.16),transparent_55%)]" />
      <div className="absolute inset-6 border border-lime-300/30" />
      <div className="relative max-w-[92vw]">
        <p className="mb-8 font-mono text-[clamp(1rem,2vw,2rem)] font-black uppercase tracking-[0.55em] text-lime-300">New Sale</p>
        <h2 className="text-[clamp(4rem,11vw,10rem)] font-black leading-[.85] tracking-tight text-[#f4f7fb]">{sale.salesRep}</h2>
        <p className="mt-10 font-mono text-[clamp(1rem,2.2vw,2.25rem)] uppercase tracking-[0.25em] text-cyan-200">{sale.bundle}</p>
        <p className="mt-5 font-mono text-[clamp(1rem,2vw,2rem)] uppercase tracking-[0.3em] text-lime-300">{sale.todaysCount} {sale.todaysCount === 1 ? 'sale' : 'sales'} today</p>
      </div>
    </div>
  );
}

function ShoppingCartIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="h-6 w-6">
      <circle cx="9" cy="20" r="1.6" />
      <circle cx="17" cy="20" r="1.6" />
      <path d="M2 3h2l3 12h12l2-9H6" />
    </svg>
  );
}

function TargetIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="h-6 w-6">
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="5" />
      <circle cx="12" cy="12" r="1.5" fill="currentColor" />
    </svg>
  );
}

function DollarIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="h-6 w-6">
      <path d="M12 3v18" />
      <path d="M16 7H10a3 3 0 0 0 0 6h4a3 3 0 0 1 0 6H8" />
    </svg>
  );
}

function CrownIcon() {
  return (
    <svg viewBox="0 0 24 24" className="h-4 w-4 fill-amber-400" aria-hidden>
      <path d="M3 7l4 4 5-7 5 7 4-4-2 11H5L3 7zm0 0" />
    </svg>
  );
}

function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).map((part) => part[0]).join('').slice(0, 2).toUpperCase() || '--';
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value) || value <= 0) return 0;
  if (value >= 100) return 100;
  return Math.round(value * 10) / 10;
}
