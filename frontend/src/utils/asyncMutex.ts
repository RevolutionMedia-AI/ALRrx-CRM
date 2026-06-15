/**
 * Coalesce concurrent calls to an async function into a single
 * in-flight promise. Useful for refresh-token flows where N parallel
 * API calls might all notice the token is about to expire and try to
 * refresh at the same time — only the first refresh actually hits
 * the backend; the rest await the same promise.
 */
export class AsyncMutex<T> {
  private inflight: Promise<T> | null = null;

  /**
   * Run `fn` under the mutex. If a call is already in flight, return
   * the existing promise instead of starting a new one. The inflight
   * promise is cleared once it settles (success or error) so a
   * subsequent call (after the original fails) can retry.
   */
  run(fn: () => Promise<T>): Promise<T> {
    if (this.inflight) return this.inflight;
    this.inflight = fn().finally(() => {
      this.inflight = null;
    });
    return this.inflight;
  }
}
