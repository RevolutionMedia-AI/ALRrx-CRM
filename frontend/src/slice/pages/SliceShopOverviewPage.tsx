import { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useSliceAuth } from '../context/SliceAuthContext';
import {
  getSliceReports,
  getSliceReport,
  patchSliceShopRow,
  sliceExportUrl,
} from '../../services/sliceReportsApi';
import type { SliceReport, SliceReportSummary, SliceShopDailyRow } from '../types';

type Period = 'Diaria' | 'Semanal' | 'Mensual';

function formatPct(v: number): string {
  return `${v.toFixed(1)}%`;
}

function formatInt(v: number): string {
  return new Intl.NumberFormat('en-US').format(v);
}

function isCriticalRow(row: SliceShopDailyRow): boolean {
  return row.totalOrders > 0 && row.conversionRate < 40;
}

interface EditableCellProps {
  value: number;
  type: 'int' | 'pct';
  disabled: boolean;
  onCommit: (val: number) => Promise<void>;
}

function EditableCell({ value, type, disabled, onCommit }: EditableCellProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(String(value));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!editing) setDraft(String(value));
  }, [value, editing]);

  const display = type === 'pct' ? formatPct(value) : formatInt(value);

  if (disabled || !editing) {
    return (
      <td
        className="py-3 px-4 text-right text-on-surface font-medium cursor-pointer hover:bg-electric-blue/5 transition-colors"
        onDoubleClick={() => !disabled && setEditing(true)}
        title={disabled ? undefined : 'Doble clic para editar'}
      >
        {busy ? <span className="text-xs text-secondary">...</span> : display}
        {error && <span className="block text-[10px] text-deep-rose mt-0.5">{error}</span>}
      </td>
    );
  }

  return (
    <td className="py-3 px-4 text-right">
      <input
        autoFocus
        type="number"
        step={type === 'pct' ? '0.1' : '1'}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={async () => {
          const parsed = parseFloat(draft);
          if (Number.isNaN(parsed)) {
            setEditing(false);
            return;
          }
          setBusy(true);
          setError(null);
          try {
            await onCommit(parsed);
            setEditing(false);
          } catch (e) {
            setError(e instanceof Error ? e.message : 'Error');
          } finally {
            setBusy(false);
          }
        }}
        onKeyDown={async (e) => {
          if (e.key === 'Enter') {
            (e.target as HTMLInputElement).blur();
          } else if (e.key === 'Escape') {
            setDraft(String(value));
            setEditing(false);
          }
        }}
        className="w-24 text-right px-2 py-1 text-sm border border-electric-blue rounded outline-none focus:ring-2 focus:ring-electric-blue/30"
      />
    </td>
  );
}

