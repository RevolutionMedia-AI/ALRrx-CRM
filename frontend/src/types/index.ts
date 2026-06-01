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
  color?: string;
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

export interface SaleRecord {
  timestamp: string;
  sellerName: string;
  saleDate: string;
  customerEmail: string;
  package: string;
  amount: number;
}

export interface SalesSummary {
  totalSales: number;
  totalCount: number;
  lastSale: SaleRecord | null;
  allSales: SaleRecord[];
  availableSellers: string[];
  availablePackages: string[];
}
