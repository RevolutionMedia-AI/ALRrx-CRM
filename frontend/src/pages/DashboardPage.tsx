import { useEffect, useState, useCallback } from 'react';
import { useAuth } from '../context/AuthContext';
import KpiCards from '../components/Dashboard/KpiCards';
import ChartSection from '../components/Charts/ChartSection';
import SectionContainer from '../components/Layout/SectionContainer';
import AgentTable from '../components/Tables/AgentTable';
import StaffingTable from '../components/Tables/StaffingTable';
import DispositionsTable from '../components/Tables/DispositionsTable';
import AllCallsTable from '../components/Dashboard/AllCallsTable';
import { getDashboardSummary, getReport, getStaffing } from '../services/api';
import { DashboardHubConnection } from '../services/signalR';
import { exportCombinedCSV } from '../utils/csv';
import type {
  DashboardSummaryDto,
  ReportDto,
  TimeFilterDto,
} from '../types';

declare module 'react' {
  interface CSSProperties {
    [key: `--${string}`]: string | number;
  }
}

const DEFAULT_FILTER: TimeFilterDto = { period: 'Today' };

export default function DashboardPage() {
  const { canEdit } = useAuth();

  // independent filter states
  const [summaryFilter, setSummaryFilter] = useState<TimeFilterDto>(DEFAULT_FILTER);
  const [agentFilter, setAgentFilter] = useState<TimeFilterDto>(DEFAULT_FILTER);
  const [dispoFilter, setDispoFilter] = useState<TimeFilterDto>(DEFAULT_FILTER);
  const [callsFilter, setCallsFilter] = useState<TimeFilterDto>(DEFAULT_FILTER);

  // data states
  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [staffingReport, setStaffingReport] = useState<ReportDto | null>(null);
  const [dispositionsReport, setDispositionsReport] = useState<ReportDto | null>(null);
  const [allCallsReport, setAllCallsReport] = useState<ReportDto | null>(null);

  // loading states
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [agentLoading, setAgentLoading] = useState(true);
  const [staffingLoading, setStaffingLoading] = useState(true);
  const [dispoLoading, setDispoLoading] = useState(true);
  const [callsLoading, setCallsLoading] = useState(true);

  // error states
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [agentError, setAgentError] = useState<string | null>(null);
  const [staffingError, setStaffingError] = useState<string | null>(null);
  const [dispoError, setDispoError] = useState<string | null>(null);
  const [callsError, setCallsError] = useState<string | null>(null);

  // load functions per section
  const loadSummary = useCallback(async (f: TimeFilterDto) => {
    setSummaryLoading(true);
    setSummaryError(null);
    try {
      const data = await getDashboardSummary(f);
      setSummary(data);
    } catch {
      setSummaryError('Failed to load summary');
    } finally {
      setSummaryLoading(false);
    }
  }, []);

  const loadAgent = useCallback(async (f: TimeFilterDto) => {
    setAgentLoading(true);
    setAgentError(null);
    try {
      const data = await getReport('agent_performance', f);
      setAgentReport(data);
    } catch {
      setAgentError('Failed to load agent data');
    } finally {
      setAgentLoading(false);
    }
  }, []);

  const loadDispositions = useCallback(async (f: TimeFilterDto) => {
    setDispoLoading(true);
    setDispoError(null);
    try {
      const data = await getReport('dispositions', f);
      setDispositionsReport(data);
    } catch {
      setDispoError('Failed to load dispositions');
    } finally {
      setDispoLoading(false);
    }
  }, []);

  const loadCalls = useCallback(async (f: TimeFilterDto) => {
    setCallsLoading(true);
    setCallsError(null);
    try {
      const data = await getReport('all_calls', f);
      setAllCallsReport(data);
    } catch {
      setCallsError('Failed to load calls');
    } finally {
      setCallsLoading(false);
    }
  }, []);

  const loadStaffing = useCallback(async () => {
    setStaffingLoading(true);
    setStaffingError(null);
    try {
      const data = await getStaffing();
      setStaffingReport(data);
    } catch {
      setStaffingError('Failed to load staffing');
    } finally {
      setStaffingLoading(false);
    }
  }, []);

  // initial loads
  useEffect(() => {
    loadSummary(summaryFilter);
    loadAgent(agentFilter);
    loadDispositions(dispoFilter);
    loadCalls(callsFilter);
    loadStaffing();
  }, []);

  // SignalR hub for real-time summary updates
  useEffect(() => {
    const hub = new DashboardHubConnection(
      (data) => setSummary(data),
      (msg) => setSummaryError(msg)
    );
    hub.start().catch(() => {});
    return () => { hub.stop().catch(() => {}); };
  }, []);

  const handleRefreshAll = () => {
    loadSummary(summaryFilter);
    loadAgent(agentFilter);
    loadDispositions(dispoFilter);
    loadCalls(callsFilter);
    loadStaffing();
  };

  const handleGlobalCSV = () => {
    const sections: { name: string; columns: string[]; rows: Record<string, unknown>[] }[] = [];
    if (summary) {
      sections.push({
        name: 'KPI Metrics',
        columns: ['Label', 'Value', 'Trend'],
        rows: summary.metrics.map(m => ({ Label: m.label, Value: m.value, Trend: m.trend ?? '' })),
      });
    }
    if (agentReport) sections.push({ name: 'Agent Performance', columns: agentReport.columns, rows: agentReport.rows });
    if (staffingReport) sections.push({ name: 'Staffing', columns: staffingReport.columns, rows: staffingReport.rows });
    if (dispositionsReport) sections.push({ name: 'Dispositions', columns: dispositionsReport.columns, rows: dispositionsReport.rows });
    if (allCallsReport) sections.push({ name: 'All Calls', columns: allCallsReport.columns, rows: allCallsReport.rows });
    exportCombinedCSV(sections);
  };

  return (
    <div className="dashboard">
      <div className="dashboard-toolbar">
        <button className="icon-btn" onClick={handleRefreshAll} title="Refresh all">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>
        </button>
        <button className="csv-btn csv-btn--global" onClick={handleGlobalCSV} title="Export all to CSV">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>
          </svg>
          Export All CSV
        </button>
      </div>

      {/* KPI + Charts section */}
      {summary && (
        <SectionContainer
          title="Overview"
          onFilterChange={(f) => { setSummaryFilter(f); loadSummary(f); }}
          loading={summaryLoading}
          error={summaryError}
        >
          <KpiCards metrics={summary.metrics} />
          <ChartSection charts={summary.charts} />
        </SectionContainer>
      )}

      {/* Agent Performance */}
      <SectionContainer
        title="Agent Performance"
        report={agentReport}
        onFilterChange={(f) => { setAgentFilter(f); loadAgent(f); }}
        loading={agentLoading}
        error={agentError}
      >
        {agentReport && (
          <AgentTable report={agentReport} canEdit={canEdit} tableName="vicidial_log" />
        )}
      </SectionContainer>

      {/* Staffing — no time filter needed */}
      <SectionContainer
        title="Staffing — Active Agents"
        report={staffingReport}
        onFilterChange={() => {}}
        loading={staffingLoading}
        error={staffingError}
      >
        {staffingReport && (
          <StaffingTable report={staffingReport} canEdit={canEdit} />
        )}
      </SectionContainer>

      {/* Dispositions */}
      <SectionContainer
        title="Dispositions Breakdown"
        report={dispositionsReport}
        onFilterChange={(f) => { setDispoFilter(f); loadDispositions(f); }}
        loading={dispoLoading}
        error={dispoError}
      >
        {dispositionsReport && (
          <DispositionsTable report={dispositionsReport} />
        )}
      </SectionContainer>

      {/* All Calls */}
      <SectionContainer
        title="All Calls"
        report={allCallsReport}
        onFilterChange={(f) => { setCallsFilter(f); loadCalls(f); }}
        loading={callsLoading}
        error={callsError}
      >
        {allCallsReport && (
          <AllCallsTable report={allCallsReport} canEdit={canEdit} tableName="vicidial_log" />
        )}
      </SectionContainer>
    </div>
  );
}
