const GOOGLE_TOKEN_KEY = 'google_access_token';

export function setGoogleAccessToken(token: string | null): void {
  if (token) {
    localStorage.setItem(GOOGLE_TOKEN_KEY, token);
  } else {
    localStorage.removeItem(GOOGLE_TOKEN_KEY);
  }
}

export function getGoogleAccessToken(): string | null {
  return localStorage.getItem(GOOGLE_TOKEN_KEY);
}
