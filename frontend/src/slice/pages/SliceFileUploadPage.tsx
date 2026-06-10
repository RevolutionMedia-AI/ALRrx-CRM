import { useState, useEffect, useCallback } from 'react';
import {
  uploadSliceZip,
  uploadSliceExcel,
  getSliceJobs,
  sliceExportUrl,
  sliceTemplateUrl,
  sliceBlankTemplateUrl,
} from '../../services/sliceReportsApi';
import { useSliceJobPolling } from '../hooks/useSliceJobPolling';
import Dropzone from '../components/Dropzone';
import JobStatusCard from '../components/JobStatusCard';
import AuditLedgerTable from '../components/AuditLedgerTable';
import type { SliceJobStatusDto, SliceJobStatus } from '../types';

type Mode = 'zip' | 'excel';

export default function SliceFileUploadPage() {
  const [mode, setMode] = useState<Mode>('zip');
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [activeJobId, setActiveJobId] = useState<string | null>(null);
  const [activeFileName, setActiveFileName] = useState<string>('');
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const [jobs, setJobs] = useState<SliceJobStatusDto[]>([]);
  const [jobsLoading, setJobsLoading] = useState(false);

  const {
    job,
    loading: jobLoading,
    error: jobError,
    uploadProgress,
    setUploadProgress,
  } = useSliceJobPolling(activeJobId);

  const loadJobs = useCallback(async () => {
    setJobsLoading(true);
    try {
      const data = await getSliceJobs();
      setJobs(data);
    } catch {
      /* swallow; UI shows empty */
    } finally {
      setJobsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadJobs();
  }, [loadJobs]);

  useEffect(() => {
    if (job && (job.status === 'Completed' || job.status === 'Failed')) {
      loadJobs();
    }
  }, [job, loadJobs]);

  const handleSubmit = async () => {
    if (pendingFiles.length === 0) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      let res;
      if (mode === 'zip') {
        res = await uploadSliceZip(pendingFiles[0], setUploadProgress);
        setActiveFileName(pendingFiles[0].name);
      } else {
        res = await uploadSliceExcel(pendingFiles, setUploadProgress);
        setActiveFileName(
          pendingFiles.length === 1 ? pendingFiles[0].name : `${pendingFiles.length} files`
        );
      }
      setActiveJobId(res.jobId);
      setPendingFiles([]);
    } catch (e) {
      const msg = e && typeof e === 'object' && 'response' in e
        ? (e as { response?: { data?: { error?: string } } }).response?.data?.error
        : undefined;
      setSubmitError(msg ?? (e instanceof Error ? e.message : 'Upload failed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleReset = () => {
    setActiveJobId(null);
    setActiveFileName('');
    setSubmitError(null);
  };

  const handleDownload = async () => {
    if (!job?.reportId) return;
    const token = localStorage.getItem('slice_token');
    if (!token) return;
    try {
      const res = await fetch(sliceExportUrl(job.reportId, 'xlsx'), {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) {
        const text = await res.text().catch(() => '');
        console.error('Download failed', res.status, text);
        setSubmitError(`Download failed (${res.status}): ${text || res.statusText}`);
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `Slice_Report_${job.reportId.slice(0, 8)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (e) {
      console.error('Download exception', e);
      setSubmitError(e instanceof Error ? e.message : 'Download failed unexpectedly');
    }
  };

  const handleDownloadTemplate = async () => {
    const token = localStorage.getItem('slice_token');
    if (!token) return;
    try {
      const url = job?.reportId ? sliceTemplateUrl(job.reportId) : sliceBlankTemplateUrl();
      const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
      if (!res.ok) {
        const text = await res.text().catch(() => '');
        console.error('Template download failed', res.status, text);
        setSubmitError(`Template download failed (${res.status}): ${text || res.statusText}`);
        return;
      }
      const blob = await res.blob();
      const link = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = link;
      a.download = job?.reportId
        ? `Slice_Template_${job.reportId.slice(0, 8)}.xlsx`
        : 'Slice_Template_Blank.xlsx';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(link);
    } catch (e) {
      console.error('Template download exception', e);
      setSubmitError(e instanceof Error ? e.message : 'Template download failed unexpectedly');
    }
  };

  const status: SliceJobStatus | 'Uploading' | 'Idle' = submitting
    ? 'Uploading'
    : job
    ? job.status
    : 'Idle';

  const canSubmit =
    pendingFiles.length > 0 &&
    !submitting &&
    (mode === 'zip' ? pendingFiles.length === 1 : pendingFiles.length <= 12);

  return (
    <>
      <header className="flex flex-col gap-2">
        <h2 className="font-headline-lg text-headline-lg text-primary">Data Ingestion Core</h2>
        <p className="text-secondary max-w-2xl">
          Securely upload operational datasets for processing. All ingested files are permanently
          logged in the audit ledger.
        </p>
      </header>

      <div className="flex gap-2 border-b border-whisper-border">
        <button
          onClick={() => {
            setMode('zip');
            setPendingFiles([]);
            setSubmitError(null);
          }}
          className={`px-4 py-2 text-sm font-semibold border-b-2 transition-colors ${
            mode === 'zip'
              ? 'border-primary text-primary'
              : 'border-transparent text-secondary hover:text-primary'
          }`}
        >
          ZIP Archive
        </button>
        <button
          onClick={() => {
            setMode('excel');
            setPendingFiles([]);
            setSubmitError(null);
          }}
          className={`px-4 py-2 text-sm font-semibold border-b-2 transition-colors ${
            mode === 'excel'
              ? 'border-primary text-primary'
              : 'border-transparent text-secondary hover:text-primary'
          }`}
        >
          Excel Files (up to 12)
        </button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 items-start">
        <section className="lg:col-span-2 bg-pure-surface rounded-xl border border-whisper-border custom-shadow p-8 flex flex-col min-h-[400px]">
          <div className="flex items-center justify-between mb-6">
            <h3 className="font-metadata-mono text-xs font-bold text-primary">
              {mode === 'zip' ? 'SECURE_PAYLOAD_DROP' : 'EXCEL_BATCH_DROP'}
            </h3>
            <span className="material-symbols-outlined text-secondary">lock</span>
          </div>

          <div className="flex-1 flex flex-col">
            <Dropzone
              accept={mode === 'zip' ? '.zip' : '.xlsx,.xls,.xlsm'}
              multiple={mode === 'excel'}
              maxSizeMB={mode === 'zip' ? 200 : 50}
              disabled={submitting || jobLoading}
              onFiles={setPendingFiles}
            />

            {submitError && (
              <div className="mt-4 bg-deep-rose/10 border border-deep-rose/20 rounded-lg px-3 py-2 text-deep-rose text-xs flex items-center gap-2">
                <span className="material-symbols-outlined text-sm">error</span>
                {submitError}
              </div>
            )}

            <div className="mt-6 flex gap-3">
              <button
                type="button"
                onClick={handleDownloadTemplate}
                className="flex items-center justify-center gap-2 bg-surface border border-whisper-border text-primary py-3 px-4 rounded font-semibold hover:bg-surface-container transition-colors text-sm"
                title="Download an empty Excel template with the three sections (Daily Global, Daily Agent, Shop Daily). Fill it in and re-upload."
              >
                <span className="material-symbols-outlined text-lg">file_download</span>
                Download Excel Template
              </button>
              <button
                onClick={handleSubmit}
                disabled={!canSubmit}
                className="flex-1 flex items-center justify-center gap-2 bg-primary text-on-primary py-3 px-4 rounded font-bold hover:bg-inverse-surface transition-colors active:scale-[0.98] disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {submitting ? (
                  <>
                    <span className="material-symbols-outlined animate-spin">progress_activity</span>
                    Uploading...
                  </>
                ) : (
                  <>
                    <span className="material-symbols-outlined">rocket_launch</span>
                    Start Processing
                  </>
                )}
              </button>
              {pendingFiles.length > 0 && !submitting && (
                <button
                  onClick={() => setPendingFiles([])}
                  className="px-4 py-3 border border-whisper-border rounded text-secondary hover:text-deep-rose hover:border-deep-rose transition-colors text-sm"
                >
                  Clear
                </button>
              )}
            </div>
          </div>
        </section>

        <section className="lg:col-span-1 bg-pure-surface rounded-xl border border-whisper-border custom-shadow p-8 flex flex-col min-h-[400px]">
          <h3 className="font-metadata-mono text-xs font-bold text-primary border-b border-whisper-border pb-4 mb-6">
            ACTIVE_PROCESS
          </h3>
          <JobStatusCard
            fileName={activeFileName || (mode === 'zip' ? 'OP_DATA_W##.zip' : 'Excel batch')}
            jobId={activeJobId}
            status={status}
            processedFiles={job?.processedFiles ?? 0}
            totalFiles={job?.totalFiles ?? 0}
            uploadProgress={uploadProgress}
            errorMessage={jobError ?? job?.errorMessage}
            reportId={job?.reportId}
            onDownload={handleDownload}
            onReset={handleReset}
          />
        </section>
      </div>

      <AuditLedgerTable jobs={jobs} loading={jobsLoading} />
    </>
  );
}
