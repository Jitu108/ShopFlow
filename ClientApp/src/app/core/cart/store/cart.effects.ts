import { Injectable, inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { of } from 'rxjs';
import { catchError, filter, map, mergeMap, switchMap } from 'rxjs/operators';
import { CartService } from '../cart';
import { extractErrorMessage } from '../../http-error.util';
import { AuthActions } from '../../auth/store/auth.actions';
import { CartActions } from './cart.actions';

@Injectable()
export class CartEffects {
  private readonly actions$ = inject(Actions);
  private readonly cartService = inject(CartService);

  loadCart$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CartActions.loadCart),
      switchMap(() =>
        this.cartService.getCart().pipe(
          map((items) => CartActions.loadCartSuccess({ items })),
          catchError((error) => of(CartActions.loadCartFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  // mergeMap, not switchMap/exhaustMap: adding/updating/removing different
  // products are independent operations and must not cancel each other.
  addItem$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CartActions.addItem),
      mergeMap(({ productId, productName, unitPrice, quantity }) =>
        this.cartService.addItem({ productId, productName, unitPrice, quantity }).pipe(
          map((item) => CartActions.addItemSuccess({ item })),
          catchError((error) => of(CartActions.addItemFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  updateQuantity$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CartActions.updateQuantity),
      mergeMap(({ productId, quantity }) =>
        this.cartService.updateItemQuantity(productId, quantity).pipe(
          map((item) => CartActions.updateQuantitySuccess({ item })),
          catchError((error) => of(CartActions.updateQuantityFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  removeItem$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CartActions.removeItem),
      mergeMap(({ productId }) =>
        this.cartService.removeItem(productId).pipe(
          map(() => CartActions.removeItemSuccess({ productId })),
          catchError((error) => of(CartActions.removeItemFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  clearCart$ = createEffect(() =>
    this.actions$.pipe(
      ofType(CartActions.clearCart),
      switchMap(() =>
        this.cartService.clearCart().pipe(
          map(() => CartActions.clearCartSuccess()),
          catchError((error) => of(CartActions.clearCartFailure({ error: extractErrorMessage(error) }))),
        ),
      ),
    ),
  );

  // Load the cart right after login/register/session-restore so the navbar
  // badge is correct even before the user visits /customer/cart.
  loadCartOnAuth$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.loginSuccess, AuthActions.registerSuccess, AuthActions.restoreSessionSuccess),
      filter(({ user }) => user.role === 'Customer'),
      map(() => CartActions.loadCart()),
    ),
  );

  // Local-only reset, not ClearCart — logging out must not delete the
  // user's saved server-side cart.
  resetOnLogout$ = createEffect(() =>
    this.actions$.pipe(
      ofType(AuthActions.logoutComplete),
      map(() => CartActions.resetState()),
    ),
  );
}
