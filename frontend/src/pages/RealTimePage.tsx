import { useEffect, useState, useCallback, useMemo, useRef } from 'react';
import {
  getDashboardSummary, getStaffing, getLeaderboard, getQueueMetrics, getAgentHistory,
  exportDashboardPdf, exportDashboardExcel,
} from '../services/api';
import { getVicidialLeadById } from '../services/vicidialFormApi';
import type { DashboardSummaryDto, ReportDto, TimeFilterDto, VicidialLeadDto } from '../types';
import { extractErrorMessage } from '../utils/extractErrorMessage';

type AgentStatus = 'READY' | 'INCALL' | 'QUEUE' | 'PAUSED' | 'OFFLINE';
type CallDirection = 'inbound' | 'outbound' | 'unknown';
type StatusFilter = 'all' | AgentStatus;
type SortKey = 'name' | 'status' | 'lastCall' | 'idleSince';
type SortDir = 'asc' | 'desc';

const STATUS_COLORS: Record<AgentStatus, { bg: string; text: string; dot: string; label: string }> = {
  READY:    { bg: 'bg-emerald-signal/8', text: 'text-emerald-signal', dot: 'bg-emerald-signal', label: 'Available' },
  INCALL:   { bg: 'bg-electric-blue/8', text: 'text-electric-blue', dot: 'bg-electric-blue', label: 'On Call' },
  QUEUE:    { bg: 'bg-amber-warmth/8', text: 'text-amber-warmth', dot: 'bg-amber-warmth', label: 'In Queue' },
  PAUSED:   { bg: 'bg-deep-rose/8', text: 'text-deep-rose', dot: 'bg-deep-rose', label: 'Paused' },
  OFFLINE:  { bg: 'bg-muted-slate/8', text: 'text-muted-slate', dot: 'bg-muted-slate', label: 'Offline' },
};

const PAUSE_REASON_LABELS: Record<string, string> = {
  BREAK: 'Break', LUNCH: 'Lunch', BIO: 'Bio', TRAINING: 'Training', ADMIN: 'Admin',
  TRAINING2: 'Training 2', MEETING: 'Meeting', CALLBK: 'Callback', DNC: 'DNC',
  EMAIL: 'Email', FAX: 'Fax', CHAT: 'Chat', SYSTEM: 'System', VACATION: 'Vacation',
  SICK: 'Sick', HOLIDAY: 'Holiday', OOO: 'Out of Office', PUMP: 'Pump', AFK: 'AFK',
};

const DIAL_METHOD_LABELS: Record<string, string> = {
  MANUAL: 'Manual', RATIO: 'Ratio', ADAPT_AVERAGE: 'Adaptive',
  ADAPT_HARDLIMIT: 'Adaptive+', INBOUND_MAN: 'Inbound', AUTO_DIAL: 'Auto',
};

const REFRESH_INTERVAL_MS = 10_000;
const LONG_PAUSE_THRESHOLD_SEC = 15 * 60;

function getInitials(name: string): string {
  if (!name) return '--';
  return name.split(' ').map((n) => n[0]).join('').substring(0, 2).toUpperCase();
}

function elapsed(isoDate: string | null): string {
  if (!isoDate) return '';
  const diff = Math.floor((Date.now() - new Date(isoDate).getTime()) / 1000);
  if (diff < 0) return '0s';
  if (diff < 60) return `${diff}s`;
  const m = Math.floor(diff / 60);
  const s = diff % 60;
  if (m < 60) return `${m}m ${s}s`;
  const h = Math.floor(m / 60);
  return `${h}h ${m % 60}m`;
}

function getPauseReasonLabel(code: string | null): string {
  if (!code) return '';
  return PAUSE_REASON_LABELS[code] ?? code;
}

function getDialMethodLabel(method: string | null): string {
  if (!method) return '';
  return DIAL_METHOD_LABELS[method] ?? method;
}

function inferDirection(campaignName: string | null, dialMethod: string | null): CallDirection {
  const n = (campaignName ?? '').toUpperCase();
  const m = (dialMethod ?? '').toUpperCase();
  if (n.includes('IN') || n.includes('INBOUND') || m.includes('INBOUND')) return 'inbound';
  if (m.includes('MANUAL') || m.includes('AUTO_DIAL') || m.includes('RATIO') || m.includes('ADAPT')) return 'outbound';
  return 'unknown';
}

function safeNumber(v: unknown, fallback = 0): number {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency', currency: 'USD', minimumFractionDigits: 0, maximumFractionDigits: 0,
  }).format(amount);
}

interface AgentRow {
  Supervisor: string;
  Emp_Number: string;
  Name: string;
  User: string;
  Status: string;
  Current_Lead_Id: number | null;
  Current_Campaign_Id: string | null;
  Current_Campaign_Name: string | null;
  Dial_Method: string | null;
  Pause_Code: string | null;
  last_call_time: string | null;
  last_update_time: string | null;
}

