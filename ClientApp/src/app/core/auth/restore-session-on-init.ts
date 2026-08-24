import { inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { Actions, ofType } from '@ngrx/effects';
import { firstValueFrom } from 'rxjs';
import { take } from 'rxjs/operators';
import { AuthActions } from './store/auth.actions';

// Exchanges a sessionStorage-persisted refresh token for a fresh access
// token before the router activates any route, so a page reload doesn't
// land on a guarded route with a stale in-memory-only access token.
//
// Subscribe BEFORE dispatching, never after: when there's no refresh token,
// restoreSession$'s effect resolves synchronously (of(null) emits
// immediately on subscription), so restoreSessionFailure is dispatched and
// gone before control would even return from a dispatch-then-subscribe
// ordering. Actions is a hot Subject — it never replays to a late
// subscriber — so getting this order wrong hangs app bootstrap forever
// with no thrown error (confirmed live: this exact bug shipped a blank
// screen with zero console output on first browser load).
export function restoreSessionOnInit(): Promise<unknown> {
  const store = inject(Store);
  const actions$ = inject(Actions);
  const promise = firstValueFrom(
    actions$.pipe(ofType(AuthActions.restoreSessionSuccess, AuthActions.restoreSessionFailure), take(1)),
  );
  store.dispatch(AuthActions.restoreSession());
  return promise;
}
