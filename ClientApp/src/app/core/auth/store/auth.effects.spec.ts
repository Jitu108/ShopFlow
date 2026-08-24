import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideMockActions } from '@ngrx/effects/testing';
import { Observable, of, throwError, Subject, firstValueFrom, take } from 'rxjs';
import { AuthEffects } from './auth.effects';
import { AuthActions } from './auth.actions';
import { Auth } from '../auth';
import { AuthUser } from '../auth.models';

const verifiedUser: AuthUser = {
  userId: 'u1',
  email: 'a@example.com',
  displayName: 'A',
  role: 'Customer',
  emailVerified: true,
};

describe('AuthEffects', () => {
  let actions$: Subject<unknown>;

  function createEffects(refreshResult: Observable<AuthUser>) {
    actions$ = new Subject();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        AuthEffects,
        provideMockActions(() => actions$),
        { provide: Auth, useValue: { refresh: () => refreshResult } },
      ],
    });
    return TestBed.inject(AuthEffects);
  }

  it('re-issues a fresh token after verifyEmailSuccess, since the old JWT still carries the stale claim', async () => {
    const effects = createEffects(of(verifiedUser));

    const emitted = firstValueFrom(effects.refreshAfterVerifyEmail$.pipe(take(1)));
    actions$.next(AuthActions.verifyEmailSuccess());

    expect(await emitted).toEqual(AuthActions.restoreSessionSuccess({ user: verifiedUser }));
  });

  it('falls back to restoreSessionFailure if the post-verification refresh itself fails', async () => {
    const effects = createEffects(throwError(() => new Error('refresh failed')));

    const emitted = firstValueFrom(effects.refreshAfterVerifyEmail$.pipe(take(1)));
    actions$.next(AuthActions.verifyEmailSuccess());

    expect(await emitted).toEqual(AuthActions.restoreSessionFailure());
  });
});
