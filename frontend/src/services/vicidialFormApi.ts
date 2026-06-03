import axios from 'axios';
import type { VicidialSaleRequest, VicidialSaleDto, VicidialFormIdentity } from '../types';
import { client } from './httpClient';

export const vicidialClient = axios.create({ baseURL: '/api', timeout: 30000 });

vicidialClient.interceptors.request.use((config) => {
  const token = sessionStorage.getItem('alrrx_form_token');
  if (token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

vicidialClient.interceptors.response.use(
  (res) => res,
  (err) => Promise.reject(err),
);

export async function authenticateVicidialFormToken(token: string): Promise<VicidialFormIdentity> {
  const { data } = await vicidialClient.post<VicidialFormIdentity>('/vicidial-form/auth', { token });
  return data;
}

export async function getCurrentVicidialFormIdentity(): Promise<VicidialFormIdentity> {
  const { data } = await vicidialClient.get<VicidialFormIdentity>('/vicidial-form/me');
  return data;
}

export async function submitVicidialSale(payload: VicidialSaleRequest): Promise<{ id: number; message: string }> {
  const { data } = await vicidialClient.post<{ id: number; message: string }>('/vicidial-form/sale', payload);
  return data;
}

export async function listVicidialSales(from?: string, to?: string, limit = 50): Promise<VicidialSaleDto[]> {
  const params: Record<string, string | number> = { limit };
  if (from) params.from = from;
  if (to) params.to = to;
  const { data } = await vicidialClient.get<VicidialSaleDto[]>('/vicidial-form/sales', { params });
  return data;
}

export async function listAllVicidialSalesAdmin(from?: string, to?: string, limit = 500): Promise<VicidialSaleDto[]> {
  const params: Record<string, string | number> = { limit };
  if (from) params.from = from;
  if (to) params.to = to;
  const { data } = await client.get<VicidialSaleDto[]>('/vicidial-form/admin/sales', { params });
  return data;
}