export default function SliceShopOverviewPage() {
  const { isAdmin } = useSliceAuth();
  const navigate = useNavigate();

  const [period, setPeriod] = useState<Period>('Diaria');
  const [reports, setReports] = useState<SliceReportSummary[]>([]);
  const [selectedReportId, setSelectedReportId] = useState<string | null>(null);
  const [report, setReport] = useState<SliceReport | null>(null);
  const [search, setSearch] = useState('');

  const [loadingReports, setLoadingReports] = useState(false);
  const [loadingReport, setLoadingReport] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadReports = useCallback(async () => {
    setLoadingReports(true);
    setError(null);
    try {
      const data = await getSliceReports();
      setReports(data);
      if (data.length > 0 && !selectedReportId) {
        setSelectedReportId(data[0].id);
      }
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
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load report');
    } finally {
      setLoadingReport(false);
    }
  }, []);

  useEffect(() => {
    if (selectedReportId) loadReport(selectedReportId);
  }, [selectedReportId, loadReport]);

  const handlePatch = async (shopName: string, patch: { totalOrders?: number; refundedOrders?: number; errorRate?: number; conversionRate?: number }) => {
    if (!selectedReportId) return;
    await patchSliceShopRow(selectedReportId, shopName, patch);
    await loadReport(selectedReportId);
  };

  const filteredRows = useMemo(() => {
    if (!report) return [];
    const q = search.trim().toLowerCase();
    if (!q) return report.shopDaily;
    return report.shopDaily.filter((r) => r.shopName.toLowerCase().includes(q));
  }, [report, search]);

  const downloadBlob = (url: string, filename: string) => {
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
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
        downloadBlob(url, `Slice_Report_${new Date(report.reportDate).toISOString().slice(0, 10)}.${format}`);
        URL.revokeObjectURL(url);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Export failed'));
  };

  return (
    <>
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-4 shrink-0">
        <div>
          <h2 className="font-headline-lg text-3xl font-bold text-primary mb-1">Shop Overview</h2>
          <p className="text-steel-secondary">Granular shop-level performance analysis.</p>
        </div>
        <div className="flex gap-3 flex-wrap">
          <div className="relative">
            <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-secondary text-lg">search</span>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 pr-4 py-2 bg-surface border border-whisper-border rounded-lg text-sm focus:ring-2 focus:ring-primary/20 focus:border-primary outline-none w-full md:w-64 transition-all shadow-sm"
              placeholder="Search Shop ID..."
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
                {new Date(r.reportDate).toLocaleDateString()} — {r.podCount} pods / {r.agentCount} agents
              </option>
            ))}
          </select>
          <div className="flex border border-whisper-border rounded-lg overflow-hidden bg-surface">
            {(['Diaria', 'Semanal', 'Mensual'] as Period[]).map((p) => (
              <button
                key={p}
                onClick={() => setPeriod(p)}
                className={`px-3 py-2 text-sm border-r last:border-r-0 border-whisper-border transition-colors ${
                  period === p
                    ? 'bg-primary text-on-primary font-semibold'
                    : 'text-secondary hover:bg-surface-container'
                }`}
              >
                {p}
              </button>
            ))}
          </div>
          <button
            onClick={() => handleExport('xlsx')}
            disabled={!selectedReportId}
            className="flex items-center gap-2 px-4 py-2 bg-emerald-signal text-white rounded-lg text-sm font-semibold hover:bg-emerald-600 transition-colors shadow-sm disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <span className="material-symbols-outlined text-lg">download</span>
            Export XLSX
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      {loadingReports && !report && (
        <div className="bg-surface border border-whisper-border rounded-xl p-10 text-center text-secondary text-sm">
          Loading reports...
        </div>
      )}

      {!loadingReports && reports.length === 0 && !error && (
        <div className="bg-surface border border-whisper-border rounded-xl p-10 flex flex-col items-center text-center">
          <span className="material-symbols-outlined text-4xl text-muted-slate/40 mb-2">inbox</span>
          <p className="text-sm font-medium text-primary">No reports yet</p>
          <p className="text-xs text-muted-slate mt-1">
            Upload an Excel or ZIP file to generate the first report.
          </p>
          <button
            onClick={() => navigate('/slice/upload')}
            className="mt-4 px-4 py-2 bg-electric-blue text-white rounded-lg text-sm font-semibold hover:bg-electric-blue/90 transition-colors"
          >
            Go to Upload Center
          </button>
        </div>
      )}

      {report && (
        <div className="flex-1 bg-surface border border-whisper-border rounded-xl shadow-sm overflow-hidden flex flex-col">
          <div className="px-4 py-2 border-b border-whisper-border bg-surface-container/40 flex items-center justify-between text-xs text-steel-secondary">
            <span>
              Report <span className="font-metadata-mono text-primary">{report.id.slice(0, 8)}</span> •{' '}
              {new Date(report.reportDate).toLocaleDateString()} • {report.shopDaily.length} shops • Owner{' '}
              <span className="text-primary">{report.generatedByEmail}</span>
            </span>
            {!isAdmin && (
              <span className="flex items-center gap-1 text-muted-slate">
                <span className="material-symbols-outlined text-sm">visibility</span>
                Read-only (Viewer)
              </span>
            )}
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse whitespace-nowrap">
              <thead>
                <tr className="border-b border-whisper-border bg-surface-container/50 text-steel-secondary text-xs uppercase tracking-wider font-semibold">
                  <th className="py-3 px-4 sticky left-0 bg-surface-container/50 z-10">Shop ID</th>
                  <th className="py-3 px-4 text-right">Total Orders</th>
                  <th className="py-3 px-4 text-right">Refunded</th>
                  <th className="py-3 px-4 text-right">Error Rate</th>
                  <th className="py-3 px-4 text-right">Conversion</th>
                  <th className="py-3 px-4 text-right">Revenue Est.</th>
                </tr>
              </thead>
              <tbody className="font-metadata-mono text-[13px] divide-y divide-whisper-border">
                {loadingReport ? (
                  <tr>
                    <td colSpan={6} className="py-10 text-center text-secondary text-sm">
                      Loading report data...
                    </td>
                  </tr>
                ) : filteredRows.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="py-10 text-center text-secondary text-sm">
                      No shops match "{search}".
                    </td>
                  </tr>
                ) : (
                  filteredRows.map((row) => {
                    const critical = isCriticalRow(row);
                    return (
                      <tr
                        key={row.shopName}
                        className={`transition-colors ${
                          critical
                            ? 'bg-rose-50/50 hover:bg-rose-50 border-l-2 border-l-deep-rose'
                            : 'hover:bg-surface-container/30'
                        }`}
                      >
                        <td
                          className={`py-3 px-4 font-semibold sticky left-0 ${
                            critical ? 'text-deep-rose bg-rose-50/50' : 'text-primary bg-surface'
                          }`}
                        >
                          <div className="flex items-center gap-1.5">
                            {critical && <span className="w-1.5 h-1.5 rounded-full bg-deep-rose inline-block" />}
                            {row.shopName}
                          </div>
                        </td>
                        <EditableCell
                          value={row.totalOrders}
                          type="int"
                          disabled={!isAdmin}
                          onCommit={(v) => handlePatch(row.shopName, { totalOrders: v })}
                        />
                        <EditableCell
                          value={row.refundedOrders}
                          type="int"
                          disabled={!isAdmin}
                          onCommit={(v) => handlePatch(row.shopName, { refundedOrders: v })}
                        />
                        <EditableCell
                          value={row.errorRate}
                          type="pct"
                          disabled={!isAdmin}
                          onCommit={(v) => handlePatch(row.shopName, { errorRate: v })}
                        />
                        <td
                          className={`py-3 px-4 text-right font-semibold ${
                            row.conversionRate < 40
                              ? 'text-deep-rose'
                              : row.conversionRate >= 60
                              ? 'text-emerald-signal'
                              : 'text-on-surface'
                          }`}
                        >
                          {formatPct(row.conversionRate)}
                        </td>
                        <td className="py-3 px-4 text-right text-on-surface">
                          {(() => {
                            const net = Math.max(0, row.totalOrders - row.refundedOrders);
                            return formatInt(net);
                          })()}
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
          <div className="mt-auto py-3 px-4 border-t border-whisper-border flex items-center justify-between bg-surface">
            <span className="text-sm text-steel-secondary">
              Showing <span className="font-medium text-primary">{filteredRows.length}</span> of{' '}
              <span className="font-medium text-primary">{report.shopDaily.length}</span> shops
            </span>
            <span className="text-xs text-muted-slate">
              {isAdmin
                ? 'Doble clic en una celda para editar. Enter para guardar, Esc para cancelar.'
                : 'Tu rol no permite edición.'}
            </span>
          </div>
        </div>
      )}
    </>
  );
}
