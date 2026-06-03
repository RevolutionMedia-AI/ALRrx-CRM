import type { VicidialFormIdentity } from '../types';

const TOKEN_KEY = 'alrrx_form_token';
const IDENTITY_KEY = 'alrrx_form_identity';

export function saveFormToken(token: string, identity: VicidialFormIdentity): void {
  sessionStorage.setItem(TOKEN_KEY, token);
  sessionStorage.setItem(IDENTITY_KEY, JSON.stringify(identity));
}

export function clearFormToken(): void {
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(IDENTITY_KEY);
}

export function getFormToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY);
}

export function getFormIdentity(): VicidialFormIdentity | null {
  const raw = sessionStorage.getItem(IDENTITY_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as VicidialFormIdentity;
  } catch {
    return null;
  }
}
