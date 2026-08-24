import { Injectable } from '@angular/core';

const REFRESH_TOKEN_KEY = 'shopflow.refreshToken';

// Access token: memory only. Refresh token: sessionStorage, so a page reload
// doesn't force re-login — a deliberate deviation from the spec's literal
// "memory only", confirmed with the user. Both are cleared on tab close either
// way, since sessionStorage doesn't survive that. See Decision #1 in
// Documentations/Phases/Phase7-Plan.md.
@Injectable({
  providedIn: 'root',
})
export class TokenStore {
  private accessToken: string | null = null;

  getAccessToken(): string | null {
    return this.accessToken;
  }

  setAccessToken(token: string | null): void {
    this.accessToken = token;
  }

  getRefreshToken(): string | null {
    return sessionStorage.getItem(REFRESH_TOKEN_KEY);
  }

  setRefreshToken(token: string | null): void {
    if (token) {
      sessionStorage.setItem(REFRESH_TOKEN_KEY, token);
    } else {
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    }
  }

  setTokens(accessToken: string, refreshToken: string): void {
    this.setAccessToken(accessToken);
    this.setRefreshToken(refreshToken);
  }

  clear(): void {
    this.setAccessToken(null);
    this.setRefreshToken(null);
  }
}
