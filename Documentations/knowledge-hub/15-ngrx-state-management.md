# NgRx State Management

## Abstract

`ClientApp` uses NgRx 21 (`@ngrx/store`, `@ngrx/effects`, `@ngrx/entity`, `@ngrx/store-devtools`) — but only for two feature slices, `auth` and `cart`. Every other feature area (catalog, vendor CRUD, admin lists, orders) uses plain `HttpClient` calls plus component-local signals instead. This document explains the Redux pattern NgRx implements, why ShopFlow drew the scoping line where it did, and walks a real action group, entity-backed reducer, effect, and selector from the `cart` feature.

## What it is

NgRx is a Redux-pattern state management library for Angular: a single, app-wide `Store` holds state as a plain object tree; **actions** are plain objects describing "what happened"; **reducers** are pure functions that compute the next state from the current state and an action; **effects** are the place side effects (HTTP calls, navigation) live, listening for actions and dispatching new ones; **selectors** are memoized functions that read a slice of state back out for components to consume. `@ngrx/entity` adds `createEntityAdapter<T>()` on top of this for collections — instead of hand-rolling array find/update/remove logic in a reducer, an entity adapter normalizes a collection into `{ ids: [], entities: {} }` and provides `setAll`/`upsertOne`/`removeOne`/`removeAll` operations plus `getSelectors()` for reading it back as an array.

ShopFlow's app-wide store is configured in [ClientApp/src/app/app.config.ts](../../ClientApp/src/app/app.config.ts):

```ts
import { provideStore } from '@ngrx/store';
import { provideEffects } from '@ngrx/effects';
import { provideStoreDevtools } from '@ngrx/store-devtools';
...
// NgRx is scoped to exactly these two slices — see Documentations/Phases/Phase7-Plan.md
// Decision #2 for why auth+cart and nothing else.
export const appConfig: ApplicationConfig = {
  providers: [
    ...
    provideStore({ auth: authReducer, cart: cartReducer }),
    provideEffects([AuthEffects, CartEffects]),
    provideStoreDevtools({ maxAge: 25, logOnly: !isDevMode() }),
    provideAppInitializer(restoreSessionOnInit),
  ],
};
```

The comment's cross-reference is accurate as of this writing — confirmed by listing `ClientApp/src/app/core/*/store`: only [core/auth/store](../../ClientApp/src/app/core/auth/store) and [core/cart/store](../../ClientApp/src/app/core/cart/store) exist. `core/services/` (`product.ts`, `category.ts`, `order.ts`, `user.ts`) is plain `HttpClient`, no NgRx slice at all — see [Angular-UI.md §1](../Architecture/Angular-UI.md#1-module-tree).

## Why ShopFlow scoped it to only `auth` and `cart`

The actual decision is recorded in [Documentations/Phases/Phase7-Plan.md](../Phases/Phase7-Plan.md)'s decision table:

> Where is NgRx actually justified, vs. plain services, in a portfolio-scale app? — **Exactly two slices: `auth` and `cart`** — the only states that are both genuinely global (read by guards/interceptors/navbar) and multi-writer (cart is mutated from both the catalog page and the cart page). Catalog, vendor CRUD, and admin lists use plain `HttpClient` + signals; introducing NgRx there would be cargo-culted, not earned.

