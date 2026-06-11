import axios from 'axios';

const http = axios.create({
  baseURL: '/api',
  timeout: 30000,
});

http.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export interface TwilioSummary {
  totalCost: number;
  totalCalls: number;
  totalMinutes: number;
  inboundCost: number;
  outboundCost: number;
  recordingCost: number;
  inboundCalls: number;
  outboundCalls: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
  lastUpdated: string;
}

export interface TwilioCall {
  sid: string;
  from: string;
  to: string;
  status: string;
  direction: 'inbound' | 'outbound';
  durationSeconds: number;
  cost: number;
  currency: string;
  startTime: string;
  endTime: string | null;
  hasRecording: boolean;
}

export interface TwilioDailyCost {
  date: string;
  cost: number;
  callCount: number;
  minutes: number;
}

export const twilioApi = {
  getSummary: async (
    period: 'today' | 'week' | 'month' | 'custom' = 'today',
    startDate?: string,
    endDate?: string
  ): Promise<TwilioSummary> => {
    const { data } = await http.get('/twilio/summary', {
      params: { period, startDate, endDate },
    });
    return data;
  },

  getRecentCalls: async (limit = 50): Promise<TwilioCall[]> => {
    const { data } = await http.get('/twilio/calls/recent', { params: { limit } });
    return data;
  },

  getDailyCosts: async (days = 30): Promise<TwilioDailyCost[]> => {
    const { data } = await http.get('/twilio/costs/daily', { params: { days } });
    return data;
  },
};
