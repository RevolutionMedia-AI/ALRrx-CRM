import { createContext, useContext, useState, useEffect, useCallback, useRef, type ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { login as apiLogin, googleLogin as apiGoogleLogin, getMe, setAuthToken, logoutRequest, refreshRequest, type UserInfo, type UserStatus, type LoginResponse } from '../services/authApi';
import { getGoogleAccessToken, setGoogleAccessToken } from '../utils/googleTokenStore';
import { SHARED_TOKEN_KEY, readSharedToken, writeSharedToken, clearSharedToken, getJwtExp, msUntilExpiry, shouldRefreshToken } from '../utils/sharedToken';
import { AUTH_FORBIDDEN_EVENT, AUTH_UNAUTHORIZED_EVENT, refreshOnce } from '../services/httpClient';
import { resolveAccess, ROUTES } from '../utils/accessControl';

const DEV_BYPASS = import.meta.env.VITE_DEV_BYPASS === 'true';

interface AuthContextType {
  user: UserInfo | null;
  token: string | null;
  loading: boolean;
  authUnavailable: boolean;
  login: (email: string, password: string) => Promise<void>;
  loginWithGoogle: (credential: string) => Promise<void>;
  logout: () => void;
  refresh: () => Promise<void>;
  isAdmin: boolean;
  canEdit: boolean;
  isPending: boolean;
  isSuspended: boolean;
  isRejected: boolean;
  has: (permission: string) => boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

function routeForStatus(status: UserStatus | undefined): string {
  if (status === 'Pending') return '/pending-approval';
  if (status === 'Suspended' || status === 'Rejected') return '/access-denied';
  return '/';
}

function routeForActiveUser(user: UserInfo): string {
  // BUG-1 fix: Active users with Slice or Both access must NOT be sent to '/'
  // (ALTRX dashboard). Respect their platformAccess to land them on the
  // correct platform, and send dual-platform users to the picker.
  const { group, redirectTo } = resolveAccess(user.platformAccess);
  if (group === 'both') return '/select-platform';
  if (group === 'slice') return ROUTES.slice;
  if (group === 'altrx') return ROUTES.altrx;
  return '/';
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  // BUG-26 fix: a transient "service unavailable" banner shown when the
  // user DB is down. The token is kept so a retry can succeed without
  // re-authentication, and the user can keep navigating cached pages
  // while the backend recovers.
  const [authUnavailable, setAuthUnavailable] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  // BUG-5 fix: useRef for the current pathname so applyUserAndRedirect does NOT
  // get recreated on every navigation. Without this, the bootstrap useEffect
  // re-fires on every route change, causing N concurrent getMe() calls in
  // flight and a race where whichever resolves last wins the user state.
  const pathnameRef = useRef(location.pathname);
  pathnameRef.current = location.pathname;
  const navigateRef = useRef(navigate);
  navigateRef.current = navigate;
  navigateRef.current = navigate;

  const applyUserAndRedirect = useCallback((u: UserInfo | null, tok: string | null) => {
    setUser(u);
    setToken(tok);
    if (u && (u.status === 'Pending' || u.status === 'Suspended' || u.status === 'Rejected')) {
      const target = routeForStatus(u.status);
      if (pathnameRef.current !== target && !pathnameRef.current.startsWith(target)) {
        navigateRef.current(target, { replace: true });
      }
    }
  }, []);

  useEffect(() => {
    if (DEV_BYPASS) {
      fetch('/api/auth/dev-login', { method: 'POST' })
        .then((r) => r.json())
        .then((data) => {
          if (data?.token) {
            writeSharedToken(data.token);
            setAuthToken(data.token);
            setToken(data.token);
            setUser(data.user);
            applyUserAndRedirect(data.user, data.token);
          }
        })
        .catch(() => {})
        .finally(() => setLoading(false));
      return;
    }
    const stored = readSharedToken();
    if (stored) {
      setAuthToken(stored);
      setToken(stored);
      // If the token is already expired or about to expire, refresh
      // first. getMe() would 401 and the interceptor would refresh
      // and retry, but doing it here avoids the extra round-trip
      // and keeps the user from seeing a brief 401 flash.
      const bootstrap = shouldRefreshToken(stored)
        ? ensureFreshToken().catch(() => stored)
        : Promise.resolve(stored);
      bootstrap
        .then((token) => {
          setAuthToken(token);
          setToken(token);
          return getMe();
        })
        .then((u) => {
          // Successful /auth/me clears the BUG-26 banner.
          setAuthUnavailable(false);
          applyUserAndRedirect(u, readSharedToken() ?? stored);
        })
        .catch((err: unknown) => {
          const status = err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;
          if (status === 401) {
            // BUG-22 fix handled by the AUTH_UNAUTHORIZED_EVENT listener,
            // but we also clear here to keep the state consistent if the
            // listener fires after this catch.
            clearSharedToken();
            setAuthToken(null);
            setToken(null);
          } else if (status === 503) {
            // BUG-26 fix: the user DB is unavailable. Don't drop the
            // token (the user is still authenticated, we just can't
            // reach the DB). Show a transient "service unavailable"
            // banner so the user knows what's going on instead of
            // staring at an empty login screen with a valid token in
            // localStorage.
            setAuthUnavailable(true);
          }
        })
        .finally(() => setLoading(false));
      return;
    }
    const googleToken = getGoogleAccessToken();
    if (googleToken) {
      apiGoogleLogin(googleToken)
        .then((res) => {
          writeSharedToken(res.token);
          setAuthToken(res.token);
          setToken(res.token);
          applyUserAndRedirect(res.user, res.token);
        })
        .catch((err: unknown) => {
          const status = err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;
          if (status === 401) setGoogleAccessToken(null);
        })
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, [applyUserAndRedirect]);

  useEffect(() => {
    const forbiddenHandler = (ev: Event) => {
      const detail = (ev as CustomEvent).detail;
      if (detail?.code === 'USER_PENDING') {
        applyUserAndRedirect(user, token);
        navigate('/pending-approval', { replace: true });
      } else if (detail?.code === 'USER_SUSPENDED' || detail?.code === 'USER_REJECTED') {
        // BUG-23 fix: clear the local token and user state on
        // Suspended/Rejected so a new tab can't auto-login with a stale
        // token. The middleware would block the request anyway, but
        // leaving the token in localStorage is a security smell and
        // caused flicker (navbar showed the suspended user as logged in
        // for a frame after navigation).
        clearSharedToken();
        setGoogleAccessToken(null);
        setAuthToken(null);
        setToken(null);
        setUser(null);
        navigate('/access-denied', { replace: true });
      }
    };

    // BUG-21/22 fix: listen for token-expired / token-invalid events from
    // the httpClient interceptor. Clear local state and navigate to
    // /login so the user is not stuck on a page that requires auth.
    const unauthorizedHandler = () => {
      clearSharedToken();
      setGoogleAccessToken(null);
      setAuthToken(null);
      setToken(null);
      setUser(null);
      navigate('/login', { replace: true });
    };

    window.addEventListener(AUTH_FORBIDDEN_EVENT, forbiddenHandler);
    window.addEventListener(AUTH_UNAUTHORIZED_EVENT, unauthorizedHandler);
    return () => {
      window.removeEventListener(AUTH_FORBIDDEN_EVENT, forbiddenHandler);
      window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, unauthorizedHandler);
    };
  }, [applyUserAndRedirect, navigate, user, token]);

  // Proactive JWT refresh: when the current token is within 60s of
  // expiry, ask the backend for a new one. Uses the shared mutex from
  // the httpClient (refreshOnce) so reactive 401 retries and proactive
  // refreshes coalesce into a single backend call. Returns the new
  // token (or the same one if no refresh was needed).
  const ensureFreshToken = useCallback(async (): Promise<string | null> => {
    const current = readSharedToken();
    if (current && !shouldRefreshToken(current)) return current;
    return refreshOnce();
  }, []);

  // Cross-tab sync: the `storage` event fires in OTHER tabs when
  // localStorage changes. Listen for the shared token and the Google
  // access token being added/removed so the current tab mirrors the
  // state of the tab that originated the change. Without this, logging
  // out in tab A leaves tab B with a phantom authenticated session
  // (the React state still shows the user, and the next API call
  // would 401 → dispatch the unauthorized event, but only after a
  // visible flash of authenticated UI).
  useEffect(() => {
    const handler = (e: StorageEvent) => {
      if (e.key === SHARED_TOKEN_KEY) {
        if (e.newValue) {
          // Another tab logged in. Adopt the new token + re-fetch
          // the user so this tab's state matches.
          setAuthToken(e.newValue);
          setToken(e.newValue);
          getMe()
            .then((u) => applyUserAndRedirect(u, e.newValue!))
            .catch(() => { /* ignore — will be handled by interceptor */ });
        } else {
          // Another tab logged out. Mirror the logout here.
          clearSharedToken();
          setAuthToken(null);
          setToken(null);
          setUser(null);
          setGoogleAccessToken(null);
          navigate('/login', { replace: true });
        }
      } else if (e.key === 'google_access_token') {
        // Mirror Google-token lifecycle so the slice rehydrate in
        // this tab uses the same credentials as the originating tab.
        if (e.newValue) {
          setGoogleAccessToken(e.newValue);
        } else {
          setGoogleAccessToken(null);
        }
      }
    };
    window.addEventListener('storage', handler);
    return () => window.removeEventListener('storage', handler);
  }, [applyUserAndRedirect, navigate]);

  const login = async (email: string, password: string) => {
    const res = await apiLogin({ email, password });
    writeSharedToken(res.token);
    setAuthToken(res.token);
    setToken(res.token);
    setUser(res.user);
    // BUG-1 fix: route based on user.status first, then platformAccess for Active.
    const target = res.user.status === 'Active'
      ? routeForActiveUser(res.user)
      : routeForStatus(res.user.status);
    navigate(target, { replace: true });
  };

  const loginWithGoogle = async (credential: string) => {
    setGoogleAccessToken(credential);
    const res = await apiGoogleLogin(credential);
    writeSharedToken(res.token);
    setAuthToken(res.token);
    setToken(res.token);
    setUser(res.user);
    // BUG-1 fix: same platform-aware routing for Google login.
    const target = res.user.status === 'Active'
      ? routeForActiveUser(res.user)
      : routeForStatus(res.user.status);
    navigate(target, { replace: true });
  };

  const logout = () => {
    // BUG-20 fix: notify the backend so the logout is recorded in the
    // audit log. The call is best-effort: if the network is down or the
    // backend is unreachable, we still clear local state and navigate.
    void logoutRequest();
    clearSharedToken();
    setGoogleAccessToken(null);
    setAuthToken(null);
    setToken(null);
    setUser(null);
    navigate('/login', { replace: true });
  };

  const refresh = async () => {
    try {
      const u = await getMe();
      // BUG-24 fix: route based on the freshly-fetched user. If the
      // polling on PendingApprovalPage detects that the admin approved
      // (status flipped to Active) the user is correctly sent to their
      // platform. If the admin suspended the user mid-session, the
      // user is sent to /access-denied instead of staying on a page
      // they can no longer use.
      setAuthUnavailable(false);
      applyUserAndRedirect(u, token);
    } catch (err: unknown) {
      const status = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { status?: number } }).response?.status
        : undefined;
      // BUG-26 fix: surface the 503 so the banner can re-appear after a
      // failed retry, instead of silently swallowing the failure.
      if (status === 503) setAuthUnavailable(true);
    }
  };

  const isAdmin = user?.role === 'Admin';
  const canEdit = user?.role === 'Admin' || user?.role === 'Supervisor';
  const isPending = user?.status === 'Pending';
  const isSuspended = user?.status === 'Suspended';
  const isRejected = user?.status === 'Rejected';
  const has = (perm: string) => !!user?.permissions?.includes(perm);

  return (
    <AuthContext.Provider value={{
      user, token, loading,
      login, loginWithGoogle, logout, refresh,
      isAdmin, canEdit, isPending, isSuspended, isRejected,
      has, authUnavailable,
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
