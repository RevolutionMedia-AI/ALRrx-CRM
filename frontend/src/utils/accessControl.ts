import type { PlatformAccess } from '../services/authApi';

export type AccessGroup = 'both' | 'slice' | 'altrx' | 'none';

export type AccessDecision =
  | { group: 'slice' | 'altrx'; redirectTo: string }
  | { group: 'both'; redirectTo: null }
  | { group: 'none'; redirectTo: null };

export const ROUTES = {
  slice: '/slice',
  altrx: '/dashboard',
} as const;

/**
 * Resolves a user's access decision based on their stored PlatformAccess
 * (data-driven via the Admin Panel). Falls back to 'None' if missing.
 */
export function getAccessGroup(platformAccess: PlatformAccess | null | undefined): AccessGroup {
  switch (platformAccess) {
    case 'Both':  return 'both';
    case 'Altrx': return 'altrx';
    case 'Slice': return 'slice';
    case 'None':
    default:
      return 'none';
  }
}

export function resolveAccess(platformAccess: PlatformAccess | null | undefined): AccessDecision {
  const group = getAccessGroup(platformAccess);
  switch (group) {
    case 'slice':
      return { group, redirectTo: ROUTES.slice };
    case 'altrx':
      return { group, redirectTo: ROUTES.altrx };
    case 'both':
      return { group, redirectTo: null };
    case 'none':
    default:
      return { group: 'none', redirectTo: null };
  }
}
