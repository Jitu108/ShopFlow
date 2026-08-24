# Angular UI — Architecture

Unlike Identity/Product/Cart/Order/Notification, the Angular UI is not a Clean Architecture service — it's a single-page app with its own idioms (standalone components, signals, NgRx for the state that genuinely needs it). This page covers the module tree, the auth/token flow, and the state-management split. For the phase narrative — hazards found, live verification, Docker setup — see [Phase7.md](../Phases/Phase7.md) and [Phase7-Plan.md](../Phases/Phase7-Plan.md).

---

## 1. Module Tree

```text
src/app/
├── core/
│   ├── auth/            TokenStore · Auth · jwt.util · guards · interceptors · store/ (NgRx slice)
│   ├── cart/            CartService · store/ (NgRx slice, @ngrx/entity)
│   ├── services/        product.ts · category.ts · order.ts · user.ts — plain HttpClient, no NgRx
│   └── http-error.util  shared error-message extraction (same middleware shape on every service)
├── customer/            catalog (anon) · cart (auth) · orders (auth + checkout/confirm flow)
├── vendor/               dashboard (client-computed stub) · products (CRUD)
├── admin/               users (search/role/reset) · orders (read-only) · categories (create)
├── login/, register/    lazy-loaded (loadComponent) — see §3
└── shared/components/    navbar · verify-email-banner · coming-soon
```

`customer/`, `vendor/`, `admin/` are lazy-loaded route groups (`loadChildren`) — a customer session never downloads the vendor or admin bundles. `vendor/` and `admin/` each carry a single `canActivate: [authGuard, roleGuard(...)]` on their parent route in `app.routes.ts`; children inherit it. `customer/`'s children are individually guarded (`catalog` is anonymous, matching `GET /api/products`; `cart`/`checkout`/`orders` require `authGuard`).

---

## 2. Why Only `auth` and `cart` Are NgRx

```text
                    read by guards, interceptors,        mutated from more
                    AND the navbar simultaneously?        than one place?
                            │                                    │
   auth  ──────────────────┼──────────── yes ──────────────────┼──── yes ──► NgRx
   cart  ──────────────────┼──────────── yes ──────────────────┼──── yes ──► NgRx
                            │  (navbar shows user + role)        │  (catalog "Add to Cart"
                            │                                    │   AND the cart page itself)
   catalog ────────────────┼──────────── no ───────────────────┼──────────► plain HttpClient + signals
   orders  ────────────────┼──────────── no ───────────────────┼──────────► plain HttpClient + signals
   vendor CRUD ────────────┼──────────── no ───────────────────┼──────────► plain HttpClient + signals
   admin lists ────────────┼──────────── no ───────────────────┼──────────► plain HttpClient + signals
```

Everything in the second group is read and written from exactly one place (a single page component), so a reducer/effects/selector triple would just add indirection around what a service + signal already does directly. `auth` and `cart` are read from multiple, unrelated places at once (guards deciding navigation, interceptors deciding whether to attach a header, the navbar rendering role-specific links and a live item count) — that's the actual justification for NgRx's overhead, not "state management" as a blanket rule.

---

## 3. Auth & Token Flow

```text
Login/Register component
        │ dispatch(AuthActions.login/register)
        ▼
AuthEffects.login$/register$ ──► Auth.login()/register() ──► POST /api/auth/{login,register}
        │                                                          │
        │                              TokenStore.setTokens():     │
        │                              access token → memory       │
        │                              refresh token → sessionStorage
        ▼
AuthActions.loginSuccess({ user }) ──► authReducer ──► navbar, guards, checkout all react

Every subsequent HttpClient request
        │
        ▼
jwtInterceptor ── attaches "Authorization: Bearer <access token>" from TokenStore
        │                              (skipped for /api/auth/{login,register,refresh})
        ▼
tokenRefreshInterceptor ── passes through on success
        │
        └─ on 401 ──► TokenRefreshGate.refresh()
                            │
                            │  singleton, shareReplay(1) — N concurrent 401s
                            │  still trigger exactly ONE refresh call, because
                            │  the refresh token is server-side single-use/rotated
                            ▼
                       Auth.refresh() ──► POST /api/auth/refresh { token: <refresh token> }
                            │
                  success ──┴── failure
                    │              │
          retry original    TokenStore.clear() + navigate to /login
          request with the
          fresh access token
```

**App start** (`provideAppInitializer(restoreSessionOnInit)` in `app.config.ts`, logic in `core/auth/restore-session-on-init.ts`): dispatches `AuthActions.restoreSession()`, which calls `Auth.tryRestoreSession()` — if a refresh token survived in `sessionStorage` (i.e. this is a page reload, not a fresh tab), it's silently exchanged for a new access token *before the router activates any route*. This is what makes the `sessionStorage`-refresh-token deviation (Decision #1 in `Phase7-Plan.md`) actually deliver on its promise — without this initializer, a reload would still force re-login despite the token surviving.

**Subscribe before you dispatch, not after — this one shipped a permanently blank screen.** `restoreSessionOnInit()` must create its `firstValueFrom(actions$.pipe(ofType(...)))` subscription *before* calling `store.dispatch(...)`. On a fresh browser with no refresh token, the effect's response is dispatched *synchronously* within the `dispatch()` call itself (`of(null)` emits immediately) — subscribing afterward means the response already fired and is gone (`Actions` is a hot `Subject`, it never replays), so the initializer's promise never resolves and Angular's bootstrap hangs forever with no console output and no thrown error. See [Phase7.md](../Phases/Phase7.md) for how this was found (only by loading the app in a real browser — curl-based verification is structurally incapable of catching a client-side JS timing bug) and the regression test that now guards it.

**Verify-email is a trap for the unwary**: `POST /api/auth/verify-email` flips the DB flag, but the *current* JWT was already signed and is immutable — it still carries the old `emailVerified` claim until a new token is issued. `AuthEffects.refreshAfterVerifyEmail$` calls `Auth.refresh()` immediately after a successful verification specifically to re-issue a token with the updated claim; skipping this step would leave the UI showing "verified" while the Gateway kept 403ing every order placement.

---

## 4. Role-Gated Routing

| Route prefix | Guard | Matches |
| --- | --- | --- |
| `customer/catalog` | none | `GET /api/products` (anonymous) |
| `customer/{cart,checkout,orders}` | `authGuard` | any authenticated role — the API only requires `[Authorize]`, not a specific role, for cart/orders |
| `vendor/*` | `authGuard` + `roleGuard('Vendor')` | mirrors `RequireVendor` policy |
| `admin/*` | `authGuard` + `roleGuard('Admin')` | mirrors `RequireAdmin` policy |

`authGuard` checks `TokenStore.getAccessToken()` synchronously (populated by the app-initializer before routing starts). `roleGuard(role)` additionally decodes the current token's role claim. Both redirect to `/login` on failure via `Router.createUrlTree(...)`, not a hard navigation — preserving the attempted URL isn't implemented (out of scope), but the redirect itself doesn't lose app state.

---

For the full hazard list (concurrent-401 dedupe, the two-step order-placement flow, the stale-JWT-after-verify-email trap, the soft-delete visibility split, the `isEmailVerified`/`emailVerified` naming mismatch) with live-verification evidence for each, see [Phase7.md](../Phases/Phase7.md).
