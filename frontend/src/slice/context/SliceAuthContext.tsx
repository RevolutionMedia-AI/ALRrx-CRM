import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import {
  sliceGetMe,
  sliceGoogleLogin,
  setSliceAuthToken,
  sliceLogin,
} from '../../services/sliceAuthApi';
import { getGoogleAccessToken, setGoogleAccessToken } from '../../utils/googleTokenStore';
import { readSharedToken, writeSharedToken, clearSharedToken } from '../../utils/sharedToken';
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

export function SliceAuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SliceUserInfo | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // We share the auth token with the alrrx AuthContext (see utils/sharedToken).
    // If the user is already signed in on the alrrx side, this context picks
    // up the same token without forcing a re-login.
    const stored = readSharedToken();
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
            // Token is invalid (revoked, expired, signature mismatch) — full sign-out.
            clearSharedToken();
            setSliceAuthToken(null);
            setToken(null);
          } else if (status && status >= 500) {
            // BUG-19 fix: a 5xx from the slice backend is a server problem,
            // not the user's token. But keeping a half-authenticated slice
            // state would block the SliceProtectedRoute rehydration with no
            // way for the user to recover (no UI feedback). Clear slice
            // state so the rehydration can be retried, but keep the shared
            // token so the altrx side still works.
            console.warn('slice getMe 5xx, clearing slice state:', err);
            setSliceAuthToken(null);
            setToken(null);
            setUser(null);
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
          writeSharedToken(res.token);
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
          if (status === 401) {
            // BUG-27 fix: 401 means the Google token is no longer valid
            // (revoked by Google, user signed out, etc). Clear it so the
            // next attempt forces a fresh Google sign-in. Do NOT clear on
            // 403 (forbidden at the slice side) or 5xx (server error) —
            // those are recoverable on retry.
            setGoogleAccessToken(null);
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
    writeSharedToken(res.token);
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
    writeSharedToken(res.token);
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
      writeSharedToken(res.token);
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
    } catch (err: unknown) {
      // BUG-27 fix: only nuke the Google access token on 401 (truly
      // invalid). 5xx (server down) and network errors are transient —
      // keeping the token lets the next rehydration attempt reuse it.
      const status = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { status?: number } }).response?.status
        : undefined;
      if (status === 401) setGoogleAccessToken(null);
      console.warn('[SliceAuth] rehydrateWithGoogle failed:', err);
      return false;
    }
  }, []);

  const logout = () => {
    // Clear the shared token so the alrrx AuthContext also signs out.
    clearSharedToken();
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
