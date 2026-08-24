import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { TokenStore } from './token-store';

// These auth endpoints are anonymous by gateway/service design — never attach
// a (possibly stale) Bearer token to them.
const ANONYMOUS_AUTH_PATHS = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh'];

export function isAnonymousAuthPath(url: string): boolean {
  return ANONYMOUS_AUTH_PATHS.some((path) => url.includes(path));
}

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  if (isAnonymousAuthPath(req.url)) {
    return next(req);
  }

  const token = inject(TokenStore).getAccessToken();
  if (!token) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
