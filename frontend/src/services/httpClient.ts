import axios from 'axios';

export const client = axios.create({ baseURL: '/api', timeout: 15000 });

client.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401) {
      const url: string = err.config?.url ?? '';
      if (!url.startsWith('/auth/')) {
        localStorage.removeItem('alrrx_token');
        delete client.defaults.headers.common['Authorization'];
        window.location.href = '/login';
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
