import { useEffect, useState, useCallback } from 'react';

const STORAGE_KEY = 'tv.navHidden';
const EVENT_NAME = 'tv:nav-toggle';

function readInitial(): boolean {
  if (typeof window === 'undefined') return false;
  try {
    return window.localStorage.getItem(STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

export function useNavHidden() {
  const [hidden, setHidden] = useState<boolean>(readInitial);

  useEffect(() => {
    const handler = (event: Event) => {
      const detail = (event as CustomEvent<boolean>).detail;
      if (typeof detail === 'boolean') setHidden(detail);
    };
    window.addEventListener(EVENT_NAME, handler);
    return () => window.removeEventListener(EVENT_NAME, handler);
  }, []);

  const toggle = useCallback(() => {
    setHidden((prev) => {
      const next = !prev;
      try {
        window.localStorage.setItem(STORAGE_KEY, String(next));
      } catch {
        /* ignore quota / disabled storage */
      }
      window.dispatchEvent(new CustomEvent(EVENT_NAME, { detail: next }));
      return next;
    });
  }, []);

  return [hidden, toggle] as const;
}
