import axios from 'axios';
import type { VicidialSaleRequest, VicidialSaleDto, ActiveAltrxAgentDto } from '../types';

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

export interface VicidialSaleUpdatePayload {
  editorEmail?: string;
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
