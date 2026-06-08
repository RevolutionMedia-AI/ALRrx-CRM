import axios from 'axios';
import type { VicidialSaleRequest, VicidialSaleDto, ActiveAltrxAgentDto, SalesSummary, VicidialLeadDto } from '../types';

export const vicidialClient = axios.create({ baseURL: '/api', timeout: 30000 });

vicidialClient.interceptors.response.use(
  (res) => res,
  (err) => Promise.reject(err),
);

export async function submitVicidialSale(payload: VicidialSaleRequest): Promise<{ id: number; message: string }> {
  const { data } = await vicidialClient.post<{ id: number; message: string }>('/vicidial-form/sale', payload);
  return data;
}

export async function listVicidialSales(salesRep: string, from?: string, to?: string, limit = 50): Promise<VicidialSaleDto[]> {
  const params: Record<string, string | number> = { salesRep, limit };
  if (from) params.from = from;
  if (to) params.to = to;
  const { data } = await vicidialClient.get<VicidialSaleDto[]>('/vicidial-form/sales', { params });
  return data;
}

export async function listAllVicidialSales(from?: string, to?: string, limit = 500): Promise<VicidialSaleDto[]> {
  const params: Record<string, string | number> = { limit };
  if (from) params.from = from;
  if (to) params.to = to;
  const { data } = await vicidialClient.get<VicidialSaleDto[]>('/vicidial-form/sales', { params });
  return data;
}

export async function getActiveAltrxAgents(): Promise<ActiveAltrxAgentDto[]> {
  const { data } = await vicidialClient.get<ActiveAltrxAgentDto[]>('/vicidial-form/active-agents');
  return data;
}

export async function getAgentByUser(user: string): Promise<ActiveAltrxAgentDto> {
  const encoded = encodeURIComponent(user);
  const { data } = await vicidialClient.get<ActiveAltrxAgentDto>(`/vicidial-form/agent/${encoded}`);
  return data;
}

export async function getVicidialSalesSummary(from?: string, to?: string, limit = 500): Promise<SalesSummary> {
  const params: Record<string, string | number> = { limit };
  if (from) params.from = from;
  if (to) params.to = to;
  const { data } = await vicidialClient.get<SalesSummary>('/vicidial-form/sales/summary', { params });
  return data;
}

export async function deleteVicidialSale(id: number, editorEmail: string): Promise<{ id: number; message: string }> {
  const { data } = await vicidialClient.delete<{ id: number; message: string }>(`/vicidial-form/sale/${id}`, {
    params: { editorEmail },
  });
  return data;
}

export interface VicidialSaleUpdatePayload {
  editorEmail?: string;
  leadId?: number;
  saleDate?: string;
  clientPhone?: string;
  clientName?: string;
  clientEmail?: string;
  bundle?: string;
  amount?: number;
}

export async function updateVicidialSale(id: number, payload: VicidialSaleUpdatePayload): Promise<{ id: number; message: string }> {
  const { data } = await vicidialClient.patch<{ id: number; message: string }>(`/vicidial-form/sale/${id}`, payload);
  return data;
}

export async function getVicidialLeadById(leadId: number): Promise<VicidialLeadDto> {
  const { data } = await vicidialClient.get<VicidialLeadDto>(`/vicidial-form/lead/${leadId}`);
  return data;
}

export async function exportVicidialSalesExcel(
  sales: VicidialSaleDto[],
  filename: string,
  meta?: { from?: string; to?: string; period?: string; agentFilter?: string },
): Promise<void> {
  const ExcelJS = (await import('exceljs')).default;
  const workbook = new ExcelJS.Workbook();
  workbook.creator = 'ALTRX CRM';
  workbook.created = new Date();

  const sheet = workbook.addWorksheet('Vicidial Form Sales', {
    views: [{ state: 'frozen', ySplit: 1 }],
  });

  sheet.columns = [
    { header: 'Date', key: 'date', width: 22 },
    { header: 'Agent', key: 'agent', width: 24 },
    { header: 'Client', key: 'client', width: 28 },
    { header: 'Phone', key: 'phone', width: 18 },
    { header: 'Email', key: 'email', width: 30 },
    { header: 'Bundle', key: 'bundle', width: 18 },
    { header: 'Amount (USD)', key: 'amount', width: 16, style: { numFmt: '"$"#,##0.00' } },
  ];

  const headerRow = sheet.getRow(1);
  headerRow.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  headerRow.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1F2937' } };
  headerRow.alignment = { vertical: 'middle', horizontal: 'left' };
  headerRow.height = 22;

  sales.forEach((s) => {
    sheet.addRow({
      date: s.saleDate,
      agent: s.salesRep,
      client: s.clientName,
      phone: s.clientPhone,
      email: s.clientEmail,
      bundle: s.bundle,
      amount: Number(s.amount),
    });
  });

  const totalRow = sheet.addRow({
    date: '',
    agent: '',
    client: '',
    phone: '',
    email: '',
    bundle: 'TOTAL',
    amount: sales.reduce((sum, s) => sum + Number(s.amount), 0),
  });
  totalRow.font = { bold: true };
  totalRow.getCell('bundle').alignment = { horizontal: 'right' };
  totalRow.getCell('amount').numFmt = '"$"#,##0.00';

  if (meta) {
    const infoSheet = workbook.addWorksheet('Info');
    infoSheet.columns = [{ header: 'Field', key: 'field', width: 16 }, { header: 'Value', key: 'value', width: 40 }];
    const infoHeader = infoSheet.getRow(1);
    infoHeader.font = { bold: true };
    infoSheet.addRow({ field: 'Generated at', value: new Date().toISOString() });
    if (meta.period) infoSheet.addRow({ field: 'Period', value: meta.period });
    if (meta.from) infoSheet.addRow({ field: 'From', value: meta.from });
    if (meta.to) infoSheet.addRow({ field: 'To', value: meta.to });
    if (meta.agentFilter && meta.agentFilter !== 'all') infoSheet.addRow({ field: 'Agent filter', value: meta.agentFilter });
    infoSheet.addRow({ field: 'Total sales', value: sales.length });
    infoSheet.addRow({ field: 'Total amount', value: sales.reduce((sum, s) => sum + Number(s.amount), 0) });
  }

  const buffer = await workbook.xlsx.writeBuffer();
  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
