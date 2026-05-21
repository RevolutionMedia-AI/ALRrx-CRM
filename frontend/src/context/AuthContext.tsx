import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { login as apiLogin, googleLogin as apiGoogleLogin, getMe, setAuthToken, type UserInfo } from '../services/authApi';

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
    const stored = localStorage.getItem('alrrx_token');
    if (stored) {
      setAuthToken(stored);
      setToken(stored);
      fetchMe(stored);
    } else {
      setLoading(false);
    }
  }, []);

  const fetchMe = async (jwt: string) => {
    try {
      setAuthToken(jwt);
      const u = await getMe();
      setUser(u);
    } catch {
      localStorage.removeItem('alrrx_token');
      setAuthToken(null);
      setToken(null);
    } finally {
      setLoading(false);
    }
  };

  const login = async (email: string, password: string) => {
    const res = await apiLogin({ email, password });
    localStorage.setItem('alrrx_token', res.token);
    setAuthToken(res.token);
    setToken(res.token);
    setUser(res.user);
  };

  const loginWithGoogle = async (credential: string) => {
    const res = await apiGoogleLogin(credential);
    localStorage.setItem('alrrx_token', res.token);
    setAuthToken(res.token);
    setToken(res.token);
    setUser(res.user);
  };

  const logout = () => {
    localStorage.removeItem('alrrx_token');
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
