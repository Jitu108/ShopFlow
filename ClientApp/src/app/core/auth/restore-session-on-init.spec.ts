import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideStore } from '@ngrx/store';
import { provideEffects } from '@ngrx/effects';
import { restoreSessionOnInit } from './restore-session-on-init';
import { authReducer } from './store/auth.reducer';
import { AuthEffects } from './store/auth.effects';
import { TokenStore } from './token-store';

// Regression test for a real bug that shipped a blank screen with zero
// console output: when there is no refresh token, restoreSession$'s effect
// resolves synchronously (Auth.tryRestoreSession() returns of(null)
// immediately). If the app initializer dispatched AuthActions.restoreSession()
// BEFORE subscribing to the response, the response action would already have
// fired and been missed (Actions is a hot Subject — no replay), hanging
// app bootstrap forever. This must resolve promptly even in that exact
// synchronous, no-refresh-token case.
describe('restoreSessionOnInit', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideStore({ auth: authReducer }),
        provideEffects([AuthEffects]),
      ],
    });
  });

  it('resolves promptly when there is no refresh token (the fully-synchronous path)', async () => {
    // No token set — TokenStore.getRefreshToken() returns null, forcing
    // Auth.tryRestoreSession()'s synchronous of(null) branch.
    const promise = TestBed.runInInjectionContext(() => restoreSessionOnInit());

    await expect(
      Promise.race([
        promise,
        new Promise((_, reject) => setTimeout(() => reject(new Error('hung — subscribed too late')), 1000)),
      ]),
    ).resolves.toBeDefined();
  });

  it('also resolves when a refresh token exists and the refresh call completes asynchronously', async () => {
    TestBed.inject(TokenStore).setTokens('stale-access', 'stale-refresh');

    const promise = TestBed.runInInjectionContext(() => restoreSessionOnInit());

    TestBed.inject(HttpTestingController)
      .expectOne((req) => req.url.endsWith('/api/auth/refresh'))
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(
      Promise.race([
        promise,
        new Promise((_, reject) => setTimeout(() => reject(new Error('hung')), 1000)),
      ]),
    ).resolves.toBeDefined();
  });
});
