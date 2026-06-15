import { createContext, useContext, useState, useEffect, useCallback, useRef, type ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { login as apiLogin, googleLogin as apiGoogleLogin, getMe, setAuthToken, type UserInfo, type UserStatus } from '../services/authApi';
import { getGoogleAccessToken, setGoogleAccessToken } from '../utils/googleTokenStore';
import { SHARED_TOKEN_KEY, readSharedToken, writeSharedToken, clearSharedToken } from '../utils/sharedToken';
import { AUTH_FORBIDDEN_EVENT } from '../services/httpClient';
import { resolveAccess, ROUTES } from '../utils/accessControl';

const DEV_BYPASS = import.meta.env.VITE_DEV_BYPASS === 'true';

interface AuthContextType {
  user: UserInfo | null;
  token: string | null;
  loading: boolean;
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
      getMe()
        .then((u) => applyUserAndRedirect(u, stored))
        .catch((err: unknown) => {
          const status = err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;
          if (status === 401) {
            clearSharedToken();
            setAuthToken(null);
            setToken(null);
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
    const handler = (ev: Event) => {
      const detail = (ev as CustomEvent).detail;
      if (detail?.code === 'USER_PENDING') {
        applyUserAndRedirect(user, token);
        navigate('/pending-approval', { replace: true });
      } else if (detail?.code === 'USER_SUSPENDED' || detail?.code === 'USER_REJECTED') {
        applyUserAndRedirect(user, token);
        navigate('/access-denied', { replace: true });
      }
    };
    window.addEventListener(AUTH_FORBIDDEN_EVENT, handler);
    return () => window.removeEventListener(AUTH_FORBIDDEN_EVENT, handler);
  }, [applyUserAndRedirect, navigate, user, token]);

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
      setUser(u);
    } catch {/* ignore */}
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
      has,
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
