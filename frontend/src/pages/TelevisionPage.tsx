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
  status: string;
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

const FALLBACK_STATUSES = ['ON CALL', 'AVAILABLE', 'ON CALL', 'BREAK', 'AVAILABLE', 'ON CALL', 'AVAILABLE', 'ON CALL', 'BREAK', 'ON CALL', 'AVAILABLE', 'ON CALL', 'BREAK', 'ON CALL', 'ON CALL', 'BREAK', 'AVAILABLE', 'ON CALL', 'BREAK', 'OFFLINE'];

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
  const [clock, setClock] = useState('');
  const [flashId, setFlashId] = useState<string | null>(null);
  const saleTimeoutRef = useRef<number | null>(null);
  const flashTimeoutRef = useRef<number | null>(null);

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
    const performance = (report?.rows ?? []).map((row, index) => ({
      name: String(row.Name ?? row.User ?? ''),
      user: String(row.User ?? ''),
      outboundSales: 0,
      inboundSales: 0,
      totalSales: num(row.Form_Sales_Count),
      revenue: num(row.Form_Sales_Amount),
      callsHandled: num(row.Calls_Handled),
      status: FALLBACK_STATUSES[index % FALLBACK_STATUSES.length] ?? 'OFFLINE',
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
          status: FALLBACK_STATUSES[performance.length % FALLBACK_STATUSES.length] ?? 'OFFLINE',
        });
      }
    }
    return performance
      .filter((agent) => agent.name)
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
  const leftRows = agents.slice(0, 10);
  const rightRows = agents.slice(10, 20);
  const onCallCount = agents.filter((a) => a.status === 'ON CALL').length;
  const availableCount = agents.filter((a) => a.status === 'AVAILABLE').length;
  const breakCount = agents.filter((a) => a.status === 'BREAK').length;
  const liveLine = `${agents.length} AGENTS ONLINE · ${onCallCount} ON CALL · ${availableCount} AVAILABLE · ${breakCount} BREAK`;
  const tickerMessage = `PUSH TO ${dailyGoal} — TOP CLOSER TAKES THE BOARD`;

  if (!authorized) return <div className="p-8 text-center text-[#f4f7fb]">You don&apos;t have access to this view.</div>;

  return (
    <div
      className="flex h-[calc(100dvh-4rem)] w-full flex-col overflow-hidden bg-[#1f2c39] text-[#f2f2f3]"
      style={{ fontFamily: 'Barlow, system-ui, sans-serif' }}
    >
      <div className="flex items-center justify-between border-b border-[#f2f2f3]/[0.14] px-8 py-4">
        <div className="flex items-baseline gap-8" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
          <div className="text-[54px] font-semibold leading-none tracking-[0.18em] text-[#f2f2f3]">ALTRX</div>
          <div className="text-[30px] font-semibold leading-none tracking-[0.26em] text-[#94bce3]">SALES FLOOR</div>
        </div>
        <div className="flex items-center gap-8" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
          <div className="flex items-center gap-3">
            <div className="h-3 w-3 animate-pulse rounded-full bg-[#94bce3]" />
            <div className="text-[22px] font-semibold leading-none tracking-[0.28em] text-[#94bce3]">LIVE</div>
          </div>
          <div className="text-[22px] font-medium uppercase leading-none tracking-[0.16em] text-[#f2f2f3]/60">{dateLabel}</div>
          <div className="min-w-[215px] text-right font-mono text-[44px] font-semibold leading-none tracking-[0.06em] text-[#f2f2f3]">{clock || '--:--:--'}</div>
        </div>
      </div>

      <div className="grid grid-cols-2 border-b border-[#f2f2f3]/[0.14] lg:grid-cols-5">
        <div className="flex flex-col gap-3 border-r border-[#f2f2f3]/[0.16] px-8 py-4">
          <div className="flex items-baseline justify-between" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            <div className="text-[22px] font-semibold uppercase leading-none tracking-[0.2em] text-[#f2f2f3]/60">TEAM SALES TODAY</div>
            <div className="text-[22px] font-semibold uppercase leading-none tracking-[0.2em] text-[#94bce3]">{goalPctLabel} OF GOAL</div>
          </div>
          <div className="flex items-end gap-4 leading-none" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            <div className="text-[94px] font-semibold tracking-[-0.02em] text-[#f2f2f3]">{totals.sales}</div>
            <div className="text-[42px] font-normal text-[#f2f2f3]/50">/ {dailyGoal}</div>
            <div className="pb-2.5 text-[22px] font-semibold uppercase tracking-[0.18em] text-[#f2f2f3]/50">DAILY GOAL</div>
          </div>
          <div className="relative h-5 border border-[#f2f2f3]/[0.34]">
            <div
              className="absolute inset-y-0 left-0 bg-[#94bce3]"
              style={{ width: `${goalPct}%`, transition: 'width .7s ease' }}
            />
          </div>
        </div>
        <KpiCell label="REVENUE" value={formatCurrency(revenue)} accent="#94bce3" />
        <KpiCell label="CALLS DIALED" value={totals.calls.toLocaleString('en-US')} accent="#f2f2f3" />
        <KpiCell label="CONVERSION" value={`${conversion.toFixed(2)}%`} accent="#94bce3" />
        <div className="flex flex-col justify-center gap-1 px-6 py-4">
          <div className="text-[20px] font-semibold uppercase tracking-[0.2em] text-[#f2f2f3]/60" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            OUT / IN
          </div>
          <div className="flex items-baseline gap-3 leading-none" style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}>
            <div className="text-[60px] font-semibold text-[#f2f2f3]">{totals.outbound}</div>
            <div className="text-[34px] text-[#f2f2f3]/40">/</div>
            <div className="text-[60px] font-semibold text-[#94bce3]">{totals.inbound}</div>
          </div>
        </div>
      </div>

      <div className="flex min-h-0 flex-1 grid-cols-2" style={{ display: 'grid' }}>
        <RankPanel rows={leftRows} side="left" startRank={1} flashId={flashId} />
        <RankPanel rows={rightRows} side="right" startRank={11} flashId={flashId} />
      </div>

      <div
        className="flex h-[54px] items-center justify-between border-t border-[#f2f2f3]/[0.14] px-8"
        style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
      >
        <div className="text-[19px] font-medium tracking-[0.2em] text-[#f2f2f3]/55">{liveLine}</div>
        <div className="text-[19px] font-medium tracking-[0.2em] text-[#94bce3]">{tickerMessage}</div>
        <div className="text-[19px] font-medium tracking-[0.2em] text-[#f2f2f3]/55">UPDATED {lastUpdated || clock || '--:--'}</div>
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

