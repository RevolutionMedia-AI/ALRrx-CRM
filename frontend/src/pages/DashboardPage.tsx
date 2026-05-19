import { useEffect, useState, useCallback } from 'react';
import { useAuth } from '../context/AuthContext';
import KpiCards from '../components/Dashboard/KpiCards';
import ChartSection from '../components/Charts/ChartSection';
import TimeFilter from '../components/Filters/TimeFilter';
import AgentTable from '../components/Tables/AgentTable';
import StaffingTable from '../components/Tables/StaffingTable';
import DispositionsTable from '../components/Tables/DispositionsTable';
import AllCallsTable from '../components/Dashboard/AllCallsTable';
import ExportPanel from '../components/Export/ExportPanel';
import { getDashboardSummary, getReport, getStaffing, getAvailableQueries } from '../services/api';
import { DashboardHubConnection } from '../services/signalR';
import type {
  DashboardSummaryDto,
  QueryDefinitionDto,
  ReportDto,
  TimeFilterDto,
} from '../types';

declare module 'react' {
  interface CSSProperties {
    [key: `--${string}`]: string | number;
  }
}

const DEFAULT_FILTER: TimeFilterDto = { period: 'Today' };
type ActiveSection = 'all_calls' | null;

function SkeletonDashboard() {
  return (
    <>
      <div className="skeleton-grid">
        {[1, 2, 3, 4].map(i => <div key={i} className="skeleton-card" />)}
      </div>
      <div className="skeleton-grid" style={{ gridTemplateColumns: '1fr 1fr' }}>
        <div className="skeleton-chart" />
        <div className="skeleton-chart" />
      </div>
    </>
  );
}

export default function DashboardPage() {
  const { canEdit } = useAuth();

  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [queries, setQueries] = useState<QueryDefinitionDto[]>([]);
  const [filter, setFilter] = useState<TimeFilterDto>(DEFAULT_FILTER);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [agentReport, setAgentReport] = useState<ReportDto | null>(null);
  const [staffingReport, setStaffingReport] = useState<ReportDto | null>(null);
  const [dispositionsReport, setDispositionsReport] = useState<ReportDto | null>(null);
  const [allCallsReport, setAllCallsReport] = useState<ReportDto | null>(null);

  const [activeSection, setActiveSection] = useState<ActiveSection>(null);

  const loadDashboard = useCallback(async (f: TimeFilterDto) => {
    setLoading(true);
    setError(null);
    try {
      const [summaryData, agentData, dispositionsData, callsData, staffingData] = await Promise.all([
        getDashboardSummary(f),
        getReport('agent_performance', f),
        getReport('dispositions', f),
        getReport('all_calls', f),
        getStaffing(),
      ]);
      setSummary(summaryData);
      setAgentReport(agentData);
      setDispositionsReport(dispositionsData);
      setAllCallsReport(callsData);
      setStaffingReport(staffingData);
    } catch (err) {
      setError('Failed to load dashboard data. Please check your connection and try again.');
      console.error(err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadDashboard(filter);
    getAvailableQueries().then(setQueries).catch(console.error);
  }, []);

  useEffect(() => {
    const hub = new DashboardHubConnection(
      (data) => setSummary(data),
      (msg) => setError(msg)
    );
    hub.start().catch(console.error);
    return () => { hub.stop().catch(() => {}); };
  }, []);

  const handleFilterChange = (newFilter: TimeFilterDto) => {
    setFilter(newFilter);
    loadDashboard(newFilter);
  };

  const handleRefresh = () => loadDashboard(filter);

  return (
    <div className="dashboard">
      <div className="dashboard-toolbar">
        <button className="icon-btn" onClick={handleRefresh} title="Refresh">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>
        </button>
        <ExportPanel queries={queries} currentFilter={filter} />
      </div>

      <TimeFilter onFilterChange={handleFilterChange} />

      {error && <div className="error-banner">{error}</div>}

      {loading && !summary && <SkeletonDashboard />}

      {summary && (
        <>
          <KpiCards metrics={summary.metrics} />
          <ChartSection charts={summary.charts} />

          {agentReport && <AgentTable report={agentReport} canEdit={canEdit} tableName="vicidial_log" />}
          {staffingReport && <StaffingTable report={staffingReport} canEdit={canEdit} />}
          {dispositionsReport && <DispositionsTable report={dispositionsReport} />}

          <section className="reports-section">
            <h2>Quick Actions</h2>
            <div className="report-list">
              <button
                className={`report-btn ${activeSection === 'all_calls' ? 'active' : ''}`}
                onClick={() => setActiveSection(s => s === 'all_calls' ? null : 'all_calls')}
              >
                <strong>All Calls</strong>
                <span>View every call recorded in the selected period</span>
              </button>
            </div>
          </section>

          {activeSection === 'all_calls' && allCallsReport && (
            <AllCallsTable report={allCallsReport} canEdit={canEdit} tableName="vicidial_log" />
          )}
        </>
      )}
    </div>
  );
}
