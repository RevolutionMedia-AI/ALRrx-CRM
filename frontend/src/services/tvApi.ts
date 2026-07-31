import axios from 'axios';

const http = axios.create({
  baseURL: '/api',
  timeout: 60000,
});

http.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export interface TvAgentSales {
  salesRep: string;
  count: number;
  amount: number;
}

export const tvApi = {
  getSalesByAgentToday: async (): Promise<TvAgentSales[]> => {
    const { data } = await http.get<TvAgentSales[]>('/tv/sales-by-agent');
    return data;
  },
};