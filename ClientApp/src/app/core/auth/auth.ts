import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { TokenStore } from './token-store';
import { decodeJwt } from './jwt.util';
import { AuthApiResponse, AuthUser, LoginRequest, RegisterRequest } from './auth.models';

@Injectable({
  providedIn: 'root',
})
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly tokenStore = inject(TokenStore);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/auth`;

  register(request: RegisterRequest): Observable<AuthUser> {
    return this.http
      .post<AuthApiResponse>(`${this.baseUrl}/register`, request)
      .pipe(map((response) => this.applyAuthResponse(response)));
  }

  login(request: LoginRequest): Observable<AuthUser> {
    return this.http
      .post<AuthApiResponse>(`${this.baseUrl}/login`, request)
      .pipe(map((response) => this.applyAuthResponse(response)));
  }

  refresh(): Observable<AuthUser> {
    const refreshToken = this.tokenStore.getRefreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }
    return this.http
      .post<AuthApiResponse>(`${this.baseUrl}/refresh`, { token: refreshToken })
      .pipe(map((response) => this.applyAuthResponse(response)));
  }

  // Best-effort server-side revoke; local state is always cleared regardless
  // of whether the network call succeeds.
  logout(): Observable<void> {
    const refreshToken = this.tokenStore.getRefreshToken();
    const request$ = refreshToken
      ? this.http.post<void>(`${this.baseUrl}/logout`, { token: refreshToken })
      : of(undefined);
    return request$.pipe(
      catchError(() => of(undefined)),
      tap(() => this.tokenStore.clear()),
    );
  }

  verifyEmail(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/verify-email`, {});
  }

  // Called once at app startup (see app.config.ts's provideAppInitializer).
  // If a refresh token survived a page reload in sessionStorage, silently
  // exchange it for a fresh access token instead of forcing re-login.
  tryRestoreSession(): Observable<AuthUser | null> {
    if (!this.tokenStore.getRefreshToken()) {
      return of(null);
    }
    return this.refresh().pipe(
      catchError(() => {
        this.tokenStore.clear();
        return of(null);
      }),
    );
  }

  private applyAuthResponse(response: AuthApiResponse): AuthUser {
    this.tokenStore.setTokens(response.accessToken, response.refreshToken);
    const decoded = decodeJwt(response.accessToken);
    return {
      userId: decoded?.userId ?? '',
      email: response.email,
      displayName: response.displayName,
      role: response.role as AuthUser['role'],
      emailVerified: decoded?.emailVerified ?? false,
    };
  }
}
