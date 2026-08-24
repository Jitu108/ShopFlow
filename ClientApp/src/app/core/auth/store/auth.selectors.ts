import { createFeatureSelector, createSelector } from '@ngrx/store';
import { AuthState } from './auth.state';

export const selectAuthState = createFeatureSelector<AuthState>('auth');

export const selectAuthUser = createSelector(selectAuthState, (state) => state.user);
export const selectIsAuthenticated = createSelector(selectAuthUser, (user) => !!user);
export const selectAuthStatus = createSelector(selectAuthState, (state) => state.status);
export const selectAuthError = createSelector(selectAuthState, (state) => state.error);
