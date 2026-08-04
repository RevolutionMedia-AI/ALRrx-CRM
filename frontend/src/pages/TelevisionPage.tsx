import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import { getAgentPerformanceWithSales, getStaffing } from '../services/api';
import { readSharedToken } from '../utils/sharedToken';
import type { ReportDto } from '../types';

interface TvSale {
  salesRep: string;
  bundle: string;
  todaysCount: number;
}

interface AgentRow {
  name: string;
  user: string;
  formSales: number;
  formRevenue: number;
  callsHandled: number;
  contacts: number;
  conversion: number;
}

function number(value: unknown): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function toAgentRow(row: Record<string, unknown>): AgentRow {
  return {
    name: String(row.Name ?? row.User ?? ''),
    user: String(row.User ?? ''),
    formSales: number(row.Form_Sales_Count),
    formRevenue: number(row.Form_Sales_Amount),
    callsHandled: number(row.Calls_Handled),
    contacts: number(row.Contacts),
    conversion: number(row.Conversion_Percentage),
  };
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

function clampPercent(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, value));
}

export default function TelevisionPage() {
  const { has } = useAuth();
  const authorized = has('tv.view');
  const [report, setReport] = useState<ReportDto | null>(null);
  const [staffing, setStaffing] = useState<ReportDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState('');
  const [tvSale, setTvSale] = useState<TvSale | null>(null);
  const saleTimeoutRef = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    try {
      const [data, currentStaffing] = await Promise.all([
        getAgentPerformanceWithSales({ period: 'Today' }),
        getStaffing().catch(() => null),
      ]);
      setReport(data);
      setStaffing(currentStaffing);
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

  const agents = useMemo(() => (report?.rows ?? [])
    .map(toAgentRow)
    .filter((agent) => agent.name)
    .sort((a, b) => b.formSales - a.formSales || b.formRevenue - a.formRevenue || a.name.localeCompare(b.name)), [report]);

  const totals = useMemo(() => agents.reduce((result, agent) => ({
    sales: result.sales + agent.formSales,
    revenue: result.revenue + agent.formRevenue,
    calls: result.calls + agent.callsHandled,
    contacts: result.contacts + agent.contacts,
  }), { sales: 0, revenue: 0, calls: 0, contacts: 0 }), [agents]);

  const onCall = useMemo(() => (staffing?.rows ?? []).filter((row) => String(row.Status ?? '').toUpperCase() === 'INCALL').length, [staffing]);
  const conversion = totals.contacts ? (totals.sales / totals.contacts) * 100 : 0;
  const topPerformer = agents[0];

  if (!authorized) return <div className="p-8 text-center">You don't have access to this view.</div>;

  return (
    <main className="fixed inset-0 z-40 flex flex-col overflow-hidden bg-[#f8fafc] text-[#111827] dark:bg-[#080c12] dark:text-white">
      {tvSale ? <SaleAnnouncement sale={tvSale} /> : null}

      <header className="flex h-[9vh] min-h-16 items-center justify-between border-b border-[#d7dee8] dark:border-[#202b39] px-[2vw]">
        <div className="flex items-baseline gap-5">
          <span className="font-headline-lg text-[clamp(1.3rem,2vw,2.5rem)] font-black tracking-[0.18em]">ALTRX</span>
          <span className="font-metadata-mono text-[clamp(.65rem,1vw,1rem)] font-bold uppercase tracking-[0.35em] text-[#b7f52c]">Sales Floor</span>
        </div>
        <div className="flex items-center gap-5 font-metadata-mono text-[clamp(.6rem,.9vw,.9rem)] uppercase tracking-[0.25em] text-[#111827] dark:text-white">
          <span className="flex items-center gap-2 text-[#ff5364]"><i className="h-2 w-2 rounded-full bg-[#ff5364] animate-pulse" />Live</span>
          <span>{new Date().toLocaleDateString('en-US', { weekday: 'long', month: 'short', day: 'numeric' })}</span>
          <span className="text-[clamp(1rem,1.8vw,2rem)] font-black tracking-[0.1em] text-[#111827] dark:text-white">{lastUpdated || '--:--'}</span>
        </div>
      </header>

      <section className="grid h-[16vh] min-h-28 grid-cols-3 border-b border-[#d7dee8] dark:border-[#202b39]">
        <Metric label="Sales" value={totals.sales.toLocaleString('en-US')} accent="primary" />
        <Metric label="Conversion" value={`${conversion.toFixed(2)}%`} accent="cyan" />
        <Metric label="Revenue" value={formatCurrency(totals.revenue)} accent="primary" />
      </section>

      {error ? <div className="border-b border-red-500/30 bg-red-500/10 px-5 py-2 font-metadata-mono text-sm text-red-300">{error}</div> : null}

      <section className="min-h-0 flex-1 grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,22rem)]">
        <div className="grid min-h-0 grid-cols-1 lg:grid-cols-2">
          <AgentColumn agents={agents.slice(0, Math.ceil(agents.length / 2))} start={0} totalSales={totals.sales} />
          <AgentColumn agents={agents.slice(Math.ceil(agents.length / 2))} start={Math.ceil(agents.length / 2)} second totalSales={totals.sales} />
        </div>
        <aside className="flex min-h-0 flex-col gap-3 overflow-y-auto border-t border-[#d7dee8] bg-[#f8fafc] p-3 dark:border-[#202b39] dark:bg-[#080c12] lg:border-l lg:border-t-0">
          <TopPerformerCard agent={topPerformer} totalSales={totals.sales} onCall={onCall} />
          <DailyGoalCard current={totals.sales} />
        </aside>
      </section>

      <footer className="flex h-[5vh] min-h-10 items-center justify-between border-t border-[#d7dee8] dark:border-[#202b39] px-[2vw] font-metadata-mono text-[clamp(.55rem,.75vw,.75rem)] uppercase tracking-[0.3em] text-[#111827] dark:text-white">
        <span>{agents.length} agents on leaderboard</span>
        <span className="text-[#111827] dark:text-white">Every sale moves the board</span>
        <span>Updated {lastUpdated || '--:--'}</span>
      </footer>
    </main>
  );
}

function Metric({ label, value, accent }: { label: string; value: string; accent: 'primary' | 'cyan' }) {
  const valueClass = accent === 'primary'
    ? 'text-blue-600 dark:text-[#b7f52c]'
    : 'text-cyan-600 dark:text-[#7ee8fa]';
  return (
    <div className="flex flex-col justify-center border-r border-[#d7dee8] dark:border-[#202b39] px-[1.5vw] last:border-r-0">
      <p className="font-metadata-mono text-[clamp(.55rem,.9vw,.9rem)] font-bold uppercase tracking-[0.25em] text-[#111827] dark:text-white">{label}</p>
      <p className={`mt-1 font-headline-lg text-[clamp(2rem,3.5vw,4rem)] font-black leading-none tracking-tight ${valueClass}`}>{value}</p>
    </div>
  );
}

function AgentColumn({ agents, start, second = false, totalSales }: { agents: AgentRow[]; start: number; second?: boolean; totalSales: number }) {
  return (
    <div className={`flex min-h-0 min-w-0 flex-col px-3 ${second ? 'border-t border-[#d7dee8] dark:border-[#202b39] lg:border-l lg:border-t-0' : ''}`}>
      <div className="grid w-full max-w-[48rem] grid-cols-[2.5rem_minmax(0,1fr)_4.5rem_4rem_4.5rem_5rem] items-center gap-2 px-2 py-2 font-metadata-mono text-[clamp(.5rem,.7vw,.7rem)] uppercase tracking-[0.22em] text-[#111827] dark:text-white">
        <span>Pos</span>
        <span>Agent</span>
        <span className="text-right">Outbound</span>
        <span className="text-right">Inbound</span>
        <span className="text-right">Total</span>
        <span className="text-right">% Total</span>
      </div>
      <div className="min-h-0 flex-1 grid auto-rows-fr gap-1 pb-3">
        {agents.length ? agents.map((agent, index) => (
          <AgentCard key={`${agent.user}-${agent.name}`} agent={agent} rank={start + index + 1} totalSales={totalSales} />
        )) : (
          <div className="grid place-items-center font-metadata-mono uppercase tracking-[0.25em] text-[#111827] dark:text-white">No agent data</div>
        )}
      </div>
    </div>
  );
}

function AgentCard({ agent, rank, totalSales }: { agent: AgentRow; rank: number; totalSales: number }) {
  const medal = rank === 1
    ? 'border-l-[#ffc83d] bg-yellow-50 dark:bg-[#1b1911]'
    : rank === 2
      ? 'border-l-[#aeb4bd] bg-[#d9dde3] dark:bg-[#101721]'
      : rank === 3
        ? 'border-l-[#d97a38] bg-orange-50 dark:bg-[#17140f]'
        : 'border-l-[#33465c] bg-white dark:bg-[#0d141d]';
  const pct = totalSales ? (agent.formSales / totalSales) * 100 : 0;
  const inboundDisplay = '--';
  return (
    <article className={`relative flex min-h-0 w-full max-w-[48rem] flex-col border-b border-l-4 border-[#d7dee8] dark:border-[#202b39] ${medal}`}>
      <div className="grid grid-cols-[2.5rem_minmax(0,1fr)_4.5rem_4rem_4.5rem_5rem] items-center gap-2 px-2 pt-2">
        <span className="font-metadata-mono text-[clamp(1rem,1.6vw,1.6rem)] font-black text-[#111827] dark:text-white">{rank}</span>
        <div className="flex min-w-0 items-center gap-3">
          <span className="grid aspect-square w-[clamp(1.8rem,2.6vw,2.6rem)] shrink-0 place-items-center rounded-full bg-[#e5e7eb] dark:bg-[#1b2738] font-metadata-mono text-[clamp(.55rem,.75vw,.75rem)] font-black text-[#111827] dark:text-white">{initials(agent.name)}</span>
          <div className="min-w-0">
            <p className="truncate font-headline-lg text-[clamp(.8rem,1.25vw,1.25rem)] font-bold leading-tight">{agent.name}</p>
            <p className="truncate font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.2em] text-[#111827] dark:text-white">{agent.user ? `#${agent.user}` : 'Sales agent'}</p>
          </div>
        </div>
        <span className="text-right font-metadata-mono text-[clamp(1rem,1.5vw,1.5rem)] font-black text-purple-600 dark:text-[#b7f52c]">{agent.formSales}</span>
        <span className="text-right font-metadata-mono text-[clamp(1rem,1.5vw,1.5rem)] font-bold text-[#111827]/50 dark:text-white/50">{inboundDisplay}</span>
        <span className="text-right font-metadata-mono text-[clamp(1rem,1.5vw,1.5rem)] font-black text-[#111827] dark:text-white">{agent.formSales}</span>
        <span className="text-right font-metadata-mono text-[clamp(.65rem,.9vw,.9rem)] font-bold text-[#111827] dark:text-white">{pct.toFixed(1)}%</span>
      </div>
      <div className="relative mt-1.5 h-1 w-full overflow-hidden bg-[#e5e7eb]/60 dark:bg-[#1b2738]/60">
        <div
          className="h-full bg-gradient-to-r from-[#b7f52c] via-[#7ee8fa] to-[#7ee8fa] transition-[width] duration-700"
          style={{ width: `${clampPercent(pct)}%` }}
        />
      </div>
    </article>
  );
}

function TopPerformerCard({ agent, totalSales, onCall }: { agent: AgentRow | undefined; totalSales: number; onCall: number }) {
  const pct = agent && totalSales ? (agent.formSales / totalSales) * 100 : 0;
  return (
    <section className="relative overflow-hidden rounded-lg border border-[#d7dee8] bg-white p-4 shadow-sm dark:border-[#202b39] dark:bg-[#0d141d]">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(183,245,44,.12),transparent_55%)] dark:bg-[radial-gradient(circle_at_top_right,rgba(183,245,44,.18),transparent_55%)]" />
      <div className="relative flex items-center justify-between">
        <p className="font-metadata-mono text-[clamp(.55rem,.85vw,.85rem)] font-black uppercase tracking-[0.3em] text-[#b7f52c]">Top Performer</p>
        <span className="font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.25em] text-[#111827]/60 dark:text-white/60">Rank #1</span>
      </div>
      {agent ? (
        <>
          <div className="relative mt-3 flex items-center gap-3">
            <span className="grid aspect-square w-[clamp(2.6rem,3.6vw,3.6rem)] shrink-0 place-items-center rounded-full bg-gradient-to-br from-[#b7f52c] to-[#7ee8fa] font-metadata-mono text-[clamp(.7rem,.9vw,.9rem)] font-black text-[#080c12]">{initials(agent.name)}</span>
            <div className="min-w-0">
              <p className="truncate font-headline-lg text-[clamp(1rem,1.6vw,1.6rem)] font-black leading-tight">{agent.name}</p>
              <p className="truncate font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.22em] text-[#111827]/70 dark:text-white/70">{agent.user ? `#${agent.user}` : 'Sales agent'}</p>
            </div>
          </div>
          <div className="relative mt-4 grid grid-cols-3 gap-2">
            <Stat label="Sales" value={String(agent.formSales)} accent="primary" />
            <Stat label="Conv" value={`${agent.conversion.toFixed(1)}%`} />
            <Stat label="Calls" value={agent.callsHandled.toLocaleString('en-US')} />
          </div>
          <div className="relative mt-3">
            <div className="flex items-center justify-between font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.22em] text-[#111827]/70 dark:text-white/70">
              <span>Share of Team</span>
              <span>{pct.toFixed(1)}%</span>
            </div>
            <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-[#e5e7eb] dark:bg-[#1b2738]">
              <div className="h-full bg-gradient-to-r from-[#b7f52c] to-[#7ee8fa]" style={{ width: `${clampPercent(pct)}%` }} />
            </div>
          </div>
          <p className="relative mt-3 font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.22em] text-[#111827]/50 dark:text-white/50">{onCall} rep{onCall === 1 ? '' : 's'} on a call now</p>
        </>
      ) : (
        <div className="relative mt-4 grid place-items-center py-6 font-metadata-mono uppercase tracking-[0.25em] text-[#111827]/60 dark:text-white/60">Awaiting data…</div>
      )}
    </section>
  );
}

