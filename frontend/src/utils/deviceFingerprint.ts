/**
 * Compute a coarse device fingerprint from browser-level signals that
 * are stable across reloads but vary between devices. Used as a
 * secondary rate-limit key so a single user behind a shared IP cannot
 * exhaust the bucket and starve other users, while still letting a
 * single attacker on a single device hit the per-device limit.
 *
 * NOTE: this is NOT a security-grade identifier. A determined attacker
 * can override navigator.userAgent or rotate the screen dimensions. The
 * intent is only to discriminate "10 employees behind a corporate NAT"
 * from "1 attacker spinning up 100 tabs".
 */
export function deviceFingerprint(): string {
  if (typeof window === 'undefined') return 'ssr';
  const parts = [
    navigator.userAgent,
    `${screen.width}x${screen.height}x${screen.colorDepth}`,
    Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'unknown-tz',
    navigator.language ?? 'unknown-lang',
    // hardwareConcurrency is 2, 4, 8, 16 — useful for distinguishing
    // a phone (4-8 cores) from a desktop (8-32) and from a server (lots).
    String(navigator.hardwareConcurrency ?? 0),
  ];
  // Simple djb2 hash — we don't need cryptographic strength, just a
  // stable key for the rate-limiter partitioner.
  let hash = 5381;
  const text = parts.join('|');
  for (let i = 0; i < text.length; i++) {
    hash = ((hash << 5) + hash + text.charCodeAt(i)) | 0;
  }
  return `d${(hash >>> 0).toString(36)}`;
}
