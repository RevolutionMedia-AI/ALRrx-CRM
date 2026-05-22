import { client } from './httpClient';
import type {
  DashboardSummaryDto,
  QueryDefinitionDto,
  ReportDto,
  ExportRequestDto,
  TimeFilterDto,
} from '../types';

export async function getDashboardSummary(filter: TimeFilterDto): Promise<DashboardSummaryDto> {
  const params: Record<string, string> = { period: filter.period };
  if (filter.customStart) params.customStart = filter.customStart;
  if (filter.customEnd) params.customEnd = filter.customEnd;
  const { data } = await client.get<DashboardSummaryDto>('/dashboard/summary', { params });
  return data;
}

export async function getAvailableQueries(): Promise<QueryDefinitionDto[]> {
  const { data } = await client.get<QueryDefinitionDto[]>('/reports');
  return data;
}

export async function getReport(reportId: string, filter: TimeFilterDto): Promise<ReportDto> {
  const params: Record<string, string> = { period: filter.period };
  if (filter.customStart) params.customStart = filter.customStart;
  if (filter.customEnd) params.customEnd = filter.customEnd;
  const { data } = await client.get<ReportDto>(`/reports/${reportId}`, { params });
  return data;
}

export async function getStaffing(): Promise<ReportDto> {
  const { data } = await client.get<ReportDto>('/staffing');
  return data;
}

export async function exportReport(request: ExportRequestDto): Promise<Blob> {
  const { data } = await client.post('/export', request, {
    responseType: 'blob',
  });
  return data;
}

export async function exportDashboardPdf(filter: TimeFilterDto): Promise<Blob> {
  const { data } = await client.post('/dashboard-export/pdf', filter, {
    responseType: 'blob',
  });
  return data;
}
