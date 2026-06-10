import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { login as apiLogin, googleLogin as apiGoogleLogin, getMe, setAuthToken, type UserInfo } from '../services/authApi';
import { getGoogleAccessToken, setGoogleAccessToken } from '../utils/googleTokenStore';
import { SHARED_TOKEN_KEY, readSharedToken, writeSharedToken, clearSharedToken } from '../utils/sharedToken';

// ─── DEV BYPASS ────────────────────────────────────────────────────────────────
// Set VITE_DEV_BYPASS=true in frontend/.env.local to skip login locally.
// The backend emits a real JWT via POST /api/auth/dev-login (Development only).
const DEV_BYPASS = import.meta.env.VITE_DEV_BYPASS === 'true';
// ───────────────────────────────────────────────────────────────────────────────

interface AuthContextType {
  user: UserInfo | null;
  token: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  loginWithGoogle: (credential: string) => Promise<void>;
  logout: () => void;
  isAdmin: boolean;
  canEdit: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // ── Dev bypass: auto-login without credentials ──
    if (DEV_BYPASS) {
      fetch('/api/auth/dev-login', { method: 'POST' })
        .then((r) => r.json())
        .then((data) => {
          if (data?.token) {
            writeSharedToken(data.token);
            setAuthToken(data.token);
            setToken(data.token);
            setUser(data.user);
          }
        })
        .catch(() => { /* backend no disponible, queda sin sesión */ })
        .finally(() => setLoading(false));
      return;
    }
    // ── Flujo normal ──
    // We use a single shared token key (`auth_token`) that both AuthContext
    // (alrrx) and SliceAuthContext (slice) read/write. That way a login on
    // either side is enough to access both backends (the backends now share
    // the same JWT key/issuer/audience). For backwards compatibility we also
    // fall back to the old per-context keys if a user still has them around.
    const stored = readSharedToken();
    if (stored) {
      setAuthToken(stored);
      setToken(stored);
      getMe().then((u) => { setUser(u); setLoading(false); }).catch((err: unknown) => {
        const status = err && typeof err === 'object' && 'response' in err
          ? (err as { response?: { status?: number } }).response?.status
          : undefined;
        if (status === 401) {
          clearSharedToken();
          setAuthToken(null);
          setToken(null);
        } else {
          console.warn('getMe failed (transient), keeping token:', err);
        }
        setLoading(false);
      });
      return;
    }
    const googleToken = getGoogleAccessToken();
    if (googleToken) {
      apiGoogleLogin(googleToken)
        .then((res) => {
          writeSharedToken(res.token);
          setAuthToken(res.token);
          setToken(res.token);
          setUser(res.user);
        })
        .catch((err: unknown) => {
          const status = err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;
          if (status === 401) {
            setGoogleAccessToken(null);
          } else {
            console.warn('Google rehydrate failed (transient), keeping token:', err);
          }
        })
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, []);

  const login = async (email: string, password: string) => {
    const res = await apiLogin({ email, password });
    writeSharedToken(res.token);
    setAuthToken(res.token);
    setToken(res.token);
    setUser(res.user);
  };

  const loginWithGoogle = async (credential: string) => {
    setGoogleAccessToken(credential);
    const res = await apiGoogleLogin(credential);
    writeSharedToken(res.token);
    setAuthToken(res.token);
    setToken(res.token);
    setUser(res.user);
  };

  const logout = () => {
    clearSharedToken();
    setGoogleAccessToken(null);
    setAuthToken(null);
    setToken(null);
    setUser(null);
  };

  const isAdmin = user?.role === 'Admin';
  const canEdit = user?.role === 'Admin' || user?.role === 'Supervisor';

  return (
    <AuthContext.Provider value={{ user, token, loading, login, loginWithGoogle, logout, isAdmin, canEdit }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
