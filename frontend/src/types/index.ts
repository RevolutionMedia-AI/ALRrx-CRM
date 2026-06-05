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

export const BUNDLE_OPTIONS = [
  'GLP-1 1 Month',
  'GLP-1 3 Months',
  'GLP-1 6 Months',
  'GLP-1 12 Months',
  'GLP-1/GIP 1 Month',
  'GLP-1/GIP 3 Months',
  'GLP-1/GIP 6 Months',
  'GLP-1/GIP 12 Months',
] as const;

export type BundleOption = typeof BUNDLE_OPTIONS[number];

export interface VicidialSaleRequest {
  leadId?: number;
  salesRep: string;
  saleDate: string;
  clientPhone: string;
  clientName: string;
  clientEmail: string;
  bundle: BundleOption;
  amount: number;
}

export interface VicidialSaleDto {
  id: number;
  leadId?: number | null;
  salesRep: string;
  saleDate: string;
  clientPhone: string;
  clientName: string;
  clientEmail: string;
  bundle: string;
  amount: number;
  createdAt: string;
}

export interface ActiveAltrxAgentDto {
  user: string;
  fullName: string;
}

export interface VicidialAuthResponse {
  token: string;
  expiresAt: string;
  formName: string;
}

export interface VicidialLeadDto {
  leadId: number;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  email: string;
}
