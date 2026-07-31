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

  const agents = useMemo<AgentRow[]>(() => {
    const rows = report?.rows ?? [];
    return rows.map(toAgentRow).filter((a) => a.formSales > 0 || a.vicidialSales > 0 || a.callsHandled > 0);
  }, [report]);

  const sortedAgents = useMemo(
    () => [...agents].sort((a, b) => b.formSales - a.formSales || a.name.localeCompare(b.name)),
    [agents],
  );

  const totals = useMemo(() => agents.reduce(
    (acc, a) => ({
      formSales: acc.formSales + a.formSales,
      formRevenue: acc.formRevenue + a.formRevenue,
      vicidialSales: acc.vicidialSales + a.vicidialSales,
    }),
    { formSales: 0, formRevenue: 0, vicidialSales: 0 },
  ), [agents]);

  const topAgent = sortedAgents[0];
  const activeSellers = sortedAgents.length;

  if (!authorized) {
    return (
      <div className="min-h-[60vh] flex items-center justify-center bg-canvas-white dark:bg-gray-950">
        <div className="max-w-md text-center">
          <span className="material-symbols-outlined text-muted-slate text-5xl mb-3 block">lock</span>
          <p className="text-secondary text-sm">No tienes acceso a esta vista.</p>
        </div>
      </div>
    );
  }

  const periodBtn = (p: Period) => (
    <button
      key={p}
      onClick={() => setPeriod(p)}
      className={`px-4 py-1.5 text-sm border-r border-whisper-border last:border-r-0 ${
        period === p
          ? 'bg-pure-surface text-primary font-medium'
          : 'text-secondary hover:bg-surface-container transition-colors'
      }`}
    >
      {p}
    </button>
  );

  return (
    <>
      <div className="flex flex-col md:flex-row justify-between items-start md:items-end gap-4 border-b border-whisper-border pb-4">
        <div>
          <h1 className="font-headline-lg text-headline-lg text-primary tracking-tight">
            TV — Sales Leaderboard
          </h1>
          <p className="text-secondary mt-1 flex items-center gap-2 text-sm">
            <span className="w-2 h-2 rounded-full bg-emerald-signal" />
            <span>
              Live from Analytics{lastUpdated && ` • Last updated: ${lastUpdated}`}
            </span>
          </p>
        </div>
        <div className="flex flex-col gap-2 items-end">
          <div className="flex gap-2 flex-wrap items-center">
            <div className="bg-surface-container-low border border-whisper-border rounded flex text-sm overflow-hidden">
              {periodBtn('Today')}
              {periodBtn('Week')}
              {periodBtn('Month')}
              {periodBtn('Custom')}
            </div>
            {period === 'Custom' && (
              <div className="flex gap-2 items-center bg-surface-container-low border border-whisper-border rounded px-3 py-1">
                <input
                  type="date"
                  value={customStart}
                  onChange={(e) => setCustomStart(e.target.value)}
                  className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
                />
                <span className="text-muted-slate text-xs">to</span>
                <input
                  type="date"
                  value={customEnd}
                  onChange={(e) => setCustomEnd(e.target.value)}
                  className="text-xs text-primary bg-transparent border-none outline-none w-[120px]"
                />
              </div>
            )}
            <button
              onClick={() => void refresh(true)}
              disabled={refreshing || loading}
              className="flex items-center gap-2 px-3 py-1.5 border border-whisper-border rounded bg-pure-surface text-secondary hover:text-primary transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed"
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

      <section>
        <h2 className="font-bold text-sm text-secondary uppercase tracking-wider font-metadata-mono mb-3">
          Operational Summary
        </h2>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <KpiCard title="Total Sales" value={totals.formSales} icon="confirmation_number" valueColor="var(--card-value-emerald)" loading={loading} />
          <KpiCard title="Revenue" value={totals.formRevenue} icon="payments" valueColor="var(--card-value-emerald)" loading={loading} isCurrency />
          <KpiCard title="Active Sellers" value={activeSellers} icon="groups" valueColor="var(--card-value-dark)" loading={loading} />
          <KpiCard
            title="Top Agent"
            value={topAgent ? `${topAgent.formSales} — ${topAgent.name}` : '—'}
            icon="workspace_premium"
            valueColor="#8B5CF6"
            loading={loading}
          />
        </div>
      </section>

      <section className="bg-pure-surface border border-whisper-border rounded-xl shadow-diffused overflow-hidden">
        <div className="p-6 border-b border-whisper-border flex flex-col sm:flex-row justify-between items-start sm:items-center gap-3">
          <div>
            <h3 className="font-bold text-lg text-primary">
              Sales by Agent
              <span className="text-sm font-normal text-secondary ml-2">(live)</span>
            </h3>
            <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
              Updates automatically when a sale is registered
            </p>
          </div>
          <span className="material-symbols-outlined text-electric-blue text-2xl">leaderboard</span>
        </div>
        {loading && agents.length === 0 ? (
          <div className="p-6 space-y-3 animate-pulse">
            {[1, 2, 3, 4, 5].map((i) => (
              <div key={i} className="h-9 bg-surface-container rounded" />
            ))}
          </div>
        ) : sortedAgents.length > 0 ? (
          <table className="w-full text-left text-sm border-collapse">
            <thead className="text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800">
              <tr>
                <th className="p-3 font-medium">#</th>
                <th className="p-3 font-medium">Agent</th>
                <th className="p-3 font-medium text-right">Form Sales</th>
                <th className="p-3 font-medium text-right">Form Revenue</th>
                <th className="p-3 font-medium text-right">VICI Sales</th>
                <th className="p-3 font-medium text-right">Calls Handled</th>
                <th className="p-3 font-medium text-right">Contacts</th>
                <th className="p-3 font-medium text-right">Conv %</th>
              </tr>
            </thead>
            <tbody>
              {sortedAgents.map((agent, i) => {
                const highlight = flash && agent.name === flash;
                return (
                  <tr
                    key={agent.name}
                    className={`border-b border-whisper-border transition-colors ${
                      highlight ? 'bg-electric-blue/10' : 'hover:bg-surface-container-lowest dark:hover:bg-gray-800/50'
                    }`}
                  >
                    <td className="p-3 font-metadata-mono text-secondary">{i + 1}</td>
                    <td className="p-3 font-medium text-primary">{agent.name}</td>
                    <td className="p-3 font-metadata-mono text-emerald-signal font-semibold">{agent.formSales}</td>
                    <td className="p-3 font-metadata-mono text-emerald-signal font-medium">{formatCurrency(agent.formRevenue)}</td>
                    <td className="p-3 font-metadata-mono">{agent.vicidialSales}</td>
                    <td className="p-3 font-metadata-mono">{agent.callsHandled}</td>
                    <td className="p-3 font-metadata-mono">{agent.contacts}</td>
                    <td className="p-3 font-metadata-mono font-medium">{agent.conversion ? `${agent.conversion.toFixed(1)}%` : '--'}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        ) : (
          <div className="p-12 text-sm text-muted-slate text-center flex flex-col items-center gap-2">
            <span className="material-symbols-outlined text-4xl text-muted-slate/40">leaderboard</span>
            <p className="font-medium text-primary">No agent sales in this period</p>
            <p className="text-xs">Click refresh to load data</p>
          </div>
        )}
      </section>
    </>
  );
}

function KpiCard({
  title, value, icon, valueColor = 'var(--card-value-dark)', loading, isCurrency,
}: {
  title: string;
  value: string | number;
  icon: string;
  valueColor?: string;
  loading?: boolean;
  isCurrency?: boolean;
}) {
  const numericValue = typeof value === 'number' ? value : 0;
  const displayValue = loading
    ? ''
    : isCurrency
      ? formatCurrency(numericValue)
      : String(value);

  return (
    <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-5 shadow-card transition-transform hover:scale-[1.01] relative">
      <div className="flex justify-between items-start mb-4">
        <p className="text-card-label text-[12px] font-medium">{title}</p>
        <div className="p-1.5 bg-card-icon-bg dark:bg-gray-800 rounded-md">
          <span className="material-symbols-outlined text-[16px] text-card-label">{icon}</span>
        </div>
      </div>
      {loading ? (
        <div className="h-7 w-24 bg-surface-container rounded animate-pulse" />
      ) : (
        <h2
          className="text-[1.6rem] font-bold leading-none tracking-tight truncate"
          style={{ color: valueColor }}
          title={String(value)}
        >
          {displayValue}
        </h2>
      )}
    </div>
  );
}