import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import {
  sliceGetMe,
  sliceGoogleLogin,
  setSliceAuthToken,
  sliceLogin,
} from '../../services/sliceAuthApi';
import { getGoogleAccessToken, setGoogleAccessToken } from '../../utils/googleTokenStore';
import type { SliceUserInfo } from '../types';

interface SliceAuthContextType {
  user: SliceUserInfo | null;
  token: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  loginWithGoogle: (accessToken: string) => Promise<void>;
  rehydrateWithGoogle: () => Promise<boolean>;
  logout: () => void;
  isAdmin: boolean;
}

const SliceAuthContext = createContext<SliceAuthContextType | null>(null);

const SLICE_TOKEN_KEY = 'slice_token';

export function SliceAuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SliceUserInfo | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const stored = localStorage.getItem(SLICE_TOKEN_KEY);
    if (stored) {
      setSliceAuthToken(stored);
      setToken(stored);
      sliceGetMe()
        .then((u) => setUser(u))
        .catch((err: unknown) => {
          const status = err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;
          if (status === 401) {
            localStorage.removeItem(SLICE_TOKEN_KEY);
            setSliceAuthToken(null);
            setToken(null);
          } else {
            console.warn('slice getMe failed (transient), keeping token:', err);
          }
        })
        .finally(() => setLoading(false));
      return;
    }
    const googleToken = getGoogleAccessToken();
    if (googleToken) {
      sliceGoogleLogin(googleToken)
        .then((res) => {
          localStorage.setItem(SLICE_TOKEN_KEY, res.token);
          setSliceAuthToken(res.token);
          setToken(res.token);
          setUser({
            id: '',
            email: res.email,
            fullName: res.fullName,
            role: res.role,
            createdAt: new Date().toISOString(),
          });
        })
        .catch((err: unknown) => {
          const status = err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;
          if (status === 401 || status === 403) {
            localStorage.removeItem(SLICE_TOKEN_KEY);
            setSliceAuthToken(null);
            setToken(null);
          } else {
            console.warn('slice google rehydrate failed (transient):', err);
          }
        })
        .finally(() => setLoading(false));
    } else {
      setLoading(false);
    }
  }, []);

  const login = async (email: string, password: string) => {
    const res = await sliceLogin(email, password);
    localStorage.setItem(SLICE_TOKEN_KEY, res.token);
    setSliceAuthToken(res.token);
    setToken(res.token);
    setUser({
      id: '',
      email: res.email,
      fullName: res.fullName,
      role: res.role,
      createdAt: new Date().toISOString(),
    });
  };

  const loginWithGoogle = async (accessToken: string) => {
    setGoogleAccessToken(accessToken);
    const res = await sliceGoogleLogin(accessToken);
    localStorage.setItem(SLICE_TOKEN_KEY, res.token);
    setSliceAuthToken(res.token);
    setToken(res.token);
    setUser({
      id: '',
      email: res.email,
      fullName: res.fullName,
      role: res.role,
      createdAt: new Date().toISOString(),
    });
  };

  const rehydrateWithGoogle = useCallback(async (): Promise<boolean> => {
    const googleToken = getGoogleAccessToken();
    if (!googleToken) return false;
    try {
      const res = await sliceGoogleLogin(googleToken);
      localStorage.setItem(SLICE_TOKEN_KEY, res.token);
      setSliceAuthToken(res.token);
      setToken(res.token);
      setUser({
        id: '',
        email: res.email,
        fullName: res.fullName,
        role: res.role,
        createdAt: new Date().toISOString(),
      });
      return true;
    } catch (err) {
      console.warn('[SliceAuth] rehydrateWithGoogle failed:', err);
      return false;
    }
  }, []);

  const logout = () => {
    localStorage.removeItem(SLICE_TOKEN_KEY);
    setSliceAuthToken(null);
    setToken(null);
    setUser(null);
  };

  const isAdmin = user?.role === 'Admin';

  return (
    <SliceAuthContext.Provider value={{ user, token, loading, login, loginWithGoogle, rehydrateWithGoogle, logout, isAdmin }}>
      {children}
    </SliceAuthContext.Provider>
  );
}

export function useSliceAuth() {
  const ctx = useContext(SliceAuthContext);
  if (!ctx) throw new Error('useSliceAuth must be used within SliceAuthProvider');
  return ctx;
}
