import type { SliceJobStatus } from '../types';
import { normalizeJobStatus } from '../utils/jobStatus';

interface JobStatusCardProps {
  fileName: string;
  jobId: string | null;
  status: SliceJobStatus | 'Uploading' | 'Idle';
  processedFiles: number;
  totalFiles: number;
  uploadProgress: number;
  errorMessage?: string | null;
  reportId?: string | null;
  onDownload: () => void;
  onReset: () => void;
}

const STATUS_LABEL: Record<SliceJobStatus | 'Uploading' | 'Idle', string> = {
  Idle: 'Idle',
  Uploading: 'Uploading...',
  Pending: 'Queued...',
  Extracting: 'Extracting ZIP...',
  Processing: 'Processing Excel files...',
  Merging: 'Merging report...',
  Completed: 'Completed',
  Failed: 'Failed',
};

const STATUS_COLOR: Record<SliceJobStatus | 'Uploading' | 'Idle', string> = {
  Idle: 'text-muted-slate',
  Uploading: 'text-electric-blue',
  Pending: 'text-electric-blue',
  Extracting: 'text-amber-warmth',
  Processing: 'text-amber-warmth',
  Merging: 'text-amber-warmth',
  Completed: 'text-emerald-signal',
  Failed: 'text-deep-rose',
};

function computeProgress(
  status: SliceJobStatus | 'Uploading' | 'Idle',
  processedFiles: number,
  totalFiles: number,
  uploadProgress: number
): number {
  if (status === 'Uploading') return Math.min(99, uploadProgress);
  if (status === 'Idle' || totalFiles === 0) return 0;
  if (status === 'Completed') return 100;
  if (status === 'Failed') return 0;
  const pct = (processedFiles / totalFiles) * 100;
  return Math.min(95, Math.max(5, Math.round(pct)));
}

export default function JobStatusCard({
  fileName,
  jobId,
  status,
  processedFiles,
  totalFiles,
  uploadProgress,
  errorMessage,
  reportId,
  onDownload,
  onReset,
}: JobStatusCardProps) {
  // Normalizamos para que el badge, label y el switch de progreso siempre
  // matcheen contra un SliceJobStatus conocido (PascalCase), independientemente
  // de si el backend ya emite el enum como string o todavía como entero.
  const normalizedStatus: SliceJobStatus | 'Uploading' | 'Idle' =
    status === 'Uploading' || status === 'Idle' ? status : normalizeJobStatus(status);
  const progress = computeProgress(normalizedStatus, processedFiles, totalFiles, uploadProgress);
  const isTerminal = normalizedStatus === 'Completed' || normalizedStatus === 'Failed';
  const isActive = normalizedStatus === 'Uploading' || !isTerminal;

  return (
    <div className="flex-1 flex flex-col justify-center gap-6">
      {normalizedStatus === 'Idle' ? (
        <div className="flex flex-col items-center justify-center text-center py-10 gap-3">
          <span className="material-symbols-outlined text-5xl text-muted-slate/40">upload_file</span>
          <p className="text-sm text-secondary">No active process</p>
          <p className="text-xs text-muted-slate">Drop a file on the left to begin.</p>
        </div>
      ) : (
        <div
          className={`bg-surface-container-lowest border rounded-lg p-4 ${
            normalizedStatus === 'Failed' ? 'border-deep-rose/30 bg-deep-rose/5' : 'border-whisper-border'
          }`}
        >
          <div className="flex justify-between items-start mb-4 gap-3">
            <div className="min-w-0 flex-1">
              <p className="font-metadata-mono text-primary font-bold truncate">{fileName}</p>
              <p className={`text-xs mt-1 ${STATUS_COLOR[normalizedStatus]}`}>
                {STATUS_LABEL[normalizedStatus]}
                {jobId && !isTerminal && totalFiles > 0 && (
                  <span className="text-muted-slate">
                    {' '}• {processedFiles}/{totalFiles} files
                  </span>
                )}
              </p>
              {errorMessage && (
                <p className="text-xs text-deep-rose mt-1 break-words">{errorMessage}</p>
              )}
            </div>
            <span
              className={`material-symbols-outlined text-2xl ${
                isActive
                  ? 'text-emerald-signal animate-spin'
                  : normalizedStatus === 'Completed'
                  ? 'text-emerald-signal'
                  : 'text-deep-rose'
              }`}
              style={!isActive ? { fontVariationSettings: "'FILL' 1" } : undefined}
            >
              {normalizedStatus === 'Failed' ? 'error' : isActive ? 'sync' : 'check_circle'}
            </span>
          </div>

          <div className="w-full bg-surface-container-high h-1.5 rounded-full overflow-hidden">
            <div
              className={`h-full rounded-full transition-all duration-500 ${
                normalizedStatus === 'Failed' ? 'bg-deep-rose' : 'bg-primary'
              }`}
              style={{ width: `${progress}%` }}
            />
          </div>
          <div className="flex justify-between mt-2 font-metadata-mono text-[10px] text-outline">
            <span>{progress}%</span>
            <span>
              {jobId ? `Job ${jobId.slice(0, 8).toUpperCase()}` : 'Preparing...'}
            </span>
          </div>
        </div>
      )}

      <div className="mt-auto pt-6 border-t border-whisper-border space-y-2">
        {normalizedStatus === 'Completed' && reportId ? (
          <button
            onClick={onDownload}
            className="w-full flex items-center justify-center gap-2 bg-emerald-signal text-on-primary py-3 px-4 rounded font-bold hover:opacity-90 transition-opacity active:scale-[0.98] custom-shadow text-sm"
          >
            <span className="material-symbols-outlined">download</span>
            Download Processed Excel
          </button>
        ) : (
          <button
            disabled
            className="w-full flex items-center justify-center gap-2 bg-surface-container-low text-muted-slate py-3 px-4 rounded font-bold cursor-not-allowed text-sm"
          >
            <span className="material-symbols-outlined">download</span>
            Download Processed Excel
          </button>
        )}
        <p className="text-xs text-center text-secondary font-metadata-mono">
          {normalizedStatus === 'Completed' ? 'Report ready' : 'Available after validation completes'}
        </p>
        {isTerminal && (
          <button
            onClick={onReset}
            className="w-full text-xs text-secondary hover:text-primary transition-colors py-1"
          >
            ← Upload another file
          </button>
        )}
      </div>
    </div>
  );
}
