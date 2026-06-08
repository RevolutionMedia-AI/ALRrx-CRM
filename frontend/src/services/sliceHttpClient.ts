import axios from 'axios';

export const sliceClient = axios.create({ baseURL: '/api/slice', timeout: 15000 });

sliceClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('slice_token');
  if (token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

sliceClient.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401) {
      const url: string = err.config?.url ?? '';
      if (!url.startsWith('/auth/')) {
        localStorage.removeItem('slice_token');
        delete sliceClient.defaults.headers.common['Authorization'];
        window.location.href = '/slice/login';
      }
    }
    return Promise.reject(err);
  }
);

export function setSliceAuthToken(token: string | null) {
  if (token) {
    sliceClient.defaults.headers.common['Authorization'] = `Bearer ${token}`;
  } else {
    delete sliceClient.defaults.headers.common['Authorization'];
  }
}
