import { client } from './httpClient';
import type {
  DashboardSummaryDto,
  QueryDefinitionDto,
  ReportDto,
  ExportRequestDto,
  TimeFilterDto,
  SalesSummary,
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

export async function exportDashboardExcel(filter: TimeFilterDto): Promise<Blob> {
  const { data } = await client.post('/dashboard-export/excel', filter, {
    responseType: 'blob',
  });
  return data;
}

export async function exportPeriodComparisonExcel(period1: TimeFilterDto, period2: TimeFilterDto): Promise<Blob> {
  const { data } = await client.post('/period-comparison/excel', { period1, period2 }, {
    responseType: 'blob',
  });
  return data;
}

export async function getGoogleSheetsSales(
  filter: TimeFilterDto,
  seller?: string,
  pkg?: string
): Promise<SalesSummary> {
  const params: Record<string, string> = { period: filter.period };
  if (filter.customStart) params.customStart = filter.customStart;
  if (filter.customEnd) params.customEnd = filter.customEnd;
  if (seller && seller !== 'all') params.seller = seller;
  if (pkg && pkg !== 'all') params.package = pkg;
  const { data } = await client.get<SalesSummary>('/dashboard/google-sheets/sales', { params });
  return data;
}
