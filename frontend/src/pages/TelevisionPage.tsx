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

      <section className="grid h-[16vh] min-h-28 grid-cols-5 border-b border-[#d7dee8] dark:border-[#202b39]">
        <Metric label="Team sales today" value={String(totals.sales)} />
        <Metric label="Revenue" value={formatCurrency(totals.revenue)} />
        <Metric label="Calls dialed" value={totals.calls.toLocaleString('en-US')} />
        <Metric label="Conversion" value={`${conversion.toFixed(2)}%`} />
        <Metric label="Connected calls / On Call" value={`${totals.contacts} / ${onCall}`} />
      </section>

      {error ? <div className="border-b border-red-500/30 bg-red-500/10 px-5 py-2 font-metadata-mono text-sm text-red-300">{error}</div> : null}

      <section className="min-h-0 flex-1 grid grid-cols-1 lg:grid-cols-2">
        <AgentColumn agents={agents.slice(0, Math.ceil(agents.length / 2))} start={0} />
        <AgentColumn agents={agents.slice(Math.ceil(agents.length / 2))} start={Math.ceil(agents.length / 2)} second />
      </section>

      <footer className="flex h-[5vh] min-h-10 items-center justify-between border-t border-[#d7dee8] dark:border-[#202b39] px-[2vw] font-metadata-mono text-[clamp(.55rem,.75vw,.75rem)] uppercase tracking-[0.3em] text-[#111827] dark:text-white">
        <span>{agents.length} agents on leaderboard</span>
        <span className="text-[#111827] dark:text-white">Every sale moves the board</span>
        <span>Updated {lastUpdated || '--:--'}</span>
      </footer>
    </main>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col justify-center border-r border-[#d7dee8] dark:border-[#202b39] px-[1.5vw] last:border-r-0">
      <p className="font-metadata-mono text-[clamp(.55rem,.9vw,.9rem)] font-bold uppercase tracking-[0.25em] text-[#111827] dark:text-white">{label}</p>
      <p className={`mt-1 font-headline-lg text-[clamp(2rem,3.5vw,4rem)] font-black leading-none tracking-tight ${
        label === 'Team sales today' ? 'text-blue-600 dark:text-[#b7f52c]' :
        label === 'Revenue' ? 'text-green-600 dark:text-[#b7f52c]' :
        'text-[#111827] dark:text-white'
      }`}>{value}</p>
    </div>
  );
}

function AgentColumn({ agents, start, second = false }: { agents: AgentRow[]; start: number; second?: boolean }) {
  return (
    <div className={`min-h-0 flex flex-col px-3 ${second ? 'border-l border-[#d7dee8] dark:border-[#202b39]' : ''}`}>
      <div className="grid grid-cols-[2.5rem_minmax(0,1fr)_4.5rem_5rem_4.5rem] items-center gap-2 px-2 py-2 font-metadata-mono text-[clamp(.5rem,.7vw,.7rem)] uppercase tracking-[0.22em] text-[#111827] dark:text-white">
        <span>#</span><span>Agent</span><span className="text-right">Sales</span><span className="text-right">Calls</span><span className="text-right">Conv</span>
      </div>
      <div className="min-h-0 flex-1 grid auto-rows-fr gap-1 pb-3">
        {agents.length ? agents.map((agent, index) => <AgentCard key={`${agent.user}-${agent.name}`} agent={agent} rank={start + index + 1} />) : (
          <div className="grid place-items-center font-metadata-mono uppercase tracking-[0.25em] text-[#111827] dark:text-white">No agent data</div>
        )}
      </div>
    </div>
  );
}

function AgentCard({ agent, rank }: { agent: AgentRow; rank: number }) {
  const medal = rank === 1 ? 'border-l-[#ffc83d] bg-yellow-50 dark:bg-[#1b1911]' : rank === 2 ? 'border-l-[#d8e0e9] bg-slate-50 dark:bg-[#101721]' : rank === 3 ? 'border-l-[#d97a38] bg-orange-50 dark:bg-[#17140f]' : 'border-l-[#33465c] bg-white dark:bg-[#0d141d]';
  return (
    <article className={`grid min-h-0 grid-cols-[2.5rem_minmax(0,1fr)_4.5rem_5rem_4.5rem] items-center gap-2 border-b border-l-4 border-[#d7dee8] dark:border-[#202b39] px-2 ${medal}`}>
      <span className={`font-metadata-mono text-[clamp(1rem,1.6vw,1.6rem)] font-black ${rank <= 3 ? 'text-[#111827] dark:text-white' : 'text-[#111827] dark:text-white'}`}>{rank}</span>
      <div className="flex min-w-0 items-center gap-3">
        <span className="grid aspect-square w-[clamp(1.8rem,2.6vw,2.6rem)] shrink-0 place-items-center rounded-full bg-[#e5e7eb] dark:bg-[#1b2738] font-metadata-mono text-[clamp(.55rem,.75vw,.75rem)] font-black text-[#111827] dark:text-white">{initials(agent.name)}</span>
        <div className="min-w-0">
          <p className="truncate font-headline-lg text-[clamp(.8rem,1.25vw,1.25rem)] font-bold leading-tight">{agent.name}</p>
          <p className="truncate font-metadata-mono text-[clamp(.45rem,.6vw,.6rem)] uppercase tracking-[0.2em] text-[#111827] dark:text-white">{agent.user ? `#${agent.user}` : 'Sales agent'}</p>
        </div>
      </div>
      <span className="text-right font-metadata-mono text-[clamp(1rem,1.5vw,1.5rem)] font-black text-[#b7f52c]">{agent.formSales}</span>
      <span className="text-right font-metadata-mono text-[clamp(.7rem,1vw,1rem)] font-bold text-[#111827] dark:text-white">{agent.callsHandled.toLocaleString('en-US')}</span>
      <span className="text-right font-metadata-mono text-[clamp(.65rem,.9vw,.9rem)] font-bold text-[#111827] dark:text-white">{agent.conversion.toFixed(2)}%</span>
    </article>
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
