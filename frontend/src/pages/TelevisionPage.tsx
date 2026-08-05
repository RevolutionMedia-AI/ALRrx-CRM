import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import {
  getAgentPerformanceWithSales,
  getDashboardSummary,
  getStaffing,
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

const EXCLUDED_RANK_NAMES = new Set(
  [
    'silver arellano',
    'jessica duarte',
  ].map((name) => name.toLowerCase()),
);

function isExcludedFromRank(name: string): boolean {
  const key = name.trim().toLowerCase();
  if (!key) return true;
  if (EXCLUDED_RANK_NAMES.has(key)) return true;
  if (key.includes('vicidial')) return true;
  return false;
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
    if (!raw) return 40;
    const parsed = Number(raw);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 40;
  } catch {
    return 40;
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
  const [staffingReport, setStaffingReport] = useState<ReportDto | null>(null);
  const [salesSummary, setSalesSummary] = useState<{ totalSales: number; totalCount: number } | null>(null);
  const [callTypeSales, setCallTypeSales] = useState<{ agentName: string; agentId: string; outboundSales: number; inboundSales: number; outboundPct: number; inboundPct: number }[]>([]);
  const [recentSales, setRecentSales] = useState<VicidialSaleDto[]>([]);
  const [summaryMetrics, setSummaryMetrics] = useState<{ label: string; value: string }[] | null>(null);
  const [lastUpdated, setLastUpdated] = useState('');
  const [tvSale, setTvSale] = useState<TvSale | null>(null);
  const [dailyGoal, setDailyGoal] = useState<number>(() => loadDailyGoal());
  const [clock, setClock] = useState('');
  const [flashId, setFlashId] = useState<string | null>(null);
  const saleTimeoutRef = useRef<number | null>(null);
  const flashTimeoutRef = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    try {
      const range = todayRange();
      const [agentReport, typeSales, summary, sales, metrics, staffing] = await Promise.all([
        getAgentPerformanceWithSales({ period: 'Today' }),
        getVicidialCallTypeSales('Today').catch(() => []),
        getVicidialSalesSummary(range.from, range.to, 500).catch(() => null),
        listAllVicidialSales(range.from, range.to, 500).catch(() => []),
        getDashboardSummary({ period: 'Today' }).catch(() => null),
        getStaffing().catch(() => null),
      ]);
      setReport(agentReport);
      setStaffingReport(staffing);
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
    const tick = () =>
      setClock(
        new Date().toLocaleTimeString('en-US', {
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
          hour12: true,
        }),
      );
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, []);

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
      setFlashId(salesRep);
      if (saleTimeoutRef.current) window.clearTimeout(saleTimeoutRef.current);
      saleTimeoutRef.current = window.setTimeout(() => setTvSale(null), 6000);
      if (flashTimeoutRef.current) window.clearTimeout(flashTimeoutRef.current);
      flashTimeoutRef.current = window.setTimeout(() => setFlashId(null), 3500);
    });
    void connection.start();
    return () => {
      void connection.stop();
      if (saleTimeoutRef.current) window.clearTimeout(saleTimeoutRef.current);
      if (flashTimeoutRef.current) window.clearTimeout(flashTimeoutRef.current);
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
      } else if (split.agentName && !isExcludedFromRank(split.agentName)) {
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
      .filter((agent) => agent.name && !isExcludedFromRank(agent.name))
      .sort((a, b) => b.totalSales - a.totalSales || b.revenue - a.revenue || a.name.localeCompare(b.name));
  }, [report, callTypeSales]);

  const totals = useMemo(
    () =>
      agents.reduce(
        (result, agent) => ({
          sales: result.sales + agent.totalSales,
          outbound: result.outbound + agent.outboundSales,
          inbound: result.inbound + agent.inboundSales,
          calls: result.calls + agent.callsHandled,
          revenue: result.revenue + agent.revenue,
        }),
        { sales: 0, outbound: 0, inbound: 0, calls: 0, revenue: 0 },
      ),
    [agents],
  );

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
    return totals.revenue || salesSummary?.totalSales || 0;
  }, [summaryMetrics, totals, salesSummary]);

  const dateLabel = new Date().toLocaleDateString('en-US', { weekday: 'long', month: 'short', day: 'numeric' });
  const goalPct = dailyGoal > 0 ? clampPercent((totals.sales / dailyGoal) * 100) : 0;
  const goalPctLabel = `${Math.round(goalPct)}%`;
  const splitIndex = agents.length > 8 ? Math.ceil(agents.length / 2) : agents.length;
  const leftRows = agents.slice(0, splitIndex);
  const rightRows = agents.slice(splitIndex);
  const liveLine = `${agents.length} AGENTS RANKED`;
  const tickerMessage = `PUSH TO ${dailyGoal} — TOP CLOSER TAKES THE BOARD`;

  const onCallCount = useMemo(() => {
    const rows = staffingReport?.rows ?? [];
    return rows.filter((row) => String(row.Status ?? '').toUpperCase() === 'INCALL').length;
  }, [staffingReport]);

  if (!authorized) return <div className="p-8 text-center text-[#f4f7fb]">You don&apos;t have access to this view.</div>;

  return (
    <div
      className="flex h-[calc(100dvh-4rem)] w-full min-w-0 min-h-0 flex-col overflow-hidden bg-white text-slate-700 dark:bg-slate-950 dark:text-cyan-100"
      style={{ fontFamily: 'Barlow, system-ui, sans-serif' }}
    >
      <div className="flex items-center justify-between border-b border-slate-300/60 bg-white px-4 py-3 text-slate-700 dark:border-cyan-400/20 dark:bg-slate-950 dark:text-cyan-100 sm:px-6">
        <div className="flex items-center gap-4 sm:gap-6" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
          <div className="font-semibold leading-none tracking-[0.18em] text-indigo-800 dark:text-cyan-300" style={{ fontSize: 'clamp(24px, 3.4vw, 54px)' }}>ALTRX</div>
        </div>
        <div className="flex items-center gap-3 sm:gap-6" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
          <div className="flex items-center gap-2 sm:gap-3">
            <div className="h-2.5 w-2.5 animate-pulse rounded-full bg-emerald-500 shadow-[0_0_12px_rgba(16,185,129,.8)] sm:h-3 sm:w-3" />
            <div className="font-semibold leading-none tracking-[0.28em] text-emerald-700 dark:text-emerald-400" style={{ fontSize: 'clamp(13px, 1.6vw, 22px)' }}>LIVE</div>
          </div>
          <div className="hidden font-medium uppercase leading-none tracking-[0.16em] text-slate-500 dark:text-cyan-200 sm:block" style={{ fontSize: 'clamp(11px, 1.4vw, 18px)' }}>{dateLabel}</div>
          <div className="font-mono font-semibold leading-none tracking-[0.06em] text-slate-900 dark:text-cyan-300" style={{ fontSize: 'clamp(18px, 2.4vw, 36px)' }}>{clock || '--:--:--'}</div>
        </div>
      </div>

      <div className="grid min-h-0 grid-cols-2 overflow-hidden border-b border-slate-300/60 bg-white text-slate-700 dark:border-cyan-400/15 dark:bg-slate-950 dark:text-cyan-100 lg:grid-cols-5">
        <div className="flex flex-col gap-2 overflow-hidden border-r border-slate-300/60 px-5 py-3 min-w-0 dark:border-cyan-400/15">
          <div className="flex min-w-0 items-baseline justify-between gap-2" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            <div className="font-semibold uppercase tracking-[0.2em] truncate text-slate-500 dark:text-cyan-200" style={{ fontSize: 'clamp(10px, 1.1vw, 15px)' }}>TEAM SALES TODAY</div>
            <div className="font-semibold uppercase tracking-[0.2em] truncate text-indigo-700 dark:text-cyan-300" style={{ fontSize: 'clamp(10px, 1.1vw, 15px)' }}>{goalPctLabel} OF GOAL</div>
          </div>
          <div className="flex min-w-0 items-end gap-2 leading-none dark:text-cyan-300" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            <div className="font-semibold tracking-[-0.02em] truncate text-indigo-800 dark:text-cyan-300" style={{ fontSize: 'clamp(32px, 4.6vw, 64px)' }}>{totals.sales}</div>
            <div className="font-normal text-slate-400 truncate dark:text-cyan-400/70" style={{ fontSize: 'clamp(14px, 1.6vw, 24px)' }}>/ {dailyGoal}</div>
            <div className="pb-1 font-semibold uppercase tracking-[0.18em] truncate text-slate-500 dark:text-cyan-200/80" style={{ fontSize: 'clamp(9px, 1vw, 13px)' }}>DAILY GOAL</div>
          </div>
          <div className="relative h-[6px] w-full overflow-hidden rounded-sm border border-slate-300 dark:border-cyan-300/40">
            <div
              className="absolute inset-y-0 left-0 bg-gradient-to-r from-indigo-500 to-blue-700 dark:from-cyan-400 dark:to-cyan-200"
              style={{ width: `${goalPct}%`, transition: 'width .7s ease' }}
            />
          </div>
        </div>
        <KpiCell
          label="REVENUE"
          value={formatCurrency(revenue)}
          titleClass="text-emerald-700 dark:text-emerald-300/80"
          valueClass="text-emerald-800 dark:text-emerald-400"
        />
        <KpiCell
          label="CALLS DIALED"
          value={totals.calls.toLocaleString('en-US')}
          titleClass="text-amber-700 dark:text-yellow-300/80"
          valueClass="text-amber-800 dark:text-yellow-300"
        />
        <KpiCell
          label="CONVERSION"
          value={`${conversion.toFixed(2)}%`}
          titleClass="text-purple-700 dark:text-rose-300/80"
          valueClass="text-rose-700 dark:text-rose-400"
        />
        <div className="flex min-w-0 flex-col justify-center gap-1 overflow-hidden px-5 py-3 min-w-0 lg:border-l lg:border-slate-300/60 dark:lg:border-cyan-400/15">
          <div className="flex items-center gap-2 font-semibold uppercase tracking-[0.2em] text-emerald-700 dark:text-emerald-300/80" style={{ fontSize: 'clamp(10px, 1.1vw, 15px)', fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            <span className="relative inline-flex h-2 w-2 shrink-0 rounded-full bg-emerald-500 dark:bg-emerald-400">
              <span className="absolute inset-0 animate-ping rounded-full bg-emerald-500/70 dark:bg-emerald-400/70" />
            </span>
            ON CALL
          </div>
          <div className="font-semibold leading-none text-emerald-700 dark:text-emerald-300" style={{ fontSize: 'clamp(24px, 3.4vw, 52px)', fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            {onCallCount}
          </div>
        </div>
      </div>

      <div
        className="grid min-h-0 flex-1 overflow-hidden bg-white text-slate-700 dark:bg-slate-950 dark:text-cyan-100"
        style={{
          display: 'grid',
          gridTemplateColumns: rightRows.length ? 'minmax(0, 1fr) minmax(0, 1fr)' : 'minmax(0, 1fr)',
        }}
      >
        <RankPanel rows={leftRows} side="left" startRank={1} flashId={flashId} />
        {rightRows.length ? (
          <RankPanel rows={rightRows} side="right" startRank={splitIndex + 1} flashId={flashId} />
        ) : null}
      </div>

      <div
        className="flex h-[32px] items-center justify-between border-t border-slate-300/60 bg-white px-3 text-slate-600 dark:border-cyan-400/20 dark:bg-slate-950 dark:text-cyan-300 sm:px-4"
        style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
      >
        <div className="truncate font-medium tracking-[0.2em]" style={{ fontSize: 'clamp(8px, 0.85vw, 11px)' }}>{liveLine}</div>
        <div className="hidden truncate font-medium tracking-[0.2em] text-slate-500 dark:text-cyan-200 md:block" style={{ fontSize: 'clamp(8px, 0.85vw, 11px)' }}>{tickerMessage}</div>
        <div className="truncate font-medium tracking-[0.2em]" style={{ fontSize: 'clamp(8px, 0.85vw, 11px)' }}>UPDATED {lastUpdated || clock || '--:--'}</div>
      </div>

      {tvSale ? <SaleAnnouncement sale={tvSale} /> : null}

      {isAdmin && (
        <DailyGoalEditor
          goal={dailyGoal}
          onSave={(value) => {
            setDailyGoal(value);
            saveDailyGoal(value);
          }}
        />
      )}
    </div>
  );
}

function KpiCell({ label, value, titleClass, valueClass }: { label: string; value: string; titleClass: string; valueClass: string }) {
  return (
    <div
      className="flex flex-col justify-center gap-1 overflow-hidden border-r border-slate-300/60 px-5 py-3 last:border-r-0 dark:border-cyan-400/15"
      style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
    >
      <div className={`font-semibold uppercase tracking-[0.2em] truncate ${titleClass}`} style={{ fontSize: 'clamp(10px, 1.1vw, 15px)' }}>{label}</div>
      <div className={`truncate font-semibold leading-none ${valueClass}`} style={{ fontSize: 'clamp(24px, 3.4vw, 52px)' }}>
        {value}
      </div>
    </div>
  );
}

function RankPanel({
  rows,
  side,
  startRank,
  flashId,
}: {
  rows: AgentRow[];
  side: 'left' | 'right';
  startRank: number;
  flashId: string | null;
}) {
  return (
    <div
      className={`flex min-w-0 flex-col overflow-hidden px-3 py-3 ${
        side === 'right' ? 'border-l border-slate-300/60 dark:border-cyan-400/20' : ''
      }`}
    >
      <div
        className="mb-2 grid items-center gap-2 px-2 font-semibold uppercase tracking-[0.18em] text-slate-500 dark:text-cyan-200"
        style={{
          gridTemplateColumns: '2.6rem minmax(0, 1fr) 3.6rem 3.6rem 3.6rem 4.6rem',
          fontFamily: 'Barlow Condensed, system-ui, sans-serif',
          fontSize: 'clamp(11px, 1vw, 14px)',
        }}
      >
        <div>RANK</div>
        <div>AGENT</div>
        <div className="text-center">SALES</div>
        <div className="text-right">CALLS</div>
        <div className="text-right">CONV</div>
        <div className="text-right">REVENUE</div>
      </div>
      <div className="flex min-h-0 flex-1 flex-col overflow-y-auto bg-white dark:bg-slate-950">
        {rows.length ? (
          rows.map((agent, index) => (
            <RankRow
              key={`${startRank}-${agent.name}-${index}`}
              agent={agent}
              rank={startRank + index}
              flash={flashId === agent.name}
              zebra={index % 2 === 1}
            />
          ))
        ) : (
          <div
            className="grid flex-1 place-items-center font-semibold uppercase tracking-[0.2em] text-slate-400 dark:text-cyan-400/70"
            style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif', fontSize: 'clamp(12px, 1.1vw, 14px)' }}
          >
            Awaiting data
          </div>
        )}
      </div>
    </div>
  );
}

function RankRow({ agent, rank, flash, zebra }: { agent: AgentRow; rank: number; flash: boolean; zebra: boolean }) {
  const lead = agent.totalSales > 0 && rank <= 3;
  const medal = ['#a16207', '#0e7490', '#9a3412'][rank - 1] ?? '#0e7490';
  const medalDark = ['#facc15', '#67e8f9', '#fb923c'][rank - 1] ?? '#67e8f9';
  const leadBg = ['bg-amber-100/80 dark:bg-yellow-400/15', 'bg-cyan-100/80 dark:bg-cyan-400/10', 'bg-orange-100/80 dark:bg-orange-400/15'][rank - 1] ?? '';
  const callsText = agent.callsHandled.toLocaleString('en-US');
  const conv = agent.callsHandled > 0 ? ((agent.totalSales / agent.callsHandled) * 100).toFixed(2) + '%' : '—';
  const revenue = formatCurrency(agent.revenue);
  const rowBg = lead ? leadBg : zebra ? 'bg-slate-100/70 dark:bg-cyan-400/[0.07]' : '';
  return (
    <div
      className={`grid h-[clamp(72px,7vw,96px)] items-center gap-2 border-b border-slate-300/80 border-l-[3px] px-2 dark:border-cyan-400/15 ${rowBg} ${flash ? 'animate-pulse' : ''}`}
      style={{
        borderLeftColor: lead ? medal : '#0e7490',
        animation: flash ? 'tvRankFlash 1.4s ease' : undefined,
        fontFamily: 'Barlow Condensed, system-ui, sans-serif',
        gridTemplateColumns: '2.6rem minmax(0, 1fr) 3.6rem 3.6rem 3.6rem 4.6rem',
      }}
    >
      <div
        className="flex items-center gap-1 font-bold leading-none"
        style={{ fontSize: 'clamp(22px, 2.2vw, 30px)', color: lead ? medal : '#0e7490' }}
      >
        {rank === 1 ? <CrownIcon /> : null}
        {rank}
      </div>
      <div className="flex min-w-0 items-center gap-3">
        <div
          className="grid h-10 w-10 shrink-0 place-items-center rounded-full font-bold bg-slate-100 border border-slate-300 text-indigo-700 dark:bg-slate-900/60 dark:border-cyan-400/40 dark:text-cyan-200"
          style={{ fontSize: 'clamp(13px, 1.2vw, 16px)' }}
        >
          {initials(agent.name)}
        </div>
        <div className="min-w-0 flex-1 truncate font-bold leading-tight text-slate-800 dark:text-cyan-100" style={{ fontSize: 'clamp(15px, 1.5vw, 21px)' }}>
          {agent.name}
        </div>
      </div>
      <div
        className="text-center font-bold leading-none text-indigo-700 dark:text-cyan-300"
        style={{ fontSize: 'clamp(17px, 1.7vw, 26px)' }}
      >
        {agent.totalSales}
      </div>
      <div className="text-right font-bold leading-none text-amber-700 dark:text-yellow-300" style={{ fontSize: 'clamp(15px, 1.4vw, 19px)' }}>
        {callsText}
      </div>
      <div className="text-right font-bold leading-none text-amber-800 dark:text-yellow-300" style={{ fontSize: 'clamp(15px, 1.4vw, 19px)' }}>
        {conv}
      </div>
      <div className="text-right font-bold leading-none text-emerald-700 dark:text-emerald-300" style={{ fontSize: 'clamp(15px, 1.5vw, 20px)' }}>
        {revenue}
      </div>
    </div>
  );
}

function CrownIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#f0b429" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="M11.562 3.266a.5.5 0 0 1 .876 0L15.39 8.87a1 1 0 0 0 1.516.294L21.183 5.5a.5.5 0 0 1 .798.519l-2.834 10.246a1 1 0 0 1-.956.734H5.81a1 1 0 0 1-.957-.734L2.02 6.02a.5.5 0 0 1 .798-.519l4.276 3.664a1 1 0 0 0 1.516-.294z" />
      <path d="M5 21h14" />
    </svg>
  );
}

function SaleAnnouncement({ sale }: { sale: TvSale }) {
  return (
    <div className="fixed inset-0 z-[120] grid place-items-center overflow-hidden bg-slate-900/95 text-slate-100 dark:bg-slate-950/95 dark:text-cyan-100">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(132,204,22,.18),transparent_55%)] dark:bg-[radial-gradient(circle_at_center,rgba(132,204,22,.18),transparent_55%)]" />
      <div className="absolute inset-2 border border-slate-300/60 dark:border-cyan-300/40 sm:inset-6" />
      <div className="relative max-w-[92vw] text-center">
        <div
          className="mb-4 font-semibold uppercase tracking-[0.4em] text-emerald-600 dark:text-cyan-300"
          style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif', fontSize: 'clamp(28px, 3.2vw, 56px)' }}
        >
          NEW SALE
        </div>
        <div
          className="font-semibold leading-[.9] text-slate-900 dark:text-cyan-100"
          style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif', fontSize: 'clamp(54px, 7vw, 110px)', letterSpacing: '-.01em' }}
        >
          {sale.salesRep}
        </div>
        <div
          className="mt-3 font-medium uppercase tracking-[0.22em] text-slate-600 dark:text-cyan-200"
          style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif', fontSize: 'clamp(18px, 1.8vw, 36px)' }}
        >
          #{sale.bundle} · {sale.todaysCount} SALES TODAY
        </div>
      </div>
    </div>
  );
}

