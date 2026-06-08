export type AccessGroup = 'both' | 'slice' | 'altrx' | 'none';

export type AccessDecision =
  | { group: 'slice' | 'altrx'; redirectTo: string }
  | { group: 'both'; redirectTo: null }
  | { group: 'none'; redirectTo: null };

const SLICE_ONLY: readonly string[] = [
  'pedro@revolutionmedia.ai',
  'ofelia.palomino@revolutionmedia.ai',
  'victor.ramirez@revolutionmedia.ai',
  'jose.camacho@revolutionmedia.ai',
  'luis.mariano@revolutionmedia.ai',
  'nayeli.novoa@revolutionmedia.ai',
  'eduardo.hernandez@revolutionmedia.ai',
  'kenny.santaella@revolutionmedia.ai',
];

const ALTRX_ONLY: readonly string[] = [
  'jessica.duarte@revolutionmedia.ai',
  'silverio.arellano@revolutionmedia.ai',
];

const BOTH: readonly string[] = [
  'david@revolutionmedia.ai',
  'j.lines@revolutionmedia.ai',
  'cuauhtemoc@revolutionmedia.ai',
  'kevin.escalante@revolutionmedia.ai',
];

export const ROUTES = {
  slice: '/slice',
  altrx: '/dashboard',
} as const;

function normalize(email: string | null | undefined): string {
  return (email ?? '').trim().toLowerCase();
}

export function getAccessGroup(userEmail: string | null | undefined): AccessGroup {
  const email = normalize(userEmail);
  if (!email) return 'none';
  if (BOTH.includes(email)) return 'both';
  if (SLICE_ONLY.includes(email)) return 'slice';
  if (ALTRX_ONLY.includes(email)) return 'altrx';
  return 'none';
}

export function resolveAccess(userEmail: string | null | undefined): AccessDecision {
  const group = getAccessGroup(userEmail);
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
