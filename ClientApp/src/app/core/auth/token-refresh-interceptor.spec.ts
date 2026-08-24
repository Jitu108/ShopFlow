import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { tokenRefreshInterceptor } from './token-refresh-interceptor';
import { TokenStore } from './token-store';

@Component({ template: '' })
class LoginStub {}

describe('tokenRefreshInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenRefreshInterceptor])),
        provideHttpClientTesting(),
        // A real /login route, matching app.routes.ts — without it,
        // router.navigateByUrl('/login') in the interceptor's failure path
        // rejects with NG04002 (found by actually running this test, not
        // knowable from reading the interceptor in isolation).
        provideRouter([{ path: 'login', component: LoginStub }]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
    tokenStore.setTokens('expired-token', 'refresh-token');
  });

  afterEach(() => httpMock.verify());

  it('dedupes N concurrent 401s into exactly one /api/auth/refresh call, then retries all N', async () => {
    const requestCount = 3;
    const results = Promise.all(
      Array.from({ length: requestCount }, () => firstValueFrom(http.get('/api/orders'))),
    );

    // All N initial requests fail with 401 before any refresh happens.
    const initialRequests = httpMock.match('/api/orders');
    expect(initialRequests.length).toBe(requestCount);
    initialRequests.forEach((req) => req.flush(null, { status: 401, statusText: 'Unauthorized' }));

    // Exactly one refresh call must have been triggered by those N failures,
    // not N — the refresh token is server-side single-use/rotated, so N
    // concurrent refresh calls would make N-1 of them fail.
    const refreshRequests = httpMock.match('/api/auth/refresh');
    expect(refreshRequests.length).toBe(1);
    refreshRequests[0].flush({
      accessToken: 'fresh-token',
      refreshToken: 'new-refresh-token',
      email: 'test@example.com',
      displayName: 'Test User',
      role: 'Customer',
    });

    // All N original requests are retried with the fresh token.
    const retriedRequests = httpMock.match('/api/orders');
    expect(retriedRequests.length).toBe(requestCount);
    retriedRequests.forEach((req) => {
      expect(req.request.headers.get('Authorization')).toBe('Bearer fresh-token');
      req.flush({ ok: true });
    });

    await expect(results).resolves.toHaveLength(requestCount);
  });

  it('clears tokens and rethrows the original error when refresh itself fails', async () => {
    const promise = firstValueFrom(http.get('/api/orders')).catch((err) => err);

    httpMock.expectOne('/api/orders').flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    const error = await promise;
    expect(error.status).toBe(401);
    expect(tokenStore.getAccessToken()).toBeNull();
    expect(tokenStore.getRefreshToken()).toBeNull();
  });

  it('does not intercept a 401 from the refresh call itself (no infinite loop)', async () => {
    tokenStore.clear(); // no refresh token available
    const promise = firstValueFrom(http.get('/api/orders')).catch((err) => err);

    httpMock.expectOne('/api/orders').flush(null, { status: 401, statusText: 'Unauthorized' });

    // Auth.refresh() throws synchronously with no refresh token, so no
    // /api/auth/refresh request should ever be made.
    httpMock.expectNone('/api/auth/refresh');

    const error = await promise;
    expect(error.status).toBe(401);
  });
});
