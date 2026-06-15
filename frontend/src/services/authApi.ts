export type UserStatus = 'Pending' | 'Active' | 'Rejected' | 'Suspended';

export type PlatformAccess = 'None' | 'Altrx' | 'Slice' | 'Both';

export interface UserInfo {
  id: number;
  email: string;
  fullName: string;
  roleId: number;
  role: string;
  status: UserStatus;
  platformAccess: PlatformAccess;
  isActive: boolean;
  lastLoginAt?: string | null;
  createdAt: string;
  permissions: string[];
  hasAccess: boolean;
}

export interface LoginResponse {
  token: string;
  user: UserInfo;
}

export interface RegisterRequest {
  email: string;
  fullName: string;
  roleId: number;
}

import { client, setAuthToken } from './httpClient';
export { setAuthToken };

export async function googleLogin(accessToken: string): Promise<LoginResponse> {
  const { data } = await client.post<LoginResponse>('/auth/google', { accessToken });
  return data;
}

export async function register(req: RegisterRequest): Promise<UserInfo> {
  const { data } = await client.post<UserInfo>('/auth/register', req);
  return data;
}

export async function getMe(): Promise<UserInfo> {
  const { data } = await client.get<UserInfo>('/auth/me');
  return data;
}

// BUG-20 fix: best-effort sign-out call to the backend. The JWT is
// stateless so the server cannot truly invalidate it, but the endpoint
// logs the logout action and gives the frontend a place to react if the
// backend is unreachable. We swallow network errors: local token cleanup
// must always happen, even if the API call fails.
export async function logoutRequest(): Promise<void> {
  try {
    await client.post('/auth/logout', null, { timeout: 5000 });
  } catch {
    // ignored — local token clear is the source of truth for the client
  }
}

// Refresh-token rotation: ask the backend to mint a new JWT using the
// current valid one. The new token replaces the old one in localStorage;
// the old one remains technically valid until it expires (stateless
// JWT), but the proactive refresh keeps the window small. If the
// backend is unreachable or the user is no longer allowed, the call
// rejects and the caller decides what to do (typically force-logout).
export async function refreshRequest(): Promise<LoginResponse> {
  const { data } = await client.post<LoginResponse>('/auth/refresh', null, { timeout: 10000 });
  return data;
}

export async function getUsers(): Promise<UserInfo[]> {
  const { data } = await client.get<UserInfo[]>('/users');
  return data;
}

export async function updateUser(id: number, req: Partial<RegisterRequest & { isActive: boolean }>): Promise<void> {
  await client.put(`/users/${id}`, req);
}
