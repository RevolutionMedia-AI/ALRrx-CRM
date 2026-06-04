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
  _seller?: string,
  _pkg?: string
): Promise<SalesSummary> {
  const params: Record<string, string> = {};
  if (filter.period === 'Custom' && filter.customStart) params.from = `${filter.customStart} 00:00:00`;
  if (filter.period === 'Custom' && filter.customEnd) params.to = `${filter.customEnd} 23:59:59`;
  if (filter.period === 'Today') {
    const now = new Date();
    const tp = getTijuanaDateParts(now);
    const todayLocalMidnight = new Date(tp.year, tp.month - 1, tp.day, 0, 0, 0);
    const tomorrow = new Date(todayLocalMidnight.getTime() + 24 * 60 * 60 * 1000);
    params.from = formatTijuanaDateTime(todayLocalMidnight);
    params.to = formatTijuanaDateTime(tomorrow);
  }
  if (filter.period === 'Week') {
    const now = new Date();
    const tp = getTijuanaDateParts(now);
    const todayLocalMidnight = new Date(tp.year, tp.month - 1, tp.day, 0, 0, 0);
    const daysSinceMonday = tp.dayOfWeek === 0 ? 6 : tp.dayOfWeek - 1;
    const startLocalMidnight = new Date(todayLocalMidnight.getTime() - daysSinceMonday * 24 * 60 * 60 * 1000);
    const tomorrow = new Date(todayLocalMidnight.getTime() + 24 * 60 * 60 * 1000);
    params.from = formatTijuanaDateTime(startLocalMidnight);
    params.to = formatTijuanaDateTime(tomorrow);
  }
  if (filter.period === 'Month') {
    const now = new Date();
    const tp = getTijuanaDateParts(now);
    const startLocalMidnight = new Date(tp.year, tp.month - 1, 1, 0, 0, 0);
    const nextMonthStart = new Date(tp.year, tp.month, 1, 0, 0, 0);
    params.from = formatTijuanaDateTime(startLocalMidnight);
    params.to = formatTijuanaDateTime(nextMonthStart);
  }
  const { data } = await client.get<SalesSummary>('/vicidial-form/sales/summary', { params });
  return data;
}

const BUSINESS_TZ = 'America/Tijuana';

const TIJUANA_FMT = new Intl.DateTimeFormat('en-CA', {
  timeZone: BUSINESS_TZ,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
});

const TIJUANA_DATE_FMT = new Intl.DateTimeFormat('en-CA', {
  timeZone: BUSINESS_TZ,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

function formatTijuanaDateTime(d: Date): string {
  const parts = TIJUANA_FMT.formatToParts(d);
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? '00';
  return `${get('year')}-${get('month')}-${get('day')} ${get('hour')}:${get('minute')}:${get('second')}`;
}

function getTijuanaDateParts(d: Date): { year: number; month: number; day: number; dayOfWeek: number } {
  const dateStr = TIJUANA_DATE_FMT.format(d);
  const [y, m, day] = dateStr.split('-').map(Number);
  const dayOfWeek = new Date(y, m - 1, day).getDay();
  return { year: y, month: m, day, dayOfWeek };
}
