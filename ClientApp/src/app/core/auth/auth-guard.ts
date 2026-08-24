import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token-store';

export const authGuard: CanActivateFn = () => {
  if (inject(TokenStore).getAccessToken()) {
    return true;
  }
  return inject(Router).createUrlTree(['/login']);
};
