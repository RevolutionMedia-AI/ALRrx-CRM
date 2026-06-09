import { useNavigate } from 'react-router-dom';
import type { SliceJobStatus, SliceJobStatusDto } from '../types';
import { sliceExportUrl } from '../../services/sliceReportsApi';
import { normalizeJobStatus } from '../utils/jobStatus';

interface AuditLedgerTableProps {
  jobs: SliceJobStatusDto[];
  loading: boolean;
}

const STATUS_BADGE: Record<SliceJobStatus, { label: string; classes: string; dot: string }> = {
  Pending: { label: 'Pending', classes: 'bg-muted-slate/10 text-muted-slate border-muted-slate/20', dot: 'bg-muted-slate' },
  Extracting: { label: 'Extracting', classes: 'bg-amber-warmth/10 text-amber-warmth border-amber-warmth/20', dot: 'bg-amber-warmth' },
  Processing: { label: 'Processing', classes: 'bg-amber-warmth/10 text-amber-warmth border-amber-warmth/20', dot: 'bg-amber-warmth' },
  Merging: { label: 'Merging', classes: 'bg-amber-warmth/10 text-amber-warmth border-amber-warmth/20', dot: 'bg-amber-warmth' },
  Completed: { label: 'Completed', classes: 'bg-emerald-signal/10 text-emerald-signal border-emerald-signal/20', dot: 'bg-emerald-signal' },
  Failed: { label: 'Failed', classes: 'bg-deep-rose/10 text-deep-rose border-deep-rose/20', dot: 'bg-deep-rose' },
};

// Fallback para status que el backend emita como string crudo que no pudimos
// normalizar (p.ej. "3" si la respuesta viniera como entero). Evita el crash
// "Cannot read properties of undefined (reading 'classes')" y muestra el valor
// real para que sea fácil de debuggear.
const UNKNOWN_BADGE = {
  label: 'Unknown',
  classes: 'bg-muted-slate/10 text-muted-slate border-muted-slate/20',
  dot: 'bg-muted-slate',
};

function formatUtc(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}`;
}

function shortId(id: string): string {
  return `UL-${id.replace(/-/g, '').slice(0, 6).toUpperCase()}`;
}

export default function AuditLedgerTable({ jobs, loading }: AuditLedgerTableProps) {
  const navigate = useNavigate();

  const handleDownload = async (reportId: string) => {
    const token = localStorage.getItem('slice_token');
    if (!token) return;
    const res = await fetch(sliceExportUrl(reportId, 'xlsx'), {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) return;
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Slice_Report_${reportId.slice(0, 8)}.xlsx`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  return (
    <section className="bg-pure-surface rounded-xl border border-whisper-border custom-shadow overflow-hidden">
      <div className="p-6 border-b border-whisper-border bg-surface-container-lowest flex justify-between items-center">
        <h3 className="font-metadata-mono text-xs font-bold text-primary flex items-center gap-2">
          <span className="material-symbols-outlined text-sm">history</span>
          AUDIT_LEDGER
        </h3>
        <div className="flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-emerald-signal block animate-pulse" />
          <span className="font-metadata-mono text-[10px] text-secondary">SYSTEM ONLINE</span>
          <span className="text-[10px] text-muted-slate ml-2">{jobs.length} jobs</span>
        </div>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-surface-container-low font-metadata-mono text-xs text-secondary border-b border-whisper-border">
              <th className="p-4 font-normal">Upload ID</th>
              <th className="p-4 font-normal">Timestamp (UTC)</th>
              <th className="p-4 font-normal">Owner</th>
              <th className="p-4 font-normal">Status</th>
              <th className="p-4 font-normal text-right">Files</th>
              <th className="p-4 font-normal text-right">Action</th>
            </tr>
          </thead>
          <tbody className="text-sm">
            {loading ? (
              <tr>
                <td colSpan={6} className="p-8 text-center text-secondary">Loading audit ledger...</td>
              </tr>
            ) : jobs.length === 0 ? (
              <tr>
                <td colSpan={6} className="p-8 text-center text-muted-slate">
                  No uploads yet. Drop a file above to start.
                </td>
              </tr>
            ) : (
              jobs.map((job) => {
                const normalized = normalizeJobStatus(job.status);
                const badge = STATUS_BADGE[normalized] ?? UNKNOWN_BADGE;
                const rawStatus = job.status;
                return (
                  <tr
                    key={job.jobId}
                    className="border-b border-whisper-border hover:bg-surface-container-lowest transition-colors group"
                  >
                    <td className="p-4 font-metadata-mono text-primary">{shortId(job.jobId)}</td>
                    <td className="p-4 font-metadata-mono text-secondary">{formatUtc(job.createdAt)}</td>
                    <td className="p-4 text-secondary text-xs font-metadata-mono">
                      {(job as SliceJobStatusDto & { createdByEmail?: string }).createdByEmail ?? '—'}
                    </td>
                    <td className="p-4">
                      <span
                        className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-[10px] font-bold border font-metadata-mono uppercase tracking-wider ${badge.classes}`}
                        title={
                          normalized === rawStatus
                            ? undefined
                            : `Status crudo del backend: ${String(rawStatus)}`
                        }
                      >
                        <span className={`w-1.5 h-1.5 rounded-full ${badge.dot}`} />
                        {badge.label}
                        {badge === UNKNOWN_BADGE && rawStatus !== undefined && (
                          <span className="text-[9px] opacity-70 ml-1">({String(rawStatus)})</span>
                        )}
                      </span>
                    </td>
                    <td className="p-4 text-right font-metadata-mono text-xs text-secondary">
                      {job.processedFiles}/{job.totalFiles || '—'}
                    </td>
                    <td className="p-4 text-right">
                      {job.status === 'Completed' && job.reportId ? (
                        <div className="flex justify-end gap-2">
                          <button
                            onClick={() => handleDownload(job.reportId!)}
                            className="font-metadata-mono text-xs px-3 py-1 border border-whisper-border rounded text-secondary hover:text-emerald-signal hover:border-emerald-signal transition-colors"
                          >
                            Download
                          </button>
                          <button
                            onClick={() => navigate('/slice')}
                            className="font-metadata-mono text-xs px-3 py-1 border border-whisper-border rounded text-secondary hover:text-electric-blue hover:border-electric-blue transition-colors"
                          >
                            View
                          </button>
                        </div>
                      ) : (
                        <span className="font-metadata-mono text-[10px] text-muted-slate">—</span>
                      )}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
}
