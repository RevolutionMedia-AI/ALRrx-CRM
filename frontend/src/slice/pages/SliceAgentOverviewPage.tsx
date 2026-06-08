import { useEffect, useState, useCallback, useMemo } from 'react';
import {
  getSliceReports,
  getSliceReport,
  patchSliceAgentRow,
  sliceExportUrl,
} from '../../services/sliceReportsApi';
import { useSliceAuth } from '../context/SliceAuthContext';
import type {
  SliceReport,
  SliceReportSummary,
  SliceDailyAgentRow,
  SliceDailyAgentRowPatch,
} from '../types';
import { secondsToMmSs, initialsFromEmail, nameFromEmail, formatInt } from '../utils/formatSlice';

type SortKey =
  | 'agentEmail'
  | 'hc'
  | 'pctTransfers'
  | 'numberOfHolds'
  | 'avgHoldTime'
  | 'asa'
  | 'aht'
  | 'acw';
type SortDir = 'asc' | 'desc';

const PAGE_SIZE = 8;

const COLUMNS: { key: SortKey; label: string; align: 'left' | 'right' }[] = [
  { key: 'agentEmail', label: 'Agent Name', align: 'left' },
  { key: 'hc', label: 'Handled', align: 'right' },
  { key: 'pctTransfers', label: 'Transfers', align: 'right' },
  { key: 'numberOfHolds', label: 'Holds', align: 'right' },
  { key: 'avgHoldTime', label: 'Avg Hold', align: 'right' },
  { key: 'asa', label: 'ASA', align: 'right' },
  { key: 'aht', label: 'AHT', align: 'right' },
  { key: 'acw', label: 'ACW', align: 'right' },
];

interface NumberCellProps {
  value: number;
  threshold?: { above?: number; below?: number };
  isTime?: boolean;
  disabled: boolean;
  onCommit: (v: number) => Promise<void>;
}

function NumberCell({ value, threshold, isTime, disabled, onCommit }: NumberCellProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(String(value));
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!editing) setDraft(String(value));
  }, [value, editing]);

  const display = isTime ? secondsToMmSs(value) : formatInt(value);
  let colorClass = 'text-on-surface';
  if (threshold?.above !== undefined && value > threshold.above) colorClass = 'text-deep-rose';
  else if (threshold?.below !== undefined && value < threshold.below) colorClass = 'text-emerald-signal';

  if (disabled || !editing) {
    return (
      <td
        data-label={isTime ? 'Time' : 'Count'}
        className={`py-3 px-2 text-right font-metadata-mono ${colorClass} ${
          disabled ? '' : 'cursor-pointer hover:bg-electric-blue/5'
        } transition-colors`}
        onDoubleClick={() => !disabled && setEditing(true)}
        title={disabled ? undefined : 'Doble clic para editar'}
      >
        {busy ? <span className="text-xs text-secondary">…</span> : display}
        {err && <span className="block text-[10px] text-deep-rose mt-0.5">{err}</span>}
      </td>
    );
  }

  return (
    <td data-label="Editing" className="py-3 px-2 text-right">
      <input
        autoFocus
        type="number"
        step="any"
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={async () => {
          const parsed = parseFloat(draft);
          if (Number.isNaN(parsed)) {
            setEditing(false);
            return;
          }
          setBusy(true);
          setErr(null);
          try {
            await onCommit(parsed);
            setEditing(false);
          } catch (e) {
            setErr(e instanceof Error ? e.message : 'Error');
          } finally {
            setBusy(false);
          }
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
          else if (e.key === 'Escape') {
            setDraft(String(value));
            setEditing(false);
          }
        }}
        className="w-24 text-right px-2 py-1 text-sm border border-electric-blue rounded outline-none focus:ring-2 focus:ring-electric-blue/30"
      />
    </td>
  );
}