function KpiCell({ label, value, accent }: { label: string; value: string; accent: string }) {
  return (
    <div
      className="flex flex-col justify-center gap-1 px-6 py-4"
      style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
    >
      <div className="text-[20px] font-semibold uppercase tracking-[0.2em] text-[#f2f2f3]/60">{label}</div>
      <div className="text-[60px] font-semibold leading-none" style={{ color: accent }}>
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
      className={`relative flex flex-col overflow-hidden px-4 py-4 ${
        side === 'right' ? 'border-l border-[#f2f2f3]/[0.16]' : ''
      }`}
    >
      <div
        className="mb-2 grid grid-cols-[56px_1fr_80px_84px_84px_116px] items-center gap-2 px-2 text-[17px] font-semibold uppercase tracking-[0.18em] text-[#f2f2f3]/55"
        style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
      >
        <div>RANK</div>
        <div>AGENT</div>
        <div className="text-center">SALES</div>
        <div className="text-right">CALLS</div>
        <div className="text-right">CONV</div>
        <div className="text-right">REVENUE</div>
      </div>
      <div className="flex flex-1 flex-col">
        {rows.length ? (
          rows.map((agent, index) => (
            <RankRow
              key={`${startRank}-${agent.name}-${index}`}
              agent={agent}
              rank={startRank + index}
              flash={flashId === agent.name}
            />
          ))
        ) : (
          <div
            className="grid flex-1 place-items-center text-[18px] font-semibold uppercase tracking-[0.2em] text-[#f2f2f3]/35"
            style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
          >
            Awaiting data
          </div>
        )}
      </div>
    </div>
  );
}

