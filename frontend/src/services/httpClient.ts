import axios from 'axios';
import { readSharedToken, clearSharedToken } from '../utils/sharedToken';

export const client = axios.create({ baseURL: '/api', timeout: 30000 });

export const AUTH_FORBIDDEN_EVENT = 'auth:forbidden';
export const AUTH_UNAUTHORIZED_EVENT = 'auth:unauthorized';

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
      // Login-style endpoints are allowed to return 401 (bad credentials)
      // without the token being touched. Treat them as anonymous endpoints.
      const isAnonymousEndpoint =
        url.startsWith('/auth/login') ||
        url.startsWith('/auth/google') ||
        url.startsWith('/auth/register') ||
        url.startsWith('/auth/dev-login');

      if (code === 'TOKEN_REVOKED') {
        // BUG-008: Admin revoked this session. Force re-login.
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
        window.dispatchEvent(new CustomEvent(AUTH_FORBIDDEN_EVENT, { detail: { code: 'TOKEN_REVOKED', error: err.response.data?.error } }));
        return Promise.reject(err);
      }

      if (!isAnonymousEndpoint) {
        // BUG-21/22 fix: a 401 on ANY endpoint that should be authenticated
        // (including /auth/me, which previously slipped through the
        // '/auth/' guard) means the token is no longer good. Clear it
        // locally and notify the AuthContext so it can clear the user
        // state and navigate to /login. Without the event, the React user
        // state stays populated while the token is gone — the user would
        // see their own name and admin links in the navbar but every
        // subsequent API call would also 401.
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
        window.dispatchEvent(new CustomEvent(AUTH_UNAUTHORIZED_EVENT, {
          detail: { code: code ?? 'TOKEN_INVALID', error: err.response.data?.error, url },
        }));
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