export default function SliceAgentOverviewPage() {
  const { isAdmin } = useSliceAuth();

  const [reports, setReports] = useState<SliceReportSummary[]>([]);
  const [selectedReportId, setSelectedReportId] = useState<string | null>(null);
  const [report, setReport] = useState<SliceReport | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [sort, setSort] = useState<{ key: SortKey; dir: SortDir }>({ key: 'hc', dir: 'desc' });

  const [loadingReports, setLoadingReports] = useState(false);
  const [loadingReport, setLoadingReport] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadReports = useCallback(async () => {
    setLoadingReports(true);
    setError(null);
    try {
      const data = await getSliceReports();
      setReports(data);
      if (data.length > 0 && !selectedReportId) setSelectedReportId(data[0].id);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load reports');
    } finally {
      setLoadingReports(false);
    }
  }, [selectedReportId]);

  useEffect(() => {
    loadReports();
  }, [loadReports]);

  const loadReport = useCallback(async (id: string) => {
    setLoadingReport(true);
    setError(null);
    try {
      const data = await getSliceReport(id);
      setReport(data);
      setPage(0);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load report');
    } finally {
      setLoadingReport(false);
    }
  }, []);

  useEffect(() => {
    if (selectedReportId) loadReport(selectedReportId);
  }, [selectedReportId, loadReport]);

  const handlePatch = async (
    agentEmail: string,
    patch: SliceDailyAgentRowPatch
  ) => {
    if (!selectedReportId) return;
    await patchSliceAgentRow(selectedReportId, agentEmail, patch);
    await loadReport(selectedReportId);
  };

  const filteredRows = useMemo(() => {
    if (!report) return [];
    const q = search.trim().toLowerCase();
    let rows = report.dailyAgents;
    if (q) {
      rows = rows.filter(
        (r) =>
          r.agentEmail.toLowerCase().includes(q) ||
          r.supervisorName.toLowerCase().includes(q) ||
          r.pod.toLowerCase().includes(q)
      );
    }
    const dir = sort.dir === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = a[sort.key];
      const bv = b[sort.key];
      if (typeof av === 'string' && typeof bv === 'string') return av.localeCompare(bv) * dir;
      return ((av as number) - (bv as number)) * dir;
    });
  }, [report, search, sort]);

  const totalPages = Math.max(1, Math.ceil(filteredRows.length / PAGE_SIZE));
  const pagedRows = filteredRows.slice(page * PAGE_SIZE, page * PAGE_SIZE + PAGE_SIZE);

  const handleSort = (key: SortKey) => {
    setSort((s) => (s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'desc' }));
  };

  const handleExport = (format: 'xlsx' | 'csv') => {
    if (!selectedReportId || !report) return;
    const token = localStorage.getItem('slice_token');
    if (!token) return;
    fetch(sliceExportUrl(selectedReportId, format), {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then((r) => r.blob())
      .then((blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `Slice_Report_${new Date(report.reportDate).toISOString().slice(0, 10)}.${format}`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Export failed'));
  };

  return (
    <>
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-end gap-4">
        <div>
          <h2 className="font-headline-lg text-headline-lg text-primary mb-2">Rendimiento de Agentes</h2>
          <p className="text-secondary max-w-2xl text-sm">
            Enfoque operativo detallado de las métricas clave de resolución y manejo de llamadas por agente.
          </p>
        </div>
        <div className="flex gap-3 w-full sm:w-auto flex-wrap">
          <div className="relative flex-1 sm:flex-none">
            <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-secondary text-lg">search</span>
            <input
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(0);
              }}
              className="pl-9 pr-4 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none w-full sm:w-64 transition-all shadow-sm"
              placeholder="Search agent, pod, supervisor..."
              type="text"
            />
          </div>
          <select
            value={selectedReportId ?? ''}
            onChange={(e) => setSelectedReportId(e.target.value || null)}
            disabled={loadingReports || reports.length === 0}
            className="px-3 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none transition-all shadow-sm"
          >
            {reports.length === 0 && <option value="">No reports yet</option>}
            {reports.map((r) => (
              <option key={r.id} value={r.id}>
                {new Date(r.reportDate).toLocaleDateString()} — {r.agentCount} agents
              </option>
            ))}
          </select>
          <button
            onClick={() => handleExport('xlsx')}
            disabled={!selectedReportId}
            className="flex items-center justify-center gap-2 px-4 py-2 bg-pure-surface border border-whisper-border text-primary rounded-lg hover:bg-surface-container-low transition-colors text-sm whitespace-nowrap shadow-sm disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-[18px]">table_view</span>
            Export Excel
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      {!loadingReports && reports.length === 0 && !error && (
        <div className="bg-pure-surface border border-whisper-border rounded-xl p-12 flex flex-col items-center text-center">
          <span className="material-symbols-outlined text-4xl text-muted-slate/40 mb-2">groups</span>
          <p className="text-sm font-medium text-primary">No reports yet</p>
          <p className="text-xs text-muted-slate mt-1">Upload an Excel or ZIP file to generate agent data.</p>
        </div>
      )}

      {report && (
        <div className="bg-pure-surface border border-whisper-border rounded-xl shadow-ambient p-4 md:p-6">
          <div className="flex items-center justify-between mb-4 pb-3 border-b border-whisper-border text-xs text-secondary">
            <span>
              <span className="font-metadata-mono text-primary">{filteredRows.length}</span> of{' '}
              <span className="font-metadata-mono text-primary">{report.dailyAgents.length}</span> agents
              {report.dailyAgents.length > 0 && (
                <>
                  {' '}• <span className="text-primary">
                    {new Set(report.dailyAgents.map((a) => a.pod)).size}
                  </span> pods
                </>
              )}
            </span>
            {!isAdmin && (
              <span className="flex items-center gap-1 text-muted-slate">
                <span className="material-symbols-outlined text-sm">visibility</span>
                Read-only (Viewer)
              </span>
            )}
          </div>

          <table className="w-full text-left border-collapse mobile-stacked-table">
            <thead>
              <tr className="border-b border-whisper-border text-secondary font-metadata-mono text-[11px] uppercase tracking-wider">
                {COLUMNS.map((col) => (
                  <th
                    key={col.key}
                    onClick={() => handleSort(col.key)}
                    className={`pb-3 px-2 font-normal cursor-pointer hover:text-primary transition-colors ${
                      col.align === 'right' ? 'text-right' : 'text-left'
                    }`}
                  >
                    <span className="inline-flex items-center gap-1">
                      {col.label}
                      {sort.key === col.key && (
                        <span className="material-symbols-outlined text-[14px]">
                          {sort.dir === 'asc' ? 'arrow_upward' : 'arrow_downward'}
                        </span>
                      )}
                    </span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="text-primary text-sm divide-y divide-whisper-border">
              {loadingReport ? (
                <tr>
                  <td colSpan={COLUMNS.length} className="py-10 text-center text-secondary">
                    Loading agents...
                  </td>
                </tr>
              ) : pagedRows.length === 0 ? (
                <tr>
                  <td colSpan={COLUMNS.length} className="py-10 text-center text-secondary">
                    No agents match "{search}".
                  </td>
                </tr>
              ) : (
                pagedRows.map((agent, idx) => (
                  <tr
                    key={agent.agentEmail}
                    className={`hover:bg-surface-container-lowest transition-colors group ${
                      idx % 2 === 1 ? 'bg-surface-container-low' : ''
                    }`}
                  >
                    <td className="py-3 px-2 font-bold" data-label="Agent Name">
                      <div className="flex items-center gap-3 min-w-0">
                        <div className="w-8 h-8 rounded-full bg-secondary-container text-on-secondary-container flex items-center justify-center text-[0.75rem] font-bold shrink-0">
                          {initialsFromEmail(agent.agentEmail)}
                        </div>
                        <div className="min-w-0">
                          <p className="truncate text-primary">{nameFromEmail(agent.agentEmail)}</p>
                          {agent.pod && (
                            <p className="text-[10px] text-muted-slate font-metadata-mono uppercase tracking-wider truncate">
                              {agent.pod}
                              {agent.supervisorName && ` · ${agent.supervisorName}`}
                            </p>
                          )}
                        </div>
                      </div>
                    </td>
                    <NumberCell
                      value={agent.hc}
                      disabled={!isAdmin}
                      onCommit={(v) => handlePatch(agent.agentEmail, { hc: v })}
                    />
                    <NumberCell
                      value={agent.pctTransfers}
                      disabled={!isAdmin}
                      threshold={{ above: 15 }}
                      onCommit={(v) => handlePatch(agent.agentEmail, { pctTransfers: v })}
                    />
                    <NumberCell
                      value={agent.numberOfHolds}
                      disabled={!isAdmin}
                      onCommit={(v) => handlePatch(agent.agentEmail, { numberOfHolds: v })}
                    />
                    <NumberCell
                      value={agent.avgHoldTime}
                      isTime
                      disabled={!isAdmin}
                      threshold={{ above: 60 }}
                      onCommit={(v) => handlePatch(agent.agentEmail, { avgHoldTime: v })}
                    />
                    <NumberCell
                      value={agent.asa}
                      isTime
                      disabled={!isAdmin}
                      threshold={{ below: 15 }}
                      onCommit={(v) => handlePatch(agent.agentEmail, { asa: v })}
                    />
                    <NumberCell
                      value={agent.aht}
                      isTime
                      disabled={!isAdmin}
                      threshold={{ above: 300 }}
                      onCommit={(v) => handlePatch(agent.agentEmail, { aht: v })}
                    />
                    <NumberCell
                      value={agent.acw}
                      isTime
                      disabled={!isAdmin}
                      onCommit={(v) => handlePatch(agent.agentEmail, { acw: v })}
                    />
                  </tr>
                ))
              )}
            </tbody>
          </table>

          <div className="mt-6 flex justify-between items-center border-t border-whisper-border pt-4">
            <span className="font-metadata-mono text-xs text-secondary">
              {filteredRows.length === 0
                ? 'Mostrando 0 agentes'
                : `Mostrando ${page * PAGE_SIZE + 1}-${Math.min(
                    (page + 1) * PAGE_SIZE,
                    filteredRows.length
                  )} de ${filteredRows.length} agentes`}
            </span>
            <div className="flex gap-2">
              <button
                onClick={() => setPage((p) => Math.max(0, p - 1))}
                disabled={page === 0}
                className="w-8 h-8 flex items-center justify-center border border-whisper-border rounded text-secondary hover:text-primary hover:border-primary transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <span className="material-symbols-outlined text-[16px]">chevron_left</span>
              </button>
              <span className="text-xs text-secondary font-metadata-mono px-2 self-center">
                {page + 1} / {totalPages}
              </span>
              <button
                onClick={() => setPage((p) => Math.min(totalPages - 1, p + 1))}
                disabled={page >= totalPages - 1}
                className="w-8 h-8 flex items-center justify-center border border-whisper-border rounded text-secondary hover:text-primary hover:border-primary transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <span className="material-symbols-outlined text-[16px]">chevron_right</span>
              </button>
            </div>
          </div>

          <p className="text-[10px] text-muted-slate mt-2 font-metadata-mono">
            {isAdmin
              ? 'Doble clic en celdas numéricas para editar. Enter para guardar, Esc para cancelar.'
              : 'Tu rol no permite edición.'}
          </p>
        </div>
      )}
    </>
  );
}
