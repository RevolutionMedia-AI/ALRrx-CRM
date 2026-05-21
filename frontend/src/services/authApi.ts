export interface LoginRequest {
  email: string;
  password: string;
}

export interface UserInfo {
  id: number;
  email: string;
  fullName: string;
  role: string;
  isActive: boolean;
  createdAt: string;
}

export interface LoginResponse {
  token: string;
  user: UserInfo;
}

export interface RegisterRequest {
  email: string;
  password: string;
  fullName: string;
  role: string;
}

import { client, setAuthToken } from './httpClient';
export { setAuthToken };

export async function login(req: LoginRequest): Promise<LoginResponse> {
  const { data } = await client.post<LoginResponse>('/auth/login', req);
  return data;
}

export async function googleLogin(credential: string): Promise<LoginResponse> {
  const { data } = await client.post<LoginResponse>('/auth/google', { credential });
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

export async function getUsers(): Promise<UserInfo[]> {
  const { data } = await client.get<UserInfo[]>('/users');
  return data;
}

export async function updateUser(id: number, req: Partial<RegisterRequest & { isActive: boolean }>): Promise<void> {
  await client.put(`/users/${id}`, req);
}
