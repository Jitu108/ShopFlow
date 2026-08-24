import { createReducer, on } from '@ngrx/store';
import { AuthActions } from './auth.actions';
import { AuthState, initialAuthState } from './auth.state';

export const authReducer = createReducer(
  initialAuthState,
  on(
    AuthActions.login,
    AuthActions.register,
    AuthActions.restoreSession,
    (state): AuthState => ({ ...state, status: 'loading', error: null }),
  ),
  on(
    AuthActions.loginSuccess,
    AuthActions.registerSuccess,
    AuthActions.restoreSessionSuccess,
    (state, { user }): AuthState => ({ ...state, user, status: 'authenticated', error: null }),
  ),
  on(
    AuthActions.loginFailure,
    AuthActions.registerFailure,
    (state, { error }): AuthState => ({ ...state, user: null, status: 'error', error }),
  ),
  on(AuthActions.restoreSessionFailure, (): AuthState => initialAuthState),
  on(AuthActions.logoutComplete, (): AuthState => initialAuthState),
  on(AuthActions.verifyEmailSuccess, (state): AuthState =>
    state.user ? { ...state, user: { ...state.user, emailVerified: true } } : state,
  ),
);
