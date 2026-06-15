// ─── Shared auth token ────────────────────────────────────────────────────────
// Both alrrx and slice now use a single shared JWT (the backends were aligned
// in Slice.Api/appsettings.json to share the same Key/Issuer/Audience as
// ALRrx.Api). Reading and writing through these helpers means a login on
// either side is enough to access both.
//
// For backwards compatibility we still read the old per-context keys
// (alrrx_token / slice_token) so existing sessions keep working until the
// user logs out. New sessions are written only to SHARED_TOKEN_KEY.

const SHARED_TOKEN_KEY = 'auth_token';
const LEGACY_KEYS = ['alrrx_token', 'slice_token'] as const;

export { SHARED_TOKEN_KEY };

/** Returns the active shared token, or null if the user isn't signed in. */
export function readSharedToken(): string | null {
  const primary = localStorage.getItem(SHARED_TOKEN_KEY);
  if (primary) return primary;
  // Fall back to one of the legacy keys (whichever is still around) and
  // promote it to the shared key so the rest of the app uses a single source
  // of truth from now on.
  for (const key of LEGACY_KEYS) {
    const legacy = localStorage.getItem(key);
    if (legacy) {
      try { localStorage.setItem(SHARED_TOKEN_KEY, legacy); } catch { /* ignore */ }
      return legacy;
    }
  }
  return null;
}

/** Persist the shared token and mirror it into the legacy keys for parity. */
export function writeSharedToken(token: string | null): void {
  if (token == null) {
    clearSharedToken();
    return;
  }
  try { localStorage.setItem(SHARED_TOKEN_KEY, token); } catch { /* ignore */ }
  for (const key of LEGACY_KEYS) {
    try { localStorage.setItem(key, token); } catch { /* ignore */ }
  }
}

/** Clear the shared token and all legacy mirrors so every context starts clean. */
export function clearSharedToken(): void {
  try { localStorage.removeItem(SHARED_TOKEN_KEY); } catch { /* ignore */ }
  for (const key of LEGACY_KEYS) {
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  }
}

/**
 * Decode the JWT payload WITHOUT verifying the signature. The signature
 * is verified by the backend on every request — this is only used to
 * read the `exp` claim so the client can decide to refresh before the
 * next request would 401.
 *
 * Returns null if the token is malformed, has no `exp`, or fails to
 * base64-decode. Returns the expiry as a Unix timestamp in seconds.
 */
export function getJwtExp(token: string | null | undefined): number | null {
  if (!token) return null;
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    // atob doesn't handle base64url — convert to standard base64.
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = b64 + '='.repeat((4 - (b64.length % 4)) % 4);
    const payload = JSON.parse(atob(padded));
    return typeof payload.exp === 'number' ? payload.exp : null;
  } catch {
    return null;
  }
}

/**
 * Returns the milliseconds until the token expires, or null if we
 * can't determine the expiry. A negative value means the token is
 * already expired.
 */
export function msUntilExpiry(token: string | null | undefined): number | null {
  const exp = getJwtExp(token);
  if (exp === null) return null;
  return exp * 1000 - Date.now();
}

/**
 * Whether the token should be refreshed proactively. Returns true when
 * the token is missing, expired, or will expire within the given
 * `marginMs` window (default 60s). The margin protects against clock
 * skew between client and server.
 */
export function shouldRefreshToken(token: string | null | undefined, marginMs = 60_000): boolean {
  const ms = msUntilExpiry(token);
  if (ms === null) return true;
  return ms <= marginMs;
}
