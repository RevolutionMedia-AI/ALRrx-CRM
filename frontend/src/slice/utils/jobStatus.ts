import type { SliceJobStatus } from '../types';

const KNOWN_STATUSES: ReadonlyArray<SliceJobStatus> = [
  'Pending',
  'Extracting',
  'Processing',
  'Merging',
  'Completed',
  'Failed',
];

export function normalizeJobStatus(raw: string | null | undefined): SliceJobStatus {
  if (!raw) return 'Pending';
  // Si ya es uno conocido y está en PascalCase, devolverlo tal cual.
  if ((KNOWN_STATUSES as ReadonlyArray<string>).includes(raw)) {
    return raw as SliceJobStatus;
  }
  const lower = String(raw).toLowerCase();
  if (lower.startsWith('pend')) return 'Pending';
  if (lower.startsWith('extract')) return 'Extracting';
  if (lower.startsWith('process')) return 'Processing';
  if (lower.startsWith('merg')) return 'Merging';
  if (lower.startsWith('complet')) return 'Completed';
  if (lower.startsWith('fail')) return 'Failed';
  // No matcheó nada: devolver Pending como fallback seguro para que el polling termine
  // solo si realmente hay un reportId, pero en la UI se mostrará como UNKNOWN.
  return 'Pending';
}

export function isTerminalStatus(raw: string | null | undefined): boolean {
  const s = normalizeJobStatus(raw);
  return s === 'Completed' || s === 'Failed';
}