function RankRow({ agent, rank, flash }: { agent: AgentRow; rank: number; flash: boolean }) {
  const lead = agent.totalSales > 0 && rank <= 3;
  const zero = agent.totalSales === 0;
  const medal = ['#f0b429', '#cfd6dd', '#d98545'][rank - 1] ?? '#94bce3';
  const tint = ['rgba(240,180,41,.20)', 'rgba(207,214,221,.16)', 'rgba(217,133,69,.18)'][rank - 1] ?? 'rgba(137,168,201,.14)';
  const callsText = agent.callsHandled.toLocaleString('en-US');
  const conv = agent.callsHandled > 0 ? ((agent.totalSales / agent.callsHandled) * 100).toFixed(2) + '%' : '—';
  const revenue = formatCurrency(agent.revenue);
  return (
    <div
      className={`grid h-[68px] grid-cols-[56px_1fr_80px_84px_84px_116px] items-center gap-2 border-b border-[#f2f2f3]/[0.14] border-l-[3px] px-2 ${
        lead ? '' : zero ? '' : 'bg-[#89a8c9]/[0.14]'
      }`}
      style={{
        background: lead ? tint : zero ? 'transparent' : 'rgba(137,168,201,.14)',
        borderLeftColor: lead ? medal : zero ? 'rgba(242,242,243,.12)' : '#94bce3',
        animation: flash ? 'rankFlash 1.4s ease' : undefined,
        fontFamily: 'Barlow Condensed, system-ui, sans-serif',
      }}
    >
      <div
        className="flex items-center gap-1 font-semibold leading-none"
        style={{ fontSize: lead ? 30 : 26, color: lead ? medal : 'rgba(242,242,243,.4)' }}
      >
        {rank === 1 ? <CrownIcon /> : null}
        {rank}
      </div>
      <div className="flex min-w-0 items-center gap-2">
        <div
          className="grid h-9 w-9 shrink-0 place-items-center rounded-full font-semibold"
          style={{
            background: 'rgba(31,44,57,0.6)',
            border: `1px solid ${medal}33`,
            color: lead ? medal : 'rgba(242,242,243,.7)',
            fontSize: 14,
          }}
        >
          {initials(agent.name)}
        </div>
        <div className="min-w-0 flex-1">
          <div
            className="truncate font-semibold leading-tight"
            style={{
              fontSize: lead ? 22 : 19,
              color: zero ? 'rgba(242,242,243,.55)' : lead ? medal : '#f2f2f3',
            }}
          >
            #{agent.user || '----'} {agent.name}
          </div>
          <div className="text-[12px] uppercase tracking-[0.18em] text-[#f2f2f3]/45">
            {agent.status}
          </div>
        </div>
      </div>
      <div
        className="text-center font-semibold leading-none"
        style={{
          fontSize: lead ? 28 : 22,
          color: zero ? 'rgba(242,242,243,.3)' : lead ? medal : '#b5d9fd',
        }}
      >
        {agent.totalSales}
      </div>
      <div className="text-right font-semibold text-[18px] text-[#f2f2f3]/70 leading-none">
        {callsText}
      </div>
      <div className="text-right font-semibold text-[18px] text-[#f2f2f3]/70 leading-none">
        {conv}
      </div>
      <div
        className="text-right font-semibold leading-none"
        style={{
          fontSize: 20,
          color: zero ? 'rgba(242,242,243,.3)' : '#94bce3',
        }}
      >
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
    <div className="fixed inset-0 z-[120] grid place-items-center overflow-hidden bg-[#1f2c39]/95">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(132,204,22,.16),transparent_55%)]" />
      <div className="absolute inset-6 border border-[#94bce3]/60" />
      <div className="relative max-w-[92vw] text-center">
        <div
          className="mb-6 text-[56px] font-semibold uppercase tracking-[0.4em] text-[#94bce3]"
          style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
        >
          NEW SALE
        </div>
        <div
          className="font-semibold leading-[.9]"
          style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif', fontSize: 110, letterSpacing: '-.01em', color: '#f2f2f3' }}
        >
          {sale.salesRep}
        </div>
        <div
          className="mt-4 text-[36px] font-medium uppercase tracking-[0.22em] text-[#f2f2f3]/60"
          style={{ fontFamily: 'Barlow Condensed, system-ui, sans-serif' }}
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
    <div className="fixed bottom-4 right-4 z-[110] flex items-center gap-2">
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
            className="w-24 rounded border border-cyan-400/60 bg-[#1f2c39] px-2 py-1 text-right font-mono text-sm font-bold text-cyan-100 outline-none focus:ring-2 focus:ring-cyan-400"
          />
          <button
            type="button"
            onClick={handleSave}
            className="rounded bg-cyan-500 px-3 py-1 font-mono text-xs font-bold uppercase tracking-[0.2em] text-white shadow hover:bg-cyan-400"
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
          className="rounded border border-cyan-400/60 bg-[#1f2c39]/80 px-3 py-1 font-mono text-xs font-bold uppercase tracking-[0.2em] text-cyan-200 shadow hover:border-cyan-300"
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
