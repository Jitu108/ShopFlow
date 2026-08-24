import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';
import { decodeJwt } from './jwt.util';
import { UserRole } from './auth.models';

export function roleGuard(requiredRole: UserRole): CanActivateFn {
  return () => {
    const token = inject(TokenStore).getAccessToken();
    const decoded = token ? decodeJwt(token) : null;
    if (decoded?.role === requiredRole) {
      return true;
    }
    return inject(Router).createUrlTree(['/login']);
  };
}