function DailyGoalEditor({ goal, onSave }: { goal: number; onSave: (value: number) => void }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');
  const handleSave = () => {
    const parsed = num(draft);
    if (Number.isFinite(parsed) && parsed >= 0) onSave(parsed);
    setEditing(false);
    setDraft('');
  };
  return (
    <div className="fixed bottom-3 right-3 z-[110] flex items-center gap-2 sm:bottom-4 sm:right-4">
      {editing ? (
        <>
          <input
            type="number"
            min={0}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') handleSave();
              if (event.key === 'Escape') {
                setEditing(false);
                setDraft('');
              }
            }}
            placeholder={String(goal)}
            className="w-20 rounded border border-slate-300 bg-white px-2 py-1 text-right font-mono text-xs font-bold text-indigo-700 outline-none focus:ring-2 focus:ring-indigo-400 dark:border-cyan-400/60 dark:bg-slate-950 dark:text-cyan-100 sm:w-24 sm:text-sm"
          />
          <button
            type="button"
            onClick={handleSave}
            className="rounded bg-emerald-600 px-2 py-1 font-mono text-[10px] font-bold uppercase tracking-[0.2em] text-white shadow hover:bg-emerald-500 dark:bg-cyan-500 sm:px-3 sm:text-xs"
          >
            Save
          </button>
        </>
      ) : (
        <button
          type="button"
          onClick={() => {
            setEditing(true);
            setDraft(String(goal));
          }}
          className="rounded border border-slate-300 bg-white px-2 py-1 font-mono text-[10px] font-bold uppercase tracking-[0.2em] text-slate-700 shadow hover:border-emerald-500 dark:border-cyan-400/60 dark:bg-slate-950/80 dark:text-cyan-200 sm:px-3 sm:text-xs"
        >
          Edit goal ({goal})
        </button>
      )}
    </div>
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