[Angular-UI.md §2](../Architecture/Angular-UI.md#2-why-only-auth-and-cart-are-ngrx) states the same test more concretely: a slice earns NgRx only if it is read by guards/interceptors *and* the navbar simultaneously, *and* it's mutated from more than one place.

| Feature | Read from multiple unrelated places? | Mutated from more than one place? | Approach |
| --- | --- | --- | --- |
| `auth` | yes — guards deciding navigation, the `jwtInterceptor` attaching a header, the navbar rendering role-specific links | yes | NgRx |
| `cart` | yes — the navbar's live item-count badge, the cart page itself | yes — catalog's "Add to Cart" AND the cart page | NgRx |
| catalog | no | no | plain `HttpClient` + signals |
| orders | no | no | plain `HttpClient` + signals |
| vendor CRUD | no | no | plain `HttpClient` + signals |
| admin lists | no | no | plain `HttpClient` + signals |

Everything in the second group is read and written from exactly one page component, so a reducer/effects/selector triple would just add indirection around what a service + signal already does directly — this is stated verbatim in Angular-UI.md: "that's the actual justification for NgRx's overhead, not 'state management' as a blanket rule."

## How it's used

Reference implementation: [core/cart/store](../../ClientApp/src/app/core/cart/store). Each feature owns a `store/` folder with a fixed file set — `<feature>.state.ts`, `<feature>.actions.ts`, `<feature>.reducer.ts` (+ `.reducer.spec.ts`), `<feature>.effects.ts` (+ `.effects.spec.ts`), `<feature>.selectors.ts` — documented in the `angular-ngrx-conventions` skill and matched exactly by both `cart` and `auth`.

### Action group

[cart.actions.ts](../../ClientApp/src/app/core/cart/store/cart.actions.ts) uses `createActionGroup`, one group per feature, with a trigger / `Success` / `Failure` triple for every async operation:

```ts
import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { CartItem } from '../cart.models';

export const CartActions = createActionGroup({
  source: 'Cart',
  events: {
    'Load Cart': emptyProps(),
    'Load Cart Success': props<{ items: CartItem[] }>(),
    'Load Cart Failure': props<{ error: string }>(),

    'Add Item': props<{ productId: string; productName: string; unitPrice: number; quantity: number }>(),
    'Add Item Success': props<{ item: CartItem }>(),
    'Add Item Failure': props<{ error: string }>(),
    ...
    // Local-only reset (no HTTP call) — used on logout. Dispatching
    // ClearCart there would call DELETE /api/cart and actually empty the
    // user's saved cart just because they logged out.
    'Reset State': emptyProps(),
  },
});
```

Note the `Reset State` action's comment: it exists specifically so logout doesn't accidentally fire a real `DELETE /api/cart` by reusing `Clear Cart` — a genuine hazard the house convention calls out explicitly.

### Entity-backed state and reducer

[cart.state.ts](../../ClientApp/src/app/core/cart/store/cart.state.ts) extends `@ngrx/entity`'s `EntityState<T>` rather than hand-declaring an array field:

```ts
import { EntityState } from '@ngrx/entity';
import { CartItem } from '../cart.models';

export interface CartState extends EntityState<CartItem> {
  loading: boolean;
  error: string | null;
}
```

[cart.reducer.ts](../../ClientApp/src/app/core/cart/store/cart.reducer.ts) builds the adapter with an explicit `selectId`, builds `initialState` via `adapter.getInitialState(...)`, and uses adapter operations instead of manual array manipulation:

```ts
export const cartAdapter = createEntityAdapter<CartItem>({
  selectId: (item) => item.productId,
});

export const initialCartState: CartState = cartAdapter.getInitialState({
  loading: false,
  error: null,
});

export const cartReducer = createReducer(
  initialCartState,
  on(
    CartActions.loadCart, CartActions.addItem, CartActions.updateQuantity,
    CartActions.removeItem, CartActions.clearCart,
    (state): CartState => ({ ...state, loading: true, error: null }),
  ),
  on(CartActions.loadCartSuccess, (state, { items }): CartState =>
    cartAdapter.setAll(items, { ...state, loading: false }),
  ),
  on(CartActions.addItemSuccess, CartActions.updateQuantitySuccess, (state, { item }): CartState =>
    cartAdapter.upsertOne(item, { ...state, loading: false }),
  ),
  on(CartActions.removeItemSuccess, (state, { productId }): CartState =>
    cartAdapter.removeOne(productId, { ...state, loading: false }),
  ),
  on(CartActions.clearCartSuccess, (state): CartState => cartAdapter.removeAll({ ...state, loading: false })),
  on(/* all Failure actions */ (state, { error }): CartState => ({ ...state, loading: false, error })),
  on(CartActions.resetState, (): CartState => initialCartState),
);
```

Every triggering action sets `loading: true, error: null`; every `Success` clears loading and applies the adapter operation; every `Failure` sets `loading: false, error`; `resetState` returns `initialState` verbatim — the exact pattern documented in the `angular-ngrx-conventions` skill.

### Effects — HTTP call in, action out

[cart.effects.ts](../../ClientApp/src/app/core/cart/store/cart.effects.ts) has one `createEffect` per action, named `<action>$`, using `catchError` so an HTTP failure never kills the effect's stream:

```ts
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
```

The operator choice is deliberate and commented, not defaulted to `switchMap` everywhere: `loadCart`/`clearCart` use `switchMap` (a newer request should supersede an older in-flight one), while `addItem`/`updateQuantity`/`removeItem` use `mergeMap` (concurrent calls for different products are independent and must not cancel each other).

`cart.effects.ts` also demonstrates the cross-feature reaction pattern — listening to `auth`'s actions rather than reaching into `auth`'s service/store directly, each with a comment explaining the coupling:

```ts
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
```

### Selectors and component usage

[cart.selectors.ts](../../ClientApp/src/app/core/cart/store/cart.selectors.ts) builds a feature selector, reuses the entity adapter's own `selectAll`, and derives count/total from it:

```ts
export const selectCartState = createFeatureSelector<CartState>('cart');
const { selectAll } = cartAdapter.getSelectors();

export const selectCartItems = createSelector(selectCartState, selectAll);
export const selectCartItemCount = createSelector(selectCartItems, (items) =>
  items.reduce((sum, item) => sum + item.quantity, 0),
);
export const selectCartTotal = createSelector(selectCartItems, (items) =>
  items.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0),
);
```

[navbar.ts](../../ClientApp/src/app/shared/components/navbar/navbar.ts) is the real multi-writer/multi-reader consumer that justifies `cart` being NgRx in the first place — it reads both the `auth` and `cart` slices as signals via `Store.selectSignal`:

```ts
import { Store } from '@ngrx/store';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';
import { selectCartItemCount } from '../../../core/cart/store/cart.selectors';

export class Navbar {
  private readonly store = inject(Store);

  readonly user = this.store.selectSignal(selectAuthUser);
  readonly cartCount = this.store.selectSignal(selectCartItemCount);

  logout(): void {
    this.store.dispatch(AuthActions.logout());
  }
}
```

`selectSignal` converts an NgRx selector directly into an Angular signal — the template reads `cartCount()` reactively with no manual subscription/unsubscription.

## Gotchas & deviations

- **Subscribe before you dispatch, not after.** `core/auth/restore-session-on-init.ts` (the app-initializer that silently restores a session on page reload) must create its `firstValueFrom(actions$.pipe(ofType(...)))` subscription *before* calling `store.dispatch(...)`. On a fresh browser with no refresh token, the effect's response fires synchronously inside the `dispatch()` call itself; subscribing afterward means the response already fired and is gone, because `Actions` is a hot `Subject` that never replays — this previously produced a permanently blank screen with no console error. See [Angular-UI.md §3](../Architecture/Angular-UI.md#3-auth--token-flow) for the full account and the regression test that now guards it.
- **`resetState` vs. `clearCart` are not interchangeable.** `resetState` is local-only (no HTTP call); `clearCart` fires a real `DELETE /api/cart`. Logout dispatches `resetState`, never `clearCart` — see the comment in both `cart.actions.ts` and `cart.effects.ts`.
- **A JWT is immutable once issued.** `AuthEffects.refreshAfterVerifyEmail$` calls `Auth.refresh()` immediately after a successful email verification specifically to re-issue a token carrying the updated `emailVerified` claim — the already-issued JWT keeps its stale claim value regardless of what changed in the database, and skipping this effect would leave order placement 403ing despite the UI showing "verified."
- Reducers and effects each get a co-located `.spec.ts` (e.g. [cart.reducer.spec.ts](../../ClientApp/src/app/core/cart/store/cart.reducer.spec.ts), [cart.effects.spec.ts](../../ClientApp/src/app/core/cart/store/cart.effects.spec.ts)) — the same "no store logic without a matching test" rule the backend follows for handlers.