function Stat({ label, value, accent }: { label: string; value: string; accent?: 'primary' }) {
  const valueClass = accent === 'primary' ? 'text-blue-600 dark:text-[#b7f52c]' : 'text-[#111827] dark:text-white';
  return (
    <div className="rounded-md border border-[#d7dee8] bg-[#f8fafc] px-2 py-1.5 dark:border-[#202b39] dark:bg-[#080c12]">
      <p className="font-metadata-mono text-[clamp(.4rem,.55vw,.55rem)] uppercase tracking-[0.22em] text-[#111827]/60 dark:text-white/60">{label}</p>
      <p className={`font-headline-lg text-[clamp(1rem,1.5vw,1.5rem)] font-black leading-none ${valueClass}`}>{value}</p>
    </div>
  );
}

function DailyGoalCard({ current }: { current: number }) {
  const [goalTarget] = useState<number | null>(null);
  const progress = goalTarget ? clampPercent((current / goalTarget) * 100) : 0;
  return (
    <section className="relative overflow-hidden rounded-lg border border-[#d7dee8] bg-white p-4 shadow-sm dark:border-[#202b39] dark:bg-[#0d141d]">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_left,rgba(126,232,250,.12),transparent_55%)] dark:bg-[radial-gradient(circle_at_top_left,rgba(126,232,250,.18),transparent_55%)]" />
      <div className="relative flex items-center justify-between">
        <p className="font-metadata-mono text-[clamp(.55rem,.85vw,.85rem)] font-black uppercase tracking-[0.3em] text-[#7ee8fa]">Daily Goal</p>
        <span className="font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.25em] text-[#111827]/60 dark:text-white/60">Today</span>
      </div>
      <div className="relative mt-3 flex items-end justify-between">
        <span className="font-metadata-mono text-[clamp(.5rem,.7vw,.7rem)] uppercase tracking-[0.22em] text-[#111827]/70 dark:text-white/70">Sales Today</span>
        <span className="font-headline-lg text-[clamp(2rem,3vw,3rem)] font-black leading-none text-[#111827] dark:text-white">{current.toLocaleString('en-US')}</span>
      </div>
      <div className="relative mt-2 flex items-end justify-between">
        <span className="font-metadata-mono text-[clamp(.5rem,.7vw,.7rem)] uppercase tracking-[0.22em] text-[#111827]/70 dark:text-white/70">Target</span>
        <span className="font-metadata-mono text-[clamp(1.2rem,1.8vw,1.8rem)] font-bold text-[#111827]/50 dark:text-white/50">{goalTarget === null ? '--' : goalTarget.toLocaleString('en-US')}</span>
      </div>
      <div className="relative mt-3 h-2 w-full overflow-hidden rounded-full bg-[#e5e7eb] dark:bg-[#1b2738]">
        <div
          className="h-full bg-gradient-to-r from-[#7ee8fa] via-[#b7f52c] to-[#b7f52c] transition-[width] duration-700"
          style={{ width: `${progress}%` }}
        />
      </div>
      <p className="relative mt-3 font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.22em] text-[#111827]/50 dark:text-white/50">
        {goalTarget === null ? 'Configure daily goal in admin' : `${((current / goalTarget) * 100).toFixed(1)}% of target`}
      </p>
    </section>
  );
}

