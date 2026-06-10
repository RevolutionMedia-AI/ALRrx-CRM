import axios from 'axios';
import { readSharedToken, clearSharedToken } from '../utils/sharedToken';

export const client = axios.create({ baseURL: '/api', timeout: 15000 });

client.interceptors.request.use((config) => {
  // Read from the shared token store so slice sessions are also visible here.
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
      if (!url.startsWith('/auth/')) {
        // Don't hard-redirect: AuthContext owns the navigation decision so a
        // transient 401 on one backend doesn't kick the user out of the other.
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
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
