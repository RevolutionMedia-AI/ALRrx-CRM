import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { login as apiLogin, getMe, setAuthToken, type UserInfo } from '../services/authApi';

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
  const [user, setUser] = useState<UserInfo | null>(null);
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('alrrx_token'));
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (token) {
      setAuthToken(token);
      getMe()
        .then(setUser)
        .catch(() => {
          localStorage.removeItem('alrrx_token');
          setAuthToken(null);
          setToken(null);
        })
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, []);

  const login = async (email: string, password: string) => {
    const res = await apiLogin({ email, password });
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
