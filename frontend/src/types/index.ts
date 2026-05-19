export interface TimeFilterDto {
  period: string;
  customStart?: string;
  customEnd?: string;
}

export interface MetricCardDto {
  label: string;
  value: string;
  trend?: string;
  format?: string;
  color?: string;
}

export interface ChartSeriesDto {
  name: string;
  data: number[];
}

export interface ChartDataDto {
  chartType: string;
  title: string;
  labels: string[];
  series: ChartSeriesDto[];
}

export interface DashboardSummaryDto {
  metrics: MetricCardDto[];
  charts: ChartDataDto[];
  lastUpdated: string;
}

export interface QueryDefinitionDto {
  id: string;
  name: string;
  description: string;
  category: string;
}

export interface ReportDto {
  reportName: string;
  columns: string[];
  rows: Record<string, unknown>[];
  generatedAt: string;
  timeRangeStart: string;
  timeRangeEnd: string;
}

export interface ExportRequestDto {
  reportId: string;
  format: string;
  timeFilter: TimeFilterDto;
}
