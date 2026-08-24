import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { Auth } from './auth';
import { TokenStore } from './token-store';
import { TokenRefreshGate } from './token-refresh-gate';
import { isAnonymousAuthPath } from './jwt-interceptor';

export const tokenRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  if (isAnonymousAuthPath(req.url)) {
    return next(req);
  }

  const auth = inject(Auth);
  const tokenStore = inject(TokenStore);
  const gate = inject(TokenRefreshGate);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return gate.refresh(auth, tokenStore).pipe(
        switchMap((user) => {
          if (!user) {
            router.navigateByUrl('/login');
            return throwError(() => error);
          }
          const token = tokenStore.getAccessToken();
          return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
        }),
      );
    }),
  );
};
