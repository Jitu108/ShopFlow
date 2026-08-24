import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError, finalize, shareReplay } from 'rxjs/operators';
import { Auth } from './auth';
import { TokenStore } from './token-store';
import { AuthUser } from './auth.models';

// The refresh token is server-side single-use/rotated (Identity's
// RefreshTokenCommandHandler revokes it on every use), so if N requests 401
// concurrently they must share exactly one refresh call, not race N separate
// ones that would make N-1 of them fail. A single shared, replayed observable
// per gate instance (registered `providedIn: 'root'`, so one per app/test)
// is what makes that guarantee.
@Injectable({
  providedIn: 'root',
})
export class TokenRefreshGate {
  private inFlight$: Observable<AuthUser | null> | null = null;

  refresh(auth: Auth, tokenStore: TokenStore): Observable<AuthUser | null> {
    if (!this.inFlight$) {
      this.inFlight$ = auth.refresh().pipe(
        catchError(() => {
          tokenStore.clear();
          return of(null);
        }),
        finalize(() => {
          this.inFlight$ = null;
        }),
        shareReplay(1),
      );
    }
    return this.inFlight$;
  }
}
