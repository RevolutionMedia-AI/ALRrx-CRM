// Date/time formatting utilities.
//
// The backend stores every timestamp in UTC and returns ISO 8601 strings
// (e.g. "2026-06-16T03:09:14Z"). The frontend used to render these with
// the default `Date.toLocaleString()`, which means whatever timezone the
// browser happens to be in — usually UTC for headless deploys, which
// shows up wrong for everyone in the US.
//
// RevolutionMedia runs on Pacific time, so we pin every render to
// America/Los_Angeles (PST/PDT, auto-DST). If we ever need a different
// timezone per user, swap the constant — every callsite updates at once.

const DEFAULT_TZ = 'America/Los_Angeles';

/**
 * Format an ISO date string as `M/D/YYYY, h:mm:ss a` in Pacific time.
 * Returns '—' for null/empty input so callers can drop the null-check
 * in JSX.
 */
export function formatDateTime(
  iso: string | null | undefined,
  timeZone: string = DEFAULT_TZ,
): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString('en-US', {
    timeZone,
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
    hour12: true,
  });
}

/**
 * Date-only variant — `M/D/YYYY` in Pacific time. Same '—' fallback.
 */
export function formatDate(
  iso: string | null | undefined,
  timeZone: string = DEFAULT_TZ,
): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString('en-US', {
    timeZone,
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
  });
}