interface LeadLookupState {
  loading: boolean;
  data: VicidialLeadDto | null;
  error: string | null;
}

interface AgentHistoryState {
  open: boolean;
  loading: boolean;
  data: ReportDto | null;
  error: string | null;
  agentName: string;
}

export default function RealTimePage() {
  const [staffing, setStaffing] = useState<ReportDto | null>(null);
  const [leaderboard, setLeaderboard] = useState<ReportDto | null>(null);
  const [queueMetrics, setQueueMetrics] = useState<ReportDto | null>(null);
  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string>('');
  const [secondsToRefresh, setSecondsToRefresh] = useState(REFRESH_INTERVAL_MS / 1000);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [exportingExcel, setExportingExcel] = useState(false);
  const [exportingPdf, setExportingPdf] = useState(false);

  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [directionFilter, setDirectionFilter] = useState<CallDirection | 'all'>('all');
  const [supervisorFilter, setSupervisorFilter] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState('');
  const [sortKey, setSortKey] = useState<SortKey>('status');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  const [leadLookup, setLeadLookup] = useState<Record<number, LeadLookupState>>({});
  const [agentHistory, setAgentHistory] = useState<AgentHistoryState>({
    open: false, loading: false, data: null, error: null, agentName: '',
  });

  const tickRef = useRef<number | null>(null);

  const filter = (): TimeFilterDto => ({ period: 'Today' });

  const fetchData = useCallback(async () => {
    try {
      setError(null);
      const f = filter();
      const [st, lb, qm, sm] = await Promise.all([
        getStaffing().catch(() => null),
        getLeaderboard(f).catch(() => null),
        getQueueMetrics(f).catch(() => null),
        getDashboardSummary(f).catch(() => null),
      ]);
      setStaffing(st);
      setLeaderboard(lb);
      setQueueMetrics(qm);
      setSummary(sm);
      setLastUpdated(new Date().toLocaleTimeString());
      setSecondsToRefresh(REFRESH_INTERVAL_MS / 1000);
    } catch (err) {
      setError(extractErrorMessage(err, 'Failed to load live data'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);

  useEffect(() => {
    if (!autoRefresh) {
      if (tickRef.current) window.clearInterval(tickRef.current);
      return;
    }
    tickRef.current = window.setInterval(() => {
      setSecondsToRefresh((s) => {
        if (s <= 1) {
          fetchData();
          return REFRESH_INTERVAL_MS / 1000;
        }
        return s - 1;
      });
    }, 1000);
    return () => {
      if (tickRef.current) window.clearInterval(tickRef.current);
    };
  }, [autoRefresh, fetchData]);

  const agents = (staffing?.rows ?? []) as unknown as AgentRow[];
  const supervisors = useMemo(() => {
    const set = new Set<string>();
    agents.forEach((a) => { if (a.Supervisor) set.add(a.Supervisor); });
    return Array.from(set).sort();
  }, [agents]);

  const enrichedAgents = useMemo(() => {
    return agents.map((a) => ({
      ...a,
      Direction: inferDirection(a.Current_Campaign_Name, a.Dial_Method),
      Status: ((a.Status ?? 'OFFLINE').toUpperCase()) as AgentStatus,
    }));
  }, [agents]);

  const statusCounts = useMemo(() => {
    const counts: Record<AgentStatus, number> = { READY: 0, INCALL: 0, QUEUE: 0, PAUSED: 0, OFFLINE: 0 };
    enrichedAgents.forEach((a) => { counts[a.Status] = (counts[a.Status] ?? 0) + 1; });
    return counts;
  }, [enrichedAgents]);

  const filteredAgents = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return enrichedAgents.filter((a) => {
      if (statusFilter !== 'all' && a.Status !== statusFilter) return false;
      if (directionFilter !== 'all' && a.Direction !== directionFilter) return false;
      if (supervisorFilter !== 'all' && a.Supervisor !== supervisorFilter) return false;
      if (q && !`${a.Name ?? ''} ${a.User ?? ''} ${a.Emp_Number ?? ''}`.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [enrichedAgents, statusFilter, directionFilter, supervisorFilter, searchQuery]);

  const sortedAgents = useMemo(() => {
    const STATUS_ORDER: Record<AgentStatus, number> = { INCALL: 0, QUEUE: 1, READY: 2, PAUSED: 3, OFFLINE: 4 };
    const arr = [...filteredAgents];
    arr.sort((a, b) => {
      let av: string | number = '';
      let bv: string | number = '';
      switch (sortKey) {
        case 'name': av = a.Name ?? a.User ?? ''; bv = b.Name ?? b.User ?? ''; break;
        case 'status': av = STATUS_ORDER[a.Status]; bv = STATUS_ORDER[b.Status]; break;
        case 'lastCall':
          av = a.last_call_time ? new Date(a.last_call_time).getTime() : 0;
          bv = b.last_call_time ? new Date(b.last_call_time).getTime() : 0;
          break;
        case 'idleSince':
          av = a.last_update_time ? new Date(a.last_update_time).getTime() : 0;
          bv = b.last_update_time ? new Date(b.last_update_time).getTime() : 0;
          break;
      }
      if (typeof av === 'string' && typeof bv === 'string') {
        return sortDir === 'asc' ? av.localeCompare(bv) : bv.localeCompare(av);
      }
      return sortDir === 'asc'
        ? (av as number) - (bv as number)
        : (bv as number) - (av as number);
    });
    return arr;
  }, [filteredAgents, sortKey, sortDir]);

  const totalOnline = statusCounts.READY + statusCounts.INCALL + statusCounts.QUEUE + statusCounts.PAUSED;
  const totalSales = summary?.metrics.find((m) => m.label.toLowerCase().includes('sales today'))?.value ?? '--';
  const totalCalls = summary?.metrics.find((m) => m.label.toLowerCase().includes('total calls'))?.value ?? '--';
  const contacts = summary?.metrics.find((m) => m.label.toLowerCase().includes('contacts'))?.value ?? '--';
  const occupancy = summary?.metrics.find((m) => m.label.toLowerCase().includes('occupancy'))?.value ?? '--%';
  const leadsDialed = summary?.metrics.find((m) => m.label.toLowerCase().includes('leads dialed'))?.value ?? '--';
  const leadsContacted = summary?.metrics.find((m) => m.label.toLowerCase().includes('leads contacted'))?.value ?? '--';

  const queueRow = queueMetrics?.rows?.[0];
  const queueDepth = safeNumber(queueRow?.Queue_Depth, 0);
  const totalInboundCalls = safeNumber(queueRow?.Total_Inbound_Calls, 0);
  const callsUnder20s = safeNumber(queueRow?.Calls_Under_20s, 0);
  const abandoned = safeNumber(queueRow?.Abandoned, 0);
  const serviceLevelPct = totalInboundCalls > 0 ? (callsUnder20s / totalInboundCalls) * 100 : null;
  const abandonRatePct = totalInboundCalls > 0 ? (abandoned / totalInboundCalls) * 100 : null;

  const lookupLead = useCallback(async (leadId: number) => {
    setLeadLookup((prev) => ({ ...prev, [leadId]: { loading: true, data: prev[leadId]?.data ?? null, error: null } }));
    try {
      const lead = await getVicidialLeadById(leadId);
      setLeadLookup((prev) => ({ ...prev, [leadId]: { loading: false, data: lead, error: null } }));
    } catch (err) {
      setLeadLookup((prev) => ({
        ...prev, [leadId]: { loading: false, data: prev[leadId]?.data ?? null, error: extractErrorMessage(err, 'Lead not found') }
      }));
    }
  }, []);

  const openAgentHistory = useCallback(async (agent: AgentRow) => {
    setAgentHistory({ open: true, loading: true, data: null, error: null, agentName: agent.Name ?? agent.User });
    try {
      const data = await getAgentHistory(agent.User, filter());
      setAgentHistory({ open: true, loading: false, data, error: null, agentName: agent.Name ?? agent.User });
    } catch (err) {
      setAgentHistory({ open: true, loading: false, data: null, error: extractErrorMessage(err, 'Could not load history'), agentName: agent.Name ?? agent.User });
    }
  }, []);

  const closeAgentHistory = useCallback(() => {
    setAgentHistory({ open: false, loading: false, data: null, error: null, agentName: '' });
  }, []);

  const handleSort = (key: SortKey) => {
    if (sortKey === key) setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    else { setSortKey(key); setSortDir('asc'); }
  };

  return (
    <>
      <div className="flex flex-col lg:flex-row justify-between items-start lg:items-end gap-4 border-b border-whisper-border pb-5">
        <div>
          <h1 className="font-headline-lg text-headline-lg font-bold text-primary tracking-tight">Real-Time Report — ALTRX</h1>
          <p className="text-secondary text-sm mt-1">
            Live agent monitoring and queue metrics
            {lastUpdated && ` • Last updated: ${lastUpdated}`}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <button
            onClick={() => setAutoRefresh((v) => !v)}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${
              autoRefresh
                ? 'text-emerald-signal bg-emerald-signal/8'
                : 'text-secondary bg-surface-container-low dark:bg-gray-800'
            }`}
            title={autoRefresh ? `Auto-refresh ON — refreshing in ${secondsToRefresh}s` : 'Auto-refresh paused'}
          >
            <span className={`w-2 h-2 rounded-full ${autoRefresh ? 'bg-emerald-signal animate-pulse' : 'bg-muted-slate'}`} />
            {autoRefresh ? `Live (${secondsToRefresh}s)` : 'Paused'}
          </button>
          <button
            onClick={fetchData}
            disabled={loading}
            className="flex items-center gap-2 px-3 py-1.5 border border-whisper-border rounded bg-pure-surface text-secondary hover:text-primary transition-colors shadow-sm text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            title="Refresh live data"
          >
            <span className={`material-symbols-outlined text-[20px] ${loading ? 'animate-spin' : ''}`}>sync</span>
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/8 border border-deep-rose/15 rounded-xl p-4 text-deep-rose text-sm mt-4">
          {error}
        </div>
      )}

      {/* Queue metrics banner (Service Level, Abandon Rate, Queue Depth) */}
      <section className="grid grid-cols-2 lg:grid-cols-4 gap-4 mt-6">
        <QueueMetricCard
          label="Service Level"
          sublabel="Calls answered in < 20s"
          value={serviceLevelPct === null ? '--' : `${serviceLevelPct.toFixed(1)}%`}
          state={serviceLevelPct === null ? 'idle' : serviceLevelPct >= 80 ? 'good' : serviceLevelPct >= 60 ? 'warn' : 'bad'}
          detail={`${callsUnder20s} of ${totalInboundCalls} inbound`}
        />
        <QueueMetricCard
          label="Abandon Rate"
          sublabel="Calls dropped in queue"
          value={abandonRatePct === null ? '--' : `${abandonRatePct.toFixed(1)}%`}
          state={abandonRatePct === null ? 'idle' : abandonRatePct <= 5 ? 'good' : abandonRatePct <= 10 ? 'warn' : 'bad'}
          detail={`${abandoned} abandoned today`}
        />
        <QueueMetricCard
          label="Queue Depth"
          sublabel="Calls waiting now"
          value={loading ? '--' : queueDepth}
          state={queueDepth === 0 ? 'good' : queueDepth <= 3 ? 'warn' : 'bad'}
          detail={queueDepth === 0 ? 'No calls waiting' : 'Calls in queue'}
        />
        <QueueMetricCard
          label="Inbound Volume"
          sublabel="Today's inbound calls"
          value={loading ? '--' : totalInboundCalls}
          state="idle"
          detail={`${abandoned} abandoned`}
        />
      </section>

      {/* Leaderboard + Status overview */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 mt-6">
        <section className="lg:col-span-2 bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg shadow-card overflow-hidden">
          <div className="p-6 border-b border-whisper-border dark:border-gray-700 flex justify-between items-center">
            <div>
              <h2 className="font-bold text-lg text-primary flex items-center gap-2">
                <span className="material-symbols-outlined text-emerald-signal">trophy</span>
                Top 5 — Sales Today
              </h2>
              <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
                Live leaderboard
              </p>
            </div>
          </div>
          {loading && (!leaderboard || leaderboard.rows.length === 0) ? (
            <div className="p-6 space-y-3 animate-pulse">
              {[1, 2, 3].map((i) => <div key={i} className="h-10 bg-surface-container dark:bg-gray-800 rounded" />)}
            </div>
          ) : (leaderboard?.rows?.length ?? 0) === 0 ? (
            <div className="p-12 text-sm text-muted-slate text-center">No sales yet today</div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm border-collapse">
                <thead className="text-xs uppercase tracking-wider text-secondary dark:text-gray-400 font-metadata-mono bg-surface-container-low dark:bg-gray-800">
                  <tr>
                    <th className="p-3 font-medium w-10">#</th>
                    <th className="p-3 font-medium">Agent</th>
                    <th className="p-3 font-medium text-right">VICI Sales</th>
                    <th className="p-3 font-medium text-right">Contacts</th>
                    <th className="p-3 font-medium text-right">Conv %</th>
                  </tr>
                </thead>
                <tbody>
                  {(leaderboard?.rows ?? []).map((row: Record<string, unknown>, i: number) => {
                    const sales = safeNumber(row.ViciSales);
                    const isTop = i === 0 && sales > 0;
                    return (
                      <tr key={i} className={`border-b border-whisper-border dark:border-gray-700 hover:bg-surface-container-lowest dark:hover:bg-gray-800/50 ${isTop ? 'bg-emerald-signal/5' : ''}`}>
                        <td className="p-3 font-metadata-mono text-secondary">
                          {isTop ? <span className="material-symbols-outlined text-amber-warmth text-[18px]">emoji_events</span> : `#${i + 1}`}
                        </td>
                        <td className="p-3 font-medium text-primary dark:text-gray-100">
                          {String(row.Name ?? row.User ?? '--')}
                        </td>
                        <td className="p-3 font-metadata-mono text-emerald-signal font-bold text-right">{sales}</td>
                        <td className="p-3 font-metadata-mono text-right">{safeNumber(row.Contacts)}</td>
                        <td className="p-3 font-metadata-mono text-right">
                          {row.Conversion_Percentage != null ? `${Number(row.Conversion_Percentage).toFixed(1)}%` : '--'}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg shadow-card overflow-hidden">
          <div className="p-6 border-b border-whisper-border dark:border-gray-700">
            <h2 className="font-bold text-lg text-primary flex items-center gap-2">
              <span className="material-symbols-outlined text-electric-blue">groups</span>
              Status Overview
            </h2>
            <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">
              {totalOnline} online of {enrichedAgents.length} ALTRX
            </p>
          </div>
          <div className="p-4 grid grid-cols-2 gap-2">
            {(Object.keys(STATUS_COLORS) as AgentStatus[]).map((s) => (
              <button
                key={s}
                onClick={() => setStatusFilter(statusFilter === s ? 'all' : s)}
                className={`p-3 rounded-lg border text-left transition-all ${
                  statusFilter === s
                    ? `${STATUS_COLORS[s].bg} border-current ${STATUS_COLORS[s].text}`
                    : 'border-whisper-border dark:border-gray-700 hover:bg-surface-container-low dark:hover:bg-gray-800'
                }`}
              >
                <p className="text-[10px] text-secondary uppercase tracking-wider font-semibold">{STATUS_COLORS[s].label}</p>
                <p className={`text-2xl font-bold ${statusFilter === s ? '' : 'text-primary dark:text-gray-100'}`}>
                  {loading ? '--' : statusCounts[s]}
                </p>
              </button>
            ))}
          </div>
        </section>
      </div>

      {/* Filters + Agent Grid */}
      <section className="mt-6">
        <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg shadow-card overflow-hidden">
          <div className="p-5 border-b border-whisper-border dark:border-gray-700 flex flex-col lg:flex-row lg:items-center justify-between gap-3">
            <h2 className="font-bold text-lg text-primary flex items-center gap-2">
              <span className="material-symbols-outlined text-electric-blue">contact_phone</span>
              Agent Status ({sortedAgents.length}{filteredAgents.length !== enrichedAgents.length ? ` of ${enrichedAgents.length}` : ''})
            </h2>
            <div className="flex flex-wrap items-center gap-2">
              <div className="relative">
                <span className="material-symbols-outlined text-[16px] text-secondary dark:text-gray-400 absolute left-2.5 top-1/2 -translate-y-1/2">search</span>
                <input
                  type="text"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Search name, user, or #"
                  className="pl-8 pr-3 py-1.5 text-sm border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none w-48"
                />
              </div>
              <select
                value={directionFilter}
                onChange={(e) => setDirectionFilter(e.target.value as CallDirection | 'all')}
                className="text-sm px-2.5 py-1.5 border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
                title="Filter by call direction"
              >
                <option value="all">All directions</option>
                <option value="inbound">Inbound</option>
                <option value="outbound">Outbound</option>
              </select>
              <select
                value={supervisorFilter}
                onChange={(e) => setSupervisorFilter(e.target.value)}
                className="text-sm px-2.5 py-1.5 border border-whisper-border dark:border-gray-700 rounded bg-pure-surface dark:bg-gray-800 text-primary dark:text-gray-100 focus:border-electric-blue focus:outline-none"
                title="Filter by supervisor"
              >
                <option value="all">All supervisors</option>
                {supervisors.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
              {(statusFilter !== 'all' || directionFilter !== 'all' || supervisorFilter !== 'all' || searchQuery !== '') && (
                <button
                  onClick={() => { setStatusFilter('all'); setDirectionFilter('all'); setSupervisorFilter('all'); setSearchQuery(''); }}
                  className="text-xs text-secondary hover:text-primary px-2 py-1"
                >
                  Clear
                </button>
              )}
            </div>
          </div>

          {/* Sort bar */}
          <div className="px-5 py-2 border-b border-whisper-border dark:border-gray-700 bg-surface-container-lowest dark:bg-gray-800/40 flex flex-wrap items-center gap-3 text-[11px] text-secondary dark:text-gray-400 font-metadata-mono uppercase tracking-wider">
            <span>Sort:</span>
            {([['name', 'Name'], ['status', 'Status'], ['lastCall', 'Last call'], ['idleSince', 'Idle since']] as Array<[SortKey, string]>).map(([k, label]) => (
              <button
                key={k}
                onClick={() => handleSort(k)}
                className={`hover:text-primary dark:hover:text-gray-100 transition-colors ${sortKey === k ? 'text-primary dark:text-gray-100 font-semibold' : ''}`}
              >
                {label} {sortKey === k && (sortDir === 'asc' ? '↑' : '↓')}
              </button>
            ))}
          </div>

          {loading ? (
            <div className="p-6 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
              {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
                <div key={i} className="h-40 border border-whisper-border rounded-xl bg-surface-container animate-pulse" />
              ))}
            </div>
          ) : sortedAgents.length > 0 ? (
            <div className="p-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
              {sortedAgents.map((agent) => (
                <AgentCard
                  key={agent.User}
                  agent={agent}
                  onClick={() => openAgentHistory(agent)}
                  leadLookup={leadLookup}
                  onLookupLead={lookupLead}
                />
              ))}
            </div>
          ) : (
            <div className="border-t border-whisper-border rounded-xl bg-surface-container-low flex items-center justify-center p-16 text-muted-slate text-sm m-4">
              No agents match the current filters
            </div>
          )}
        </div>
      </section>

      {/* Today's Running Totals */}
      <section className="mt-6 mb-8">
        <h2 className="font-bold text-lg text-primary mb-4">Today&apos;s Running Totals</h2>
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-5">
          <KpiCard label="Total Calls" value={totalCalls} valueColor="var(--card-value-dark)" />
          <KpiCard label="Sales Today" value={totalSales} valueColor="var(--card-value-emerald)" />
          <KpiCard label="Contacts" value={contacts} valueColor="var(--card-value-emerald)" />
          <KpiCard label="Occupancy" value={occupancy} valueColor="var(--card-value-blue)" />
          <KpiCard label="Leads Dialed" value={leadsDialed} valueColor="var(--card-value-dark)" />
          <KpiCard label="Leads Contacted" value={leadsContacted} valueColor="var(--card-value-emerald)" />
        </div>
      </section>

      <div className="flex justify-end gap-3 mb-4">
        <button
          onClick={async () => {
            setExportingExcel(true);
            try {
              const blob = await exportDashboardExcel({ period: 'Today' });
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url; a.download = `ALTRX_RealTime_Today_${new Date().toISOString().split('T')[0]}.xlsx`;
              document.body.appendChild(a); a.click(); document.body.removeChild(a); URL.revokeObjectURL(url);
            } catch { setError('Failed to export Excel'); }
            finally { setExportingExcel(false); }
          }}
          disabled={exportingExcel}
          className="bg-emerald-signal text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <span className="material-symbols-outlined text-sm">table_chart</span>
          {exportingExcel ? 'Generating...' : 'Export Excel'}
        </button>
        <button
          onClick={async () => {
            setExportingPdf(true);
            try {
              const blob = await exportDashboardPdf({ period: 'Today' });
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url; a.download = `ALTRX_RealTime_Today_${new Date().toISOString().split('T')[0]}.pdf`;
              document.body.appendChild(a); a.click(); document.body.removeChild(a); URL.revokeObjectURL(url);
            } catch { setError('Failed to export PDF'); }
            finally { setExportingPdf(false); }
          }}
          disabled={exportingPdf}
          className="bg-deep-rose text-white px-4 py-2 rounded font-medium text-sm hover:scale-[0.98] transition-transform shadow-sm flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <span className="material-symbols-outlined text-sm">picture_as_pdf</span>
          {exportingPdf ? 'Generating...' : 'Export PDF'}
        </button>
      </div>

      {agentHistory.open && (
        <AgentHistoryModal state={agentHistory} onClose={closeAgentHistory} />
      )}
    </>
  );
}

function AgentCard({
  agent, onClick, leadLookup, onLookupLead,
}: {
  agent: AgentRow & { Direction: CallDirection; Status: AgentStatus };
  onClick: () => void;
  leadLookup: Record<number, LeadLookupState>;
  onLookupLead: (id: number) => void;
}) {
  const c = STATUS_COLORS[agent.Status];
  const callTime = (agent.Status === 'INCALL' || agent.Status === 'QUEUE') ? elapsed(agent.last_call_time) : '';
  const pauseTime = agent.Status === 'PAUSED' ? elapsed(agent.last_update_time) : '';
  const idleTime = (agent.Status === 'READY' || agent.Status === 'OFFLINE') ? elapsed(agent.last_update_time) : '';

  const isLongPause = agent.Status === 'PAUSED' && agent.last_update_time
    ? (Date.now() - new Date(agent.last_update_time).getTime()) / 1000 > LONG_PAUSE_THRESHOLD_SEC
    : false;

  const leadId = agent.Current_Lead_Id ?? null;
  const leadState = leadId ? leadLookup[leadId] : null;
  const pauseLabel = getPauseReasonLabel(agent.Pause_Code);
  const dialLabel = getDialMethodLabel(agent.Dial_Method);
  const direction = agent.Direction;

  return (
    <div
      onClick={onClick}
      className={`relative border ${isLongPause ? 'border-deep-rose' : 'border-whisper-border'} dark:border-gray-700 rounded-xl p-4 ${c.bg} cursor-pointer hover:shadow-card transition-shadow`}
    >
      {isLongPause && (
        <span title={`Paused for ${pauseTime}`} className="absolute top-2 right-2 material-symbols-outlined text-deep-rose text-[16px] animate-pulse">priority_high</span>
      )}
      <div className="flex items-start justify-between mb-2">
        <div className="flex items-center gap-2.5">
          <div className="w-9 h-9 rounded-full bg-pure-surface border border-whisper-border flex items-center justify-center text-primary font-bold text-xs">
            {getInitials(agent.Name ?? agent.User ?? '')}
          </div>
          <div className="min-w-0">
            <p className="text-sm font-medium text-primary leading-tight truncate" title={agent.Name}>{agent.Name ?? agent.User ?? '--'}</p>
            <p className="text-[11px] text-secondary font-metadata-mono">#{agent.Emp_Number ?? agent.User}</p>
          </div>
        </div>
        <div className="flex items-center gap-1.5 shrink-0">
          <span className={`w-2 h-2 rounded-full ${c.dot}`} />
          <span className={`text-[11px] font-medium ${c.text}`}>{c.label}</span>
        </div>
      </div>

      {/* Current lead */}
      {leadId && (
        <div className="text-[11px] text-secondary dark:text-gray-400 font-metadata-mono flex items-center gap-1 mb-1.5" onClick={(e) => { e.stopPropagation(); onLookupLead(leadId); }}>
          <span className="material-symbols-outlined text-[12px]">person</span>
          <span>Lead:</span>
          {leadState?.loading ? (
            <span className="material-symbols-outlined text-[12px] animate-spin text-electric-blue">progress_activity</span>
          ) : leadState?.data ? (
            <span className="font-medium text-primary dark:text-gray-100 truncate" title={`${leadState.data.firstName} ${leadState.data.lastName}`}>
              {leadState.data.firstName} {leadState.data.lastName} <span className="text-secondary">#{leadId}</span>
            </span>
          ) : leadState?.error ? (
            <span className="text-deep-rose" title={leadState.error}>#{leadId} (not found)</span>
          ) : (
            <button className="text-electric-blue hover:underline">#{leadId} (load)</button>
          )}
        </div>
      )}

      {/* Campaign + dial method + direction */}
      {agent.Current_Campaign_Name && (
        <div className="text-[11px] text-secondary dark:text-gray-400 font-metadata-mono flex flex-wrap items-center gap-1 mb-1.5">
          <span className="material-symbols-outlined text-[12px]">campaign</span>
          <span className="font-medium text-primary dark:text-gray-100 truncate" title={agent.Current_Campaign_Name}>{agent.Current_Campaign_Name}</span>
          {dialLabel && (
            <span className="px-1.5 py-0.5 rounded bg-surface-container dark:bg-gray-800 text-[10px] uppercase tracking-wider text-secondary font-semibold">
              {dialLabel}
            </span>
          )}
          {direction === 'inbound' && (
            <span className="px-1.5 py-0.5 rounded bg-electric-blue/10 text-electric-blue text-[10px] uppercase tracking-wider font-semibold">Inbound</span>
          )}
          {direction === 'outbound' && (
            <span className="px-1.5 py-0.5 rounded bg-amber-warmth/10 text-amber-warmth text-[10px] uppercase tracking-wider font-semibold">Outbound</span>
          )}
        </div>
      )}

      {/* Pause reason */}
      {agent.Status === 'PAUSED' && pauseLabel && (
        <div className={`text-[11px] font-metadata-mono flex items-center gap-1 mb-1.5 ${isLongPause ? 'text-deep-rose font-semibold' : 'text-secondary dark:text-gray-400'}`}>
          <span className="material-symbols-outlined text-[12px]">pause_circle</span>
          <span>{pauseLabel}{pauseTime && ` — ${pauseTime}`}</span>
          {isLongPause && <span title="Paused for more than 15 minutes" className="ml-1 px-1.5 py-0.5 rounded bg-deep-rose/10 text-deep-rose text-[10px] font-bold uppercase tracking-wider">Long</span>}
        </div>
      )}

      {/* Time display */}
      {callTime && (
        <div className="flex items-center gap-2 text-[11px] text-secondary font-metadata-mono">
          <span className="material-symbols-outlined text-[14px]">call</span>
          <span>Call: <span className="font-medium text-primary">{callTime}</span></span>
        </div>
      )}
      {idleTime && (
        <div className="flex items-center gap-2 text-[11px] text-secondary font-metadata-mono">
          <span className="material-symbols-outlined text-[14px]">schedule</span>
          <span>{agent.Status === 'READY' ? 'Waiting' : 'Idle'}: <span className="font-medium text-primary">{idleTime}</span></span>
        </div>
      )}
      {agent.Status === 'OFFLINE' && !idleTime && (
        <div className="flex items-center gap-2 text-[11px] text-secondary font-metadata-mono">
          <span className="material-symbols-outlined text-[14px]">block</span>
          <span>Not logged in</span>
        </div>
      )}
    </div>
  );
}

function QueueMetricCard({ label, sublabel, value, state, detail }: {
  label: string; sublabel: string; value: string | number; state: 'good' | 'warn' | 'bad' | 'idle'; detail?: string;
}) {
  const stateColor = state === 'good' ? 'var(--card-value-emerald)' : state === 'warn' ? '#F59E0B' : state === 'bad' ? '#E11D48' : 'var(--card-value-dark)';
  return (
    <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-5 shadow-card">
      <p className="text-card-label text-[12px] font-medium uppercase tracking-wider">{label}</p>
      <p className="text-[1.75rem] font-bold mt-1 leading-none" style={{ color: stateColor }}>{value}</p>
      <p className="text-[11px] text-secondary dark:text-gray-400 mt-1">{sublabel}</p>
      {detail && <p className="text-[10px] text-secondary dark:text-gray-500 mt-0.5 font-metadata-mono">{detail}</p>}
    </div>
  );
}

function KpiCard({ label, value, valueColor }: { label: string; value: string; valueColor: string }) {
  return (
    <div className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-lg p-7 shadow-card">
      <p className="text-card-label text-[13px] font-medium">{label}</p>
      <p className="text-[2rem] font-bold mt-1 leading-none" style={{ color: valueColor }}>{value}</p>
    </div>
  );
}

function AgentHistoryModal({ state, onClose }: { state: AgentHistoryState; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div
        className="bg-pure-surface dark:bg-gray-900 border border-card-border dark:border-gray-700 rounded-xl shadow-2xl w-full max-w-3xl max-h-[80vh] overflow-hidden flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="p-5 border-b border-whisper-border dark:border-gray-700 flex justify-between items-center">
          <div>
            <h3 className="font-bold text-lg text-primary flex items-center gap-2">
              <span className="material-symbols-outlined text-electric-blue">history</span>
              Today's Calls — {state.agentName}
            </h3>
            <p className="text-[11px] text-secondary mt-0.5 font-metadata-mono uppercase tracking-wider">Call history for the day</p>
          </div>
          <button onClick={onClose} className="text-secondary hover:text-primary">
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-5">
          {state.loading ? (
            <div className="space-y-3 animate-pulse">
              {[1, 2, 3, 4, 5].map((i) => <div key={i} className="h-12 bg-surface-container dark:bg-gray-800 rounded" />)}
            </div>
          ) : state.error ? (
            <div className="text-deep-rose text-sm">{state.error}</div>
          ) : (state.data?.rows?.length ?? 0) === 0 ? (
            <div className="text-muted-slate text-sm text-center py-12">No calls today</div>
          ) : (
            <table className="w-full text-left text-sm border-collapse">
              <thead className="text-[11px] uppercase tracking-wider text-secondary font-metadata-mono bg-surface-container-low dark:bg-gray-800 sticky top-0">
                <tr>
                  <th className="p-2 font-medium">Time</th>
                  <th className="p-2 font-medium">Length</th>
                  <th className="p-2 font-medium">Status</th>
                  <th className="p-2 font-medium">Lead</th>
                  <th className="p-2 font-medium">Phone</th>
                </tr>
              </thead>
              <tbody>
                {(state.data?.rows ?? []).map((row: Record<string, unknown>, i: number) => {
                  const callDate = row.call_date ? new Date(String(row.call_date)) : null;
                  const length = safeNumber(row.Length_Sec, 0);
                  return (
                    <tr key={i} className="border-b border-whisper-border dark:border-gray-700">
                      <td className="p-2 font-metadata-mono text-[11px]">
                        {callDate ? callDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '--'}
                      </td>
                      <td className="p-2 font-metadata-mono text-[11px]">
                        {Math.floor(length / 60)}m {length % 60}s
                      </td>
                      <td className="p-2 text-[11px]">{String(row.status ?? '--')}</td>
                      <td className="p-2 font-metadata-mono text-[11px]">{row.lead_id != null ? `#${row.lead_id}` : '--'}</td>
                      <td className="p-2 font-metadata-mono text-[11px]">{String(row.Phone ?? '--')}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
}