function SaleAnnouncement({ sale }: { sale: TvSale }) {
  return (
    <div className="fixed inset-0 z-[100] grid place-items-center overflow-hidden bg-[#f8fafc] text-center text-[#111827] dark:bg-[#080c12] dark:text-white">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(183,245,44,.16),transparent_52%)] dark:bg-[radial-gradient(circle_at_center,rgba(183,245,44,.14),transparent_52%)]" />
      <div className="absolute inset-6 border border-[#b7f52c]/30 dark:border-[#b7f52c]/20" />
      <div className="relative max-w-[92vw]">
        <p className="mb-8 font-metadata-mono text-[clamp(1rem,2vw,2rem)] font-black uppercase tracking-[0.55em] text-[#b7f52c]">New Sale</p>
        <h2 className="font-headline-lg text-[clamp(4rem,11vw,10rem)] font-black leading-[.85] tracking-tight text-[#111827] dark:text-white">{sale.salesRep}</h2>
        <p className="mt-10 font-metadata-mono text-[clamp(1rem,2.2vw,2.25rem)] uppercase tracking-[0.25em] text-[#111827] dark:text-white">{sale.bundle}</p>
        <p className="mt-5 font-metadata-mono text-[clamp(1rem,2vw,2rem)] uppercase tracking-[0.3em] text-[#b7f52c]">{sale.todaysCount} {sale.todaysCount === 1 ? 'sale' : 'sales'} today</p>
      </div>
    </div>
  );
}
