import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authGuard } from './auth-guard';
import { TokenStore } from './token-store';

describe('authGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function runGuard() {
    return TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
  }

  it('allows navigation when an access token is present', () => {
    tokenStore.setAccessToken('some-token');
    expect(runGuard()).toBe(true);
  });

  it('redirects to /login when there is no access token', () => {
    const result = runGuard();
    expect(result).toEqual(router.createUrlTree(['/login']));
  });
});
