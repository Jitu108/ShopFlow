import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { jwtInterceptor } from './jwt-interceptor';
import { TokenStore } from './token-store';

describe('jwtInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokenStore: TokenStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([jwtInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStore);
  });

  afterEach(() => httpMock.verify());

  it('attaches the Bearer token to a protected request', async () => {
    tokenStore.setAccessToken('my-token');
    const promise = firstValueFrom(http.get('/api/orders'));
    const req = httpMock.expectOne('/api/orders');
    expect(req.request.headers.get('Authorization')).toBe('Bearer my-token');
    req.flush({});
    await promise;
  });

  it('does not attach a header when there is no token', async () => {
    const promise = firstValueFrom(http.get('/api/orders'));
    const req = httpMock.expectOne('/api/orders');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
    await promise;
  });

  it.each(['/api/auth/login', '/api/auth/register', '/api/auth/refresh'])(
    'never attaches a header to the anonymous endpoint %s, even with a token set',
    async (path) => {
      tokenStore.setAccessToken('my-token');
      const promise = firstValueFrom(http.post(path, {}));
      const req = httpMock.expectOne(path);
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush({});
      await promise;
    },
  );
});
