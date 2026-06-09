import { useEffect, useState, useRef } from 'react';
import { getSliceJobStatus } from '../../services/sliceReportsApi';
import type { SliceJobStatusDto } from '../types';
import { isTerminalStatus, normalizeJobStatus } from '../utils/jobStatus';

interface PollingState {
  job: SliceJobStatusDto | null;
  loading: boolean;
  error: string | null;
  uploadProgress: number;
}

export function useSliceJobPolling(jobId: string | null) {
  const [state, setState] = useState<PollingState>({
    job: null,
    loading: false,
    error: null,
    uploadProgress: 0,
  });
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cancelledRef = useRef(false);

  useEffect(() => {
    cancelledRef.current = false;
    if (!jobId) {
      setState({ job: null, loading: false, error: null, uploadProgress: 0 });
      return;
    }
    setState((s) => ({ ...s, loading: true, error: null }));

    const tick = async () => {
      try {
        const job = await getSliceJobStatus(jobId);
        if (cancelledRef.current) return;
        const normalized = normalizeJobStatus(job.status);
        const terminal = isTerminalStatus(job.status);
        // Sobrescribimos el status con la versión normalizada para que el resto de
        // la UI (JobStatusCard, badge del ledger) reciba siempre PascalCase.
        const normalizedJob: SliceJobStatusDto = { ...job, status: normalized };
        setState({
          job: normalizedJob,
          loading: !terminal,
          error: job.errorMessage ?? null,
          uploadProgress: 100,
        });
        if (!terminal) {
          timerRef.current = setTimeout(tick, 2000);
        }
      } catch (e) {
        if (cancelledRef.current) return;
        setState((s) => ({
          ...s,
          loading: false,
          error: e instanceof Error ? e.message : 'Polling failed',
        }));
        timerRef.current = setTimeout(tick, 5000);
      }
    };
    tick();

    return () => {
      cancelledRef.current = true;
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [jobId]);

  const setUploadProgress = (pct: number) =>
    setState((s) => ({ ...s, uploadProgress: pct }));

  return { ...state, setUploadProgress };
}
