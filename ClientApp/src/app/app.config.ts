import { ApplicationConfig, isDevMode, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideStore } from '@ngrx/store';
import { provideEffects } from '@ngrx/effects';
import { provideStoreDevtools } from '@ngrx/store-devtools';

import { routes } from './app.routes';
import { authReducer } from './core/auth/store/auth.reducer';
import { AuthEffects } from './core/auth/store/auth.effects';
import { restoreSessionOnInit } from './core/auth/restore-session-on-init';
import { jwtInterceptor } from './core/auth/jwt-interceptor';
import { tokenRefreshInterceptor } from './core/auth/token-refresh-interceptor';
import { cartReducer } from './core/cart/store/cart.reducer';
import { CartEffects } from './core/cart/store/cart.effects';

// NgRx is scoped to exactly these two slices — see Documentations/Phases/Phase7-Plan.md
// Decision #2 for why auth+cart and nothing else.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAnimationsAsync(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([jwtInterceptor, tokenRefreshInterceptor])),
    provideStore({ auth: authReducer, cart: cartReducer }),
    provideEffects([AuthEffects, CartEffects]),
    provideStoreDevtools({ maxAge: 25, logOnly: !isDevMode() }),
    provideAppInitializer(restoreSessionOnInit),
  ],
};
