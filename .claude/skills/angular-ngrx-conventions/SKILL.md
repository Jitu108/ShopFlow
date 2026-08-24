---
name: angular-ngrx-conventions
description: Conventions for Angular + NgRx feature state in ClientApp (e.g. core/cart, core/auth) — action groups, entity adapters, effects, and RxJS operator choice. Use whenever adding or changing a feature store (actions/reducer/selectors/effects/state) or a component that dispatches to one.
---

# Angular / NgRx Conventions

Reference implementations: [ClientApp/src/app/core/cart/store](../../../ClientApp/src/app/core/cart/store) and [core/auth/store](../../../ClientApp/src/app/core/auth/store).

## Feature store folder

Each feature owns a `store/` folder with exactly these files, all under the same feature name:

```
<feature>.state.ts       interface + (if entity-backed) createEntityAdapter
<feature>.actions.ts      createActionGroup
<feature>.reducer.ts       createReducer + matching .reducer.spec.ts
<feature>.effects.ts       createEffect classes + matching .effects.spec.ts
<feature>.selectors.ts     createSelector
```

Reducers and effects each get a co-located `.spec.ts` — same rule as the backend: no store logic without a matching test.

## Actions

Use `createActionGroup` (not individual `createAction` calls), one group per feature, `source` set to the feature's display name (e.g. `'Cart'`). For every async operation define three events: the trigger, `<X> Success` with `props<{...}>()`, and `<X> Failure` with `props<{ error: string }>()`. Use `emptyProps()` for actions with no payload.

## State shape

If the feature manages a collection, use `@ngrx/entity`'s `createEntityAdapter<T>()` with an explicit `selectId`, and build `initialState` via `adapter.getInitialState({...extra flags})`. Don't hand-roll array manipulation in the reducer — use `adapter.setAll/upsertOne/removeOne/removeAll`.

## Reducer

- Every triggering action sets `loading: true, error: null`.
- Every `Success` action clears loading and applies the entity adapter operation.
- Every `Failure` action sets `loading: false, error`.
- A `resetState` (or similarly named) action returns `initialState` verbatim for feature-scoped cleanup (e.g. on logout) — see the comment in `cart.reducer.ts` about *not* reusing the "clear" action for this, since that action also fires a real DELETE call.

## Effects

- One `createEffect` per action, named `<action>$`.
- Success/failure are handled with `catchError` mapping to the `Failure` action wrapped in `of(...)` — never let an effect's stream die on an HTTP error.
- Operator choice is deliberate, not defaulted to `switchMap`:
  - `switchMap` for a single in-flight request per intent (e.g. `loadCart`, `clearCart`) where a newer request should supersede an older one.
  - `mergeMap` when concurrent calls are independent and must not cancel each other (e.g. `addItem`/`updateQuantity`/`removeItem` for different products) — comment the reasoning if it's not obvious from the action name.
- Cross-feature reactions (e.g. loading the cart after `AuthActions.loginSuccess`) are their own effect that listens to the other feature's actions via `ofType`, with a comment explaining *why* the coupling exists — don't reach into another feature's service/store directly.
- Use `extractErrorMessage(error)` (from `core/http-error.util`) to normalize HTTP errors into the `error: string` used by `Failure` actions — don't format error messages ad hoc in each effect.

## When adding a new async feature action

1. Add the three actions (trigger/success/failure) to `<feature>.actions.ts`.
2. Add the loading/success/failure `on(...)` handlers to the reducer, plus a reducer spec case for each.
3. Add the effect with the correct operator per the rule above, plus an effects spec.
4. Wire any cross-feature reaction explicitly — don't assume another feature's effects will pick it up.
