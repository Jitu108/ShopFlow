import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { AuthUser } from '../auth.models';

export const AuthActions = createActionGroup({
  source: 'Auth',
  events: {
    Login: props<{ email: string; password: string }>(),
    'Login Success': props<{ user: AuthUser }>(),
    'Login Failure': props<{ error: string }>(),

    Register: props<{ email: string; password: string; displayName: string }>(),
    'Register Success': props<{ user: AuthUser }>(),
    'Register Failure': props<{ error: string }>(),

    'Restore Session': emptyProps(),
    'Restore Session Success': props<{ user: AuthUser }>(),
    'Restore Session Failure': emptyProps(),

    Logout: emptyProps(),
    'Logout Complete': emptyProps(),

    'Verify Email Requested': emptyProps(),
    'Verify Email Success': emptyProps(),
    'Verify Email Failure': props<{ error: string }>(),
  },
});
