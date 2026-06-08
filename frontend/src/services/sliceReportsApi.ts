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
  return data;
}

export async function getSliceReport(reportId: string): Promise<SliceReport> {
  const { data } = await sliceClient.get<SliceReport>(`/reports/${reportId}`);
  // Normalize: backend may omit empty sections, but every consumer assumes
  // arrays. Defensive defaulting prevents "Cannot read properties of undefined"
  // crashes in the UI when a section is null.
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
  return data;
}
