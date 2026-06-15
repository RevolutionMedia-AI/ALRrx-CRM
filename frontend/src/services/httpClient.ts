import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { readSharedToken, clearSharedToken, writeSharedToken } from '../utils/sharedToken';
import { refreshRequest } from './authApi';
import { deviceFingerprint } from '../utils/deviceFingerprint';

export const client = axios.create({ baseURL: '/api', timeout: 30000 });

export const AUTH_FORBIDDEN_EVENT = 'auth:forbidden';
export const AUTH_UNAUTHORIZED_EVENT = 'auth:unauthorized';

// Refresh-token rotation: when a request 401s with a non-revoked token,
// the client calls POST /auth/refresh to get a new one and retries the
// original request. Only one refresh is in flight at a time even if many
// parallel requests 401 simultaneously. Exported so the AuthContext can
// use the same mutex for proactive refreshes (before the 401).
let refreshInflight: Promise<string | null> | null = null;
export async function refreshOnce(): Promise<string | null> {
  if (!refreshInflight) {
    refreshInflight = refreshRequest()
      .then((res) => {
        writeSharedToken(res.token);
        client.defaults.headers.common['Authorization'] = `Bearer ${res.token}`;
        return res.token;
      })
      .catch(() => null)
      .finally(() => {
        // Allow the next 401 to start a new refresh attempt.
        setTimeout(() => { refreshInflight = null; }, 0);
      });
  }
  return refreshInflight;
}

client.interceptors.request.use((config) => {
  const token = readSharedToken();
  if (token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${token}`;
  }
  // Per-device rate-limit identifier. The backend uses this as a
  // secondary key for the 'auth' policy so a single attacker on one
  // device can't exhaust the per-IP bucket and lock out the other 9
  // users behind the same corporate NAT.
  config.headers = config.headers ?? {};
  (config.headers as Record<string, string>)['X-Device-Fingerprint'] = deviceFingerprint();
  return config;
});

// Track which requests have already been retried after a refresh, to
// avoid infinite loops if the backend is broken.
interface RetryableRequest extends InternalAxiosRequestConfig {
  _retryAfterRefresh?: boolean;
}

client.interceptors.response.use(
  (res) => res,
  async (err: AxiosError) => {
    const status = err?.response?.status;
    const config = err?.config as RetryableRequest | undefined;
    const url: string = config?.url ?? '';
    const code = (err?.response?.data as { code?: string } | undefined)?.code;
    const isAnonymousEndpoint =
      url.startsWith('/auth/google') ||
      url.startsWith('/auth/register') ||
      url.startsWith('/auth/dev-login') ||
      url.startsWith('/auth/refresh');

    if (status === 401) {
      if (code === 'TOKEN_REVOKED') {
        // BUG-008: Admin revoked this session. Force re-login.
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
        window.dispatchEvent(new CustomEvent(AUTH_FORBIDDEN_EVENT, { detail: { code: 'TOKEN_REVOKED', error: (err.response?.data as { error?: string } | undefined)?.error } }));
        return Promise.reject(err);
      }

      // Try refresh-then-retry once. The conditions:
      //   1. We have a token to refresh (otherwise 401 is just a stale
      //      session — clear it and force re-login).
      //   2. We haven't already retried this request.
      //   3. The endpoint requires auth (anonymous endpoints like login
      //      can 401 without it being a session problem).
      const hasToken = !!readSharedToken();
      if (hasToken && !config?._retryAfterRefresh && !isAnonymousEndpoint) {
        const newToken = await refreshOnce();
        if (newToken) {
          // Mark this request as already-retried and re-issue it with
          // the fresh token. Other concurrent requests that are still
          // in-flight will hit the same refresh promise via refreshOnce
          // and pick up the new token from the default header.
          if (config) {
            config._retryAfterRefresh = true;
            config.headers = config.headers ?? {};
            (config.headers as Record<string, string>).Authorization = `Bearer ${newToken}`;
          }
          return client.request(config!);
        }
        // Refresh failed — fall through to the standard 401 cleanup.
      }

      if (!isAnonymousEndpoint) {
        // BUG-21/22 fix: clear local state and notify the AuthContext
        // so the user is redirected to /login.
        clearSharedToken();
        delete client.defaults.headers.common['Authorization'];
        window.dispatchEvent(new CustomEvent(AUTH_UNAUTHORIZED_EVENT, {
          detail: { code: code ?? 'TOKEN_INVALID', error: (err.response?.data as { error?: string } | undefined)?.error, url },
        }));
      }
    }

    if (status === 403) {
      const data = err?.response?.data as { code?: string; error?: string } | undefined;
      const c = data?.code;
      if (c && ['USER_PENDING', 'USER_SUSPENDED', 'USER_REJECTED', 'USER_LOCKED'].includes(c)) {
        window.dispatchEvent(new CustomEvent(AUTH_FORBIDDEN_EVENT, { detail: { code: c, error: data?.error } }));
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
