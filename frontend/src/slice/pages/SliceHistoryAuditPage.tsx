import { useCallback, useEffect, useState } from 'react';
import { getSliceJobs } from '../../services/sliceReportsApi';
import AuditLedgerTable from '../components/AuditLedgerTable';
import type { SliceJobStatusDto } from '../types';

type StatusFilter = 'all' | 'Completed' | 'Failed' | 'InProgress';

function getJobSearchHaystack(job: SliceJobStatusDto): string {
  const createdBy = (job as SliceJobStatusDto & { createdByEmail?: string }).createdByEmail ?? '';
  return [job.jobId, job.reportId ?? '', createdBy, job.status, job.errorMessage ?? '']
    .join(' ')
    .toLowerCase();
}

function isInProgress(status: string): boolean {
  return status === 'Pending' || status === 'Extracting' || status === 'Processing' || status === 'Merging';
}

function matchesStatusFilter(job: SliceJobStatusDto, filter: StatusFilter): boolean {
  switch (filter) {
    case 'all':
      return true;
    case 'Completed':
      return job.status === 'Completed';
    case 'Failed':
      return job.status === 'Failed';
    case 'InProgress':
      return isInProgress(job.status);
  }
}

export default function SliceHistoryAuditPage() {
  const [jobs, setJobs] = useState<SliceJobStatusDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');

  const loadJobs = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getSliceJobs();
      setJobs(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load audit ledger');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadJobs();
  }, [loadJobs]);

  const filteredJobs = jobs.filter((job) => {
    if (!matchesStatusFilter(job, statusFilter)) return false;
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return getJobSearchHaystack(job).includes(q);
  });

  const counts = {
    all: jobs.length,
    Completed: jobs.filter((j) => j.status === 'Completed').length,
    Failed: jobs.filter((j) => j.status === 'Failed').length,
    InProgress: jobs.filter((j) => isInProgress(j.status)).length,
  };

  return (
    <>
      <div className="flex flex-col gap-2 max-w-3xl">
        <h2 className="font-headline-lg text-3xl font-bold text-primary">System Audit Ledger</h2>
        <p className="text-secondary">
          Immutable record of all operational and system-level events.
        </p>
      </div>

      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 bg-pure-surface p-4 rounded-xl border border-whisper-border shadow-sm">
        <div className="relative w-full md:w-96">
          <span className="material-symbols-outlined absolute left-4 top-1/2 -translate-y-1/2 text-muted-slate">
            search
          </span>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-12 pr-4 py-3 bg-canvas-white border border-whisper-border rounded-lg text-sm text-primary focus:border-electric-blue focus:ring-1 focus:ring-electric-blue outline-none transition-all"
            placeholder="Search Event ID, Owner, or Report ID..."
            type="text"
          />
        </div>
        <div className="flex items-center gap-2 flex-wrap w-full md:w-auto">
          {(['all', 'InProgress', 'Completed', 'Failed'] as StatusFilter[]).map((f) => {
            const active = statusFilter === f;
            const label =
              f === 'all' ? 'All' : f === 'InProgress' ? 'In Progress' : f;
            const count = counts[f];
            return (
              <button
                key={f}
                onClick={() => setStatusFilter(f)}
                className={`flex items-center gap-2 px-4 py-2 border rounded-lg text-xs font-metadata-mono transition-colors ${
                  active
                    ? 'bg-primary text-on-primary border-primary'
                    : 'bg-canvas-white border-whisper-border text-secondary hover:text-primary hover:border-outline-variant'
                }`}
              >
                {label}
                <span
                  className={`px-1.5 py-0.5 rounded text-[10px] ${
                    active ? 'bg-on-primary/20 text-on-primary' : 'bg-surface-container text-secondary'
                  }`}
                >
                  {count}
                </span>
              </button>
            );
          })}
        </div>
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-xl p-4 text-deep-rose text-sm flex items-center gap-2">
          <span className="material-symbols-outlined text-base">error</span>
          {error}
        </div>
      )}

      <div className="text-xs text-secondary font-metadata-mono">
        Showing <span className="text-primary">{filteredJobs.length}</span> of{' '}
        <span className="text-primary">{jobs.length}</span> ledger entries
      </div>

      <AuditLedgerTable jobs={filteredJobs} loading={loading} />
    </>
  );
}
