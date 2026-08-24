import { AuthActions } from './auth.actions';
import { authReducer } from './auth.reducer';
import { initialAuthState } from './auth.state';
import { AuthUser } from '../auth.models';

const user: AuthUser = {
  userId: 'u1',
  email: 'test@example.com',
  displayName: 'Test User',
  role: 'Customer',
  emailVerified: false,
};

describe('authReducer', () => {
  it('returns the initial state for an unknown action', () => {
    const state = authReducer(undefined, { type: 'noop' });
    expect(state).toEqual(initialAuthState);
  });

  it('sets status to loading on login/register/restoreSession', () => {
    const state = authReducer(initialAuthState, AuthActions.login({ email: 'a', password: 'b' }));
    expect(state.status).toBe('loading');
    expect(state.error).toBeNull();
  });

  it('sets the user and status=authenticated on loginSuccess', () => {
    const state = authReducer(initialAuthState, AuthActions.loginSuccess({ user }));
    expect(state.user).toEqual(user);
    expect(state.status).toBe('authenticated');
  });

  it('clears the user and records the error on loginFailure', () => {
    const authenticated = authReducer(initialAuthState, AuthActions.loginSuccess({ user }));
    const state = authReducer(authenticated, AuthActions.loginFailure({ error: 'Invalid credentials' }));
    expect(state.user).toBeNull();
    expect(state.status).toBe('error');
    expect(state.error).toBe('Invalid credentials');
  });

  it('resets to initial state on restoreSessionFailure', () => {
    const authenticated = authReducer(initialAuthState, AuthActions.loginSuccess({ user }));
    const state = authReducer(authenticated, AuthActions.restoreSessionFailure());
    expect(state).toEqual(initialAuthState);
  });

  it('resets to initial state on logoutComplete', () => {
    const authenticated = authReducer(initialAuthState, AuthActions.loginSuccess({ user }));
    const state = authReducer(authenticated, AuthActions.logoutComplete());
    expect(state).toEqual(initialAuthState);
  });

  it('flips emailVerified on the current user when verifyEmailSuccess fires', () => {
    const authenticated = authReducer(initialAuthState, AuthActions.loginSuccess({ user }));
    const state = authReducer(authenticated, AuthActions.verifyEmailSuccess());
    expect(state.user?.emailVerified).toBe(true);
  });

  it('verifyEmailSuccess is a no-op when there is no current user', () => {
    const state = authReducer(initialAuthState, AuthActions.verifyEmailSuccess());
    expect(state).toEqual(initialAuthState);
  });
});
