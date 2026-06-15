import axios from 'axios';
import { readSharedToken, clearSharedToken } from '../utils/sharedToken';

export const client = axios.create({ baseURL: '/api', timeout: 30000 });

export const AUTH_FORBIDDEN_EVENT = 'auth:forbidden';

client.interceptors.request.use((config) => {
  const token = readSharedToken();
  if (token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

client.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401) {
      const url: string = err.config?.url ?? '';
      const code = err?.response?.data?.code;
      if (code === 'TOKEN_REVOKED') {
        // BUG-008: Admin revoked this session. Force re-login.
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
        window.dispatchEvent(new CustomEvent(AUTH_FORBIDDEN_EVENT, { detail: { code: 'TOKEN_REVOKED', error: err.response.data?.error } }));
        return Promise.reject(err);
      }
      if (!url.startsWith('/auth/')) {
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
      }
    }
    if (err?.response?.status === 403) {
      const data = err?.response?.data;
      const code = data?.code;
      if (code && ['USER_PENDING', 'USER_SUSPENDED', 'USER_REJECTED', 'USER_LOCKED'].includes(code)) {
        window.dispatchEvent(new CustomEvent(AUTH_FORBIDDEN_EVENT, { detail: { code, error: data?.error } }));
      }
    }
    return Promise.reject(err);
  }
);

export function setAuthToken(token: string | null) {
  if (token) {
    client.defaults.headers.common['Authorization'] = `Bearer ${token}`;
  } else {
    delete client.defaults.headers.common['Authorization'];
  }
}
