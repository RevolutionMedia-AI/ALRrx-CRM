import axios from 'axios';
import type { VicidialAuthResponse, VicidialSaleRequest, VicidialSaleDto } from '../types';

const VICIDIAL_TOKEN_KEY = 'vicidial_form_token';
const VICIDIAL_TOKEN_EXPIRES_KEY = 'vicidial_form_token_expires';

export const vicidialClient = axios.create({ baseURL: '/api', timeout: 30000 });

vicidialClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(VICIDIAL_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

vicidialClient.interceptors.response.use(
  (res) => res,
  (err) => Promise.reject(err),
);

export function getStoredVicidialToken(): { token: string; expiresAt: string } | null {
  const token = localStorage.getItem(VICIDIAL_TOKEN_KEY);
  const expiresAt = localStorage.getItem(VICIDIAL_TOKEN_EXPIRES_KEY);
  if (!token || !expiresAt) return null;
  if (new Date(expiresAt).getTime() <= Date.now()) {
    clearVicidialToken();
    return null;
  }
  return { token, expiresAt };
}

export function setVicidialToken(token: string, expiresAt: string): void {
  localStorage.setItem(VICIDIAL_TOKEN_KEY, token);
  localStorage.setItem(VICIDIAL_TOKEN_EXPIRES_KEY, expiresAt);
}

export function clearVicidialToken(): void {
  localStorage.removeItem(VICIDIAL_TOKEN_KEY);
  localStorage.removeItem(VICIDIAL_TOKEN_EXPIRES_KEY);
}

export async function authenticateVicidialForm(key: string): Promise<VicidialAuthResponse> {
  const { data } = await vicidialClient.post<VicidialAuthResponse>('/vicidial-form/auth', { key });
  return data;
}

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
