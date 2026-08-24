import { AuthUser } from '../auth.models';

export interface AuthState {
  user: AuthUser | null;
  status: 'idle' | 'loading' | 'authenticated' | 'error';
  error: string | null;
}

export const initialAuthState: AuthState = {
  user: null,
  status: 'idle',
  error: null,
};
