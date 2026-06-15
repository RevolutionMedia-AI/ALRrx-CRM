import { client } from './httpClient';
import type { UserInfo, UserStatus } from './authApi';

export interface AdminUserDto extends UserInfo {
  approvedBy: number | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  failedLoginAttempts: number;
  lockedUntil: string | null;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AuditLogEntry {
  id: number;
  userId: number;
  userEmail: string;
  action: string;
  performedBy: number | null;
  performedByEmail: string | null;
  oldValue: string | null;
  newValue: string | null;
  reason: string | null;
  ipAddress: string | null;
  createdAt: string;
}

export interface AdminUserDetailResponse {
  user: AdminUserDto;
  audit: AuditLogEntry[];
}

export interface RoleDto {
  id: number;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissions: string[];
}

export interface PasswordResetResult {
  temporaryPassword: string;
  email: string;
  fullName: string;
  emailSent: boolean;
  emailError?: string | null;
}

export interface AdminActionResult {
  user: AdminUserDto;
  emailSent: boolean;
  emailError?: string | null;
}

export async function listUsers(params: {
  status?: UserStatus | string;
  search?: string;
  page?: number;
  pageSize?: number;
} = {}): Promise<PagedResult<AdminUserDto>> {
  const { data } = await client.get<PagedResult<AdminUserDto>>('/admin/users', { params });
  return data;
}

export async function getUserDetail(id: number): Promise<AdminUserDetailResponse> {
  const { data } = await client.get<AdminUserDetailResponse>(`/admin/users/${id}`);
  return data;
}

export async function approveUser(id: number, roleId: number): Promise<AdminActionResult> {
  const { data } = await client.post<AdminActionResult>(`/admin/users/${id}/approve`, { roleId });
  return data;
}

export async function rejectUser(id: number, reason: string): Promise<AdminActionResult> {
  const { data } = await client.post<AdminActionResult>(`/admin/users/${id}/reject`, { reason });
  return data;
}

export async function suspendUser(id: number, reason: string): Promise<AdminActionResult> {
  const { data } = await client.post<AdminActionResult>(`/admin/users/${id}/suspend`, { reason });
  return data;
}

export async function reactivateUser(id: number): Promise<AdminActionResult> {
  const { data } = await client.post<AdminActionResult>(`/admin/users/${id}/reactivate`);
  return data;
}

export async function changeUserRole(id: number, roleId: number): Promise<AdminActionResult> {
  const { data } = await client.put<AdminActionResult>(`/admin/users/${id}/role`, { roleId });
  return data;
}

export async function resetUserPassword(id: number): Promise<PasswordResetResult> {
  const { data } = await client.post<PasswordResetResult>(`/admin/users/${id}/reset-password`);
  return data;
}

export async function getRecentAudit(limit = 100): Promise<AuditLogEntry[]> {
  const { data } = await client.get<AuditLogEntry[]>('/admin/audit', { params: { limit } });
  return data;
}

export async function getRoles(): Promise<RoleDto[]> {
  const { data } = await client.get<RoleDto[]>('/roles');
  return data;
}
