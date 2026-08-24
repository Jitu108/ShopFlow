import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { of } from 'rxjs';
import { catchError, map, switchMap, tap } from 'rxjs/operators';
import { Auth } from '../auth';
import { extractErrorMessage } from '../../http-error.util';
import { AuthActions } from './auth.actions';

@Injectable()
export class AuthEffects {
  private readonly actions$ = inject(Actions);
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  login$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.login),
      switchMap(({ email, password }) =>
        this.auth.login({ email, password }).pipe(
          map((user) => AuthActions.loginSuccess({ user })),
          catchError((error) => of(AuthActions.loginFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  register$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.register),
      switchMap(({ email, password, displayName }) =>
        this.auth.register({ email, password, displayName }).pipe(
          map((user) => AuthActions.registerSuccess({ user })),
          catchError((error) => of(AuthActions.registerFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  restoreSession$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.restoreSession),
      switchMap(() =>
        this.auth.tryRestoreSession().pipe(
          map((user) => (user ? AuthActions.restoreSessionSuccess({ user }) : AuthActions.restoreSessionFailure())),
          catchError(() => of(AuthActions.restoreSessionFailure())),
        ),
      ),
    ),
  );

  logout$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.logout),
      switchMap(() => this.auth.logout().pipe(map(() => AuthActions.logoutComplete()))),
    ),
  );

  verifyEmail$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.verifyEmailRequested),
      switchMap(() =>
        this.auth.verifyEmail().pipe(
          map(() => AuthActions.verifyEmailSuccess()),
          catchError((error) => of(AuthActions.verifyEmailFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  // POST /api/auth/verify-email flips the DB flag, but the JWT already
  // issued still carries the stale emailVerified="false" claim — a JWT is
  // immutable once signed. Without re-issuing one via refresh() here, the
  // Gateway's RouteClaimsRequirement on order placement would keep 403'ing
  // even after a "successful" verification, since it checks the token's
  // claim, not the database.
  refreshAfterVerifyEmail$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.verifyEmailSuccess),
      switchMap(() =>
        this.auth.refresh().pipe(
          map((user) => AuthActions.restoreSessionSuccess({ user })),
          catchError(() => of(AuthActions.restoreSessionFailure())),
        ),
      ),
    ),
  );

  navigateAfterAuth$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(AuthActions.loginSuccess, AuthActions.registerSuccess),
        tap(() => this.router.navigateByUrl('/customer/catalog')),
      ),
    { dispatch: false },
  );

  navigateAfterLogout$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(AuthActions.logoutComplete),
        tap(() => this.router.navigateByUrl('/login')),
      ),
    { dispatch: false },
  );
}
