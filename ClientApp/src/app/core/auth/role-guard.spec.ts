import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { roleGuard } from './role-guard';
import { TokenStore } from './token-store';

// header.payload.signature with role claim = "Vendor" / exp far in the future.
const VENDOR_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiJ2MSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6InZlbmRvckBleGFtcGxlLmNvbSIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IlZlbmRvciIsImVtYWlsVmVyaWZpZWQiOiJ0cnVlIiwiZXhwIjo5OTk5OTk5OTk5fQ.fake';
// same shape but role = "Customer"
const CUSTOMER_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiJjMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6ImN1c3RvbWVyQGV4YW1wbGUuY29tIiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9yb2xlIjoiQ3VzdG9tZXIiLCJlbWFpbFZlcmlmaWVkIjoidHJ1ZSIsImV4cCI6OTk5OTk5OTk5OX0.fake';

describe('roleGuard', () => {
  let tokenStore: TokenStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    tokenStore = TestBed.inject(TokenStore);
    router = TestBed.inject(Router);
  });

  function runGuard(requiredRole: 'Vendor' | 'Admin' | 'Customer') {
    const guard = roleGuard(requiredRole);
    return TestBed.runInInjectionContext(() => guard({} as never, {} as never));
  }

  it('allows a Vendor into a Vendor-gated route', () => {
    tokenStore.setAccessToken(VENDOR_JWT);
    expect(runGuard('Vendor')).toBe(true);
  });

  it('redirects a Customer away from a Vendor-gated route', () => {
    tokenStore.setAccessToken(CUSTOMER_JWT);
    expect(runGuard('Vendor')).toEqual(router.createUrlTree(['/login']));
  });

  it('redirects when there is no token at all', () => {
    expect(runGuard('Admin')).toEqual(router.createUrlTree(['/login']));
  });
});
