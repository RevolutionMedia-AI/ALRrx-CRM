export type SliceJobStatus =
  | 'Pending'
  | 'Extracting'
  | 'Processing'
  | 'Merging'
  | 'Completed'
  | 'Failed';

export interface SliceJobStatusDto {
  jobId: string;
  status: SliceJobStatus;
  totalFiles: number;
  processedFiles: number;
  errorMessage?: string | null;
  reportId?: string | null;
  createdAt: string;
  completedAt?: string | null;
  createdByEmail?: string;
}

export interface SliceUploadJobResponse {
  jobId: string;
  fileCount: number;
  status: string;
}

export interface SliceShopDailyRow {
  shopName: string;
  shopId: string;
  totalOrders: number;
  refundedOrders: number;
  errorRate: number;
  conversionRate: number;
}

export interface SliceShopCallMetricsRow {
  weekStart: string;
  shopId: string;
  shopName: string;
  podId: string;
  totalCalls: number;
  overflowCalls: number;
  queueCalls: number;
  handledCalls: number;
  missedCalls: number;
  transferredCalls: number;
  pctOverflow: number;
  pctQueued: number;
  pctHandled: number;
  pctMissedOfQueued: number;
  pctTransferred: number;
}

export interface SliceDailyAgentRow {
  pod: string;
  supervisorName: string;
  agentEmail: string;
  hc: number;
  tc: number;
  numberOfHolds: number;
  avgHoldTime: number;
  asa: number;
  aht: number;
  acw: number;
  pctContactsOnHold: number;
  pctSLUnder15Sec: number;
  pctTransfers: number;
  shift: string;
}

export interface SliceDailyGlobalRow {
  pod: string;
  queued: number;
  handled: number;
  missedCalls: number;
  transferredCalls: number;
  pctQueued: number;
  pctHandled: number;
  pctMissed: number;
  pctTransferred: number;
  convPct: number;
  orderCount: number;
  refundedOrders: number;
  pctOrdersWithErrors: number;
}

export interface SliceReportSummary {
  id: string;
  reportDate: string;
  generatedAt: string;
  podCount: number;
  agentCount: number;
  mergedCsvPath?: string | null;
  mergedXlsxPath?: string | null;
}

export interface SliceReport {
  id: string;
  jobId: string;
  reportDate: string;
  generatedAt: string;
  generatedByEmail: string;
  shopDaily: SliceShopDailyRow[];
  dailyGlobal: SliceDailyGlobalRow[];
  dailyAgents: SliceDailyAgentRow[];
  shopCallMetrics: SliceShopCallMetricsRow[];
  mergedCsvPath?: string | null;
  mergedXlsxPath?: string | null;
}

export interface SliceShopDailyRowPatch {
  totalOrders?: number;
  refundedOrders?: number;
  errorRate?: number;
  conversionRate?: number;
}

export interface SliceDailyAgentRowPatch {
  hc?: number;
  tc?: number;
  numberOfHolds?: number;
  avgHoldTime?: number;
  asa?: number;
  aht?: number;
  acw?: number;
  pctContactsOnHold?: number;
  pctSLUnder15Sec?: number;
  pctTransfers?: number;
  shift?: string;
  supervisorName?: string;
}

export interface SliceChartData {
  label: string;
  series: { name: string; values: number[] }[];
}

export interface SliceUserInfo {
  id: string;
  email: string;
  fullName: string;
  role: 'Admin' | 'Supervisor' | 'Viewer';
  createdAt: string;
}

export interface SliceLoginResponse {
  token: string;
  email: string;
  fullName: string;
  role: 'Admin' | 'Supervisor' | 'Viewer';
  expiresAt: string;
}
