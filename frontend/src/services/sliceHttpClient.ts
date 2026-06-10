import axios from 'axios';
import { readSharedToken, clearSharedToken } from '../utils/sharedToken';

export const sliceClient = axios.create({ baseURL: '/api/slice', timeout: 15000 });

sliceClient.interceptors.request.use((config) => {
  // Read from the shared token store so alrrx sessions are also visible here.
  const token = readSharedToken();
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
      // Auth endpoints (login/google/dev-login) returning 401 is a normal
      // failure (bad password). Anything else means the token is invalid;
      // we clear it from localStorage so the next render shows the login
      // page, but we DON'T hard-redirect here — the AuthContext owns the
      // navigation decision so a transient 401 on one backend doesn't kick
      // the user out of the other.
      if (!url.startsWith('/auth/')) {
        clearSharedToken();
        delete sliceClient.defaults.headers.common['Authorization'];
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
