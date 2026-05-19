import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { login as apiLogin, setAuthToken, type UserInfo } from '../services/authApi';

const FAKE_USER: UserInfo = {
  id: 'dev-bypass',
  email: 'kevin.escalante@revolutionmedia.ai',
  name: 'Kevin Escalante',
  role: 'Admin',
};

interface AuthContextType {
  user: UserInfo | null;
  token: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  isAdmin: boolean;
  canEdit: boolean;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(FAKE_USER);
  const [token, setToken] = useState<string | null>('dev-bypass-token');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (token) {
      setAuthToken(token);
    }
  }, []);

  const login = async (email: string, password: string) => {
    try {
      const res = await apiLogin({ email, password });
      localStorage.setItem('alrrx_token', res.token);
      setAuthToken(res.token);
      setToken(res.token);
      setUser(res.user);
    } catch {
      // fallback to dev bypass
    }
  };

  const logout = () => {
    localStorage.removeItem('alrrx_token');
    setAuthToken(null);
    setToken(null);
    setUser(FAKE_USER);
  };

  const isAdmin = user?.role === 'Admin';
  const canEdit = user?.role === 'Admin' || user?.role === 'Supervisor';

  return (
    <AuthContext.Provider value={{ user, token, loading, login, logout, isAdmin, canEdit }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
