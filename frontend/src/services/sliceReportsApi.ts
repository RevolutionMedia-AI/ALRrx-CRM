import { sliceClient } from '../services/sliceHttpClient';
import type {
  SliceReport,
  SliceReportSummary,
  SliceShopDailyRowPatch,
  SliceDailyAgentRowPatch,
  SliceChartData,
  SliceJobStatusDto,
  SliceUploadJobResponse,
} from '../slice/types';

export async function getSliceReports(): Promise<SliceReportSummary[]> {
  const { data } = await sliceClient.get<SliceReportSummary[]>('/reports');
  return data ?? [];
}

export async function getSliceReport(reportId: string): Promise<SliceReport> {
  const { data } = await sliceClient.get<SliceReport>(`/reports/${reportId}`);
  // Normalize: backend may omit empty sections, but every consumer assumes
  // arrays. Defensive defaulting prevents "Cannot read properties of undefined"
  // crashes in the UI when a section is null.
  return normalizeReport(data);
}

// ─── Period queries (DB-backed via SQLite) ────────────────────────────────────

/**
 * Returns summaries of reports whose ReportDate falls on the given UTC day.
 * The backend applies the standard access policy (admins see all, others see
 * their own). Optionally filter rows by Pod inside the report via `pod`.
 *
 * Bust-18: this now returns the lightweight summary projection
 * (header + counts + DailyGlobal rows only) — no ShopCallMetrics hydrating.
 */
export async function getSliceReportsByDate(
  date: string,
  pod?: string,
  opts?: { limit?: number; offset?: number }
): Promise<SliceReportSummary[]> {
  const params: Record<string, string | number> = { date };
  if (pod) params.pod = pod;
  if (opts?.limit !== undefined) params.limit = opts.limit;
  if (opts?.offset !== undefined) params.offset = opts.offset;
  const { data } = await sliceClient.get<SliceReportSummary[]>('/reports/daily', { params });
  return data ?? [];
}

/** Returns summaries of reports whose ReportDate falls in [start, end] inclusive. */
export async function getSliceReportsByDateRange(
  start: string,
  end: string,
  pod?: string,
  opts?: { limit?: number; offset?: number }
): Promise<SliceReportSummary[]> {
  const params: Record<string, string | number> = { start, end };
  if (pod) params.pod = pod;
  if (opts?.limit !== undefined) params.limit = opts.limit;
  if (opts?.offset !== undefined) params.offset = opts.offset;
  const { data } = await sliceClient.get<SliceReportSummary[]>('/reports/range', { params });
  return data ?? [];
}

/** Returns summaries of reports whose ReportDate falls within the given month (1-12). */
export async function getSliceReportsByMonth(
  year: number,
  month: number,
  pod?: string,
  opts?: { limit?: number; offset?: number }
): Promise<SliceReportSummary[]> {
  const params: Record<string, string | number> = { year, month };
  if (pod) params.pod = pod;
  if (opts?.limit !== undefined) params.limit = opts.limit;
  if (opts?.offset !== undefined) params.offset = opts.offset;
  const { data } = await sliceClient.get<SliceReportSummary[]>('/reports/monthly', { params });
  return data ?? [];
}

/** Applies the same defensive defaulting as getSliceReport to a report payload. */
function normalizeReport(data: SliceReport): SliceReport {
  return {
    ...data,
    shopDaily:        data.shopDaily        ?? [],
    dailyGlobal:      data.dailyGlobal      ?? [],
    dailyAgents:      data.dailyAgents      ?? [],
    shopCallMetrics:  data.shopCallMetrics  ?? [],
  };
}

export async function getSliceGlobalChart(reportId: string): Promise<SliceChartData> {
  const { data } = await sliceClient.get<SliceChartData>(`/reports/${reportId}/charts/global`);
  return data;
}

export async function patchSliceShopRow(
  reportId: string,
  shopName: string,
  patch: SliceShopDailyRowPatch
): Promise<unknown> {
  const { data } = await sliceClient.patch(
    `/reports/${reportId}/shop/${encodeURIComponent(shopName)}`,
    patch
  );
  return data;
}

export async function patchSliceAgentRow(
  reportId: string,
  agentEmail: string,
  patch: SliceDailyAgentRowPatch
): Promise<unknown> {
  const { data } = await sliceClient.patch(
    `/reports/${reportId}/agent/${encodeURIComponent(agentEmail)}`,
    patch
  );
  return data;
}

export function sliceExportUrl(reportId: string, format: 'xlsx' | 'csv'): string {
  return `/api/slice/reports/${reportId}/export/${format}`;
}

export function sliceTemplateUrl(reportId: string): string {
  return `/api/slice/reports/${reportId}/template`;
}

export function sliceBlankTemplateUrl(): string {
  return `/api/slice/reports/template/blank`;
}

export async function uploadSliceZip(file: File, onProgress?: (pct: number) => void): Promise<SliceUploadJobResponse> {
  const form = new FormData();
  form.append('file', file);
  const { data } = await sliceClient.post<SliceUploadJobResponse>('/fileupload/zip', form, {
    onUploadProgress: (e) => {
      if (e.total && onProgress) onProgress(Math.round((e.loaded * 100) / e.total));
    },
  });
  return data;
}

export async function uploadSliceExcel(
  files: File[],
  onProgress?: (pct: number) => void
): Promise<SliceUploadJobResponse> {
  const form = new FormData();
  files.forEach((f) => form.append('files', f));
  const { data } = await sliceClient.post<SliceUploadJobResponse>('/fileupload/excel', form, {
    onUploadProgress: (e) => {
      if (e.total && onProgress) onProgress(Math.round((e.loaded * 100) / e.total));
    },
  });
  return data;
}

export async function getSliceJobStatus(jobId: string): Promise<SliceJobStatusDto> {
  const { data } = await sliceClient.get<SliceJobStatusDto>(`/fileupload/status/${jobId}`);
  return data;
}

export async function getSliceJobs(): Promise<SliceJobStatusDto[]> {
  const { data } = await sliceClient.get<SliceJobStatusDto[]>('/fileupload/jobs');
  return data ?? [];
}
