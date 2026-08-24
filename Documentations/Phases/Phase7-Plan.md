# Phase 7 — Angular UI: Implementation Plan

## Context

ShopFlow follows a 7-phase build order (`Documentations/ShopFlow-Approach.md`, tracked live in `Documentations/STATUS.md`). Phases 1–6 (Infrastructure, Identity, Product, Cart, Order + Notification, API Gateway) are complete — 313 backend tests passing, plus a full Postman/Newman run through the gateway (`Documentations/Phases/Phase6.md`). Phase 7 is the **Angular UI**, currently pending: `ClientApp/` is an empty directory and its `docker-compose.yml` block exists only as a comment. STATUS.md's stated blocker — "all API endpoints stable" — is satisfied.

Three planning docs prescribe the shape of this phase: `README.md` (tech stack table, port table), `Documentations/ShopFlow-ProjectSpec.md` §"Angular UI" (module tree, auth flow), and `Documentations/ShopFlow-Approach.md` "Phase 7" (build order: `core/auth` → `customer/{catalog,cart,orders}` → `vendor/products` → `admin/users`). All agree on: Angular 17+, Angular Material, NgRx, a single SPA for all 3 roles (customer/vendor/admin) rather than separate apps, talking exclusively through the Gateway (`http://localhost:5005`) — never a service directly.

## Pre-work found: a hard blocker not mentioned in any planning doc

**CORS is completely unconfigured anywhere in the codebase.** Grepped Gateway + all 5 services for `AddCors`/`UseCors`/`AllowAnyOrigin` — zero hits. Every browser call Angular makes to the Gateway will fail CORS preflight until this is fixed. This must be step zero of Sub-phase 7.0, done once at the Gateway (the only service Angular ever talks to) rather than per-service.

## Decisions confirmed with the user before writing this plan

| # | Question | Resolution |
|---|---|---|
| 1 | Spec says auth tokens live in memory only (not `localStorage`), to limit XSS exposure. Pure in-memory means a browser reload forces re-login on every role — real UX cost for an SPA. | **Access token in memory; refresh token in `sessionStorage`.** User explicitly chose this over the spec-literal "memory only," trading a small, deliberately-accepted security margin for not forcing re-login on reload. Documented here as a known deviation, not a bug to "fix" later. |
| 2 | Spec's `vendor/dashboard` implies a sales/revenue view, but no backend route exposes vendor-scoped order/revenue data — only `GET /api/vendors/{id}/products` is vendor-scoped. Building it for real means adding a new Order Service endpoint, which is backend scope creep in a phase whose premise is "all API endpoints stable." | **Ship as a client-computed stub** — listing count, stock value, low-stock count, derived only from the vendor's own product list. No backend changes beyond the CORS fix. Documented as a known limitation. |
| 3 | Where is NgRx actually justified, vs. plain services, in a portfolio-scale app? | **Exactly two slices: `auth` and `cart`** — the only states that are both genuinely global (read by guards/interceptors/navbar) and multi-writer (cart is mutated from both the catalog page and the cart page). Catalog, vendor CRUD, and admin lists use plain `HttpClient` + signals; introducing NgRx there would be cargo-culted, not earned. |
| 4 | NgModules vs. standalone components? | **Standalone, no NgModules.** Angular 17+ defaults to standalone, and the spec's module tree is already folder-based rather than NgModule-based — directly compatible. Functional guards (`CanActivateFn`) and functional interceptors (`HttpInterceptorFn`); lazy `loadChildren` per role so a customer session never downloads vendor/admin bundles. |
| 5 | Dev (`ng serve`) vs. Docker (`docker-compose`) API wiring — does nginx reverse-proxy `/api` in the container, matching the dev proxy? | **No.** Dev uses `proxy.conf.json` (browser same-origin to `:4200`, forwarded to `:5005`). Docker's nginx serves static files only; `environment.prod.ts` points straight at `http://localhost:5005`. A templated reverse-proxy in nginx would need to know the Gateway's address at container-build time either way, so it buys nothing once CORS is fixed at the real origin — and the CORS fix is required regardless, since the Docker path has no proxy to mask its absence. |

## Verified backend API surface (read directly from the Gateway's `ocelot.json` and each controller, matching Phase 6's own verified-route-inventory precedent)

| Service | Route | Method(s) | Auth |
|---|---|---|---|
| Identity | `/api/auth/register`, `/login`, `/refresh` | POST | none |
| Identity | `/api/auth/logout`, `/verify-email` | POST | Bearer |
| Identity | `/api/users/me` | GET | Bearer |
| Identity | `/api/admin/users` | GET | Bearer + Admin |
| Identity | `/api/admin/users/{id}/assign-role`, `/reset-password` | POST | Bearer + Admin |
| Product | `/api/products`, `/api/products/{id}` | GET | none |
| Product | `/api/products`, `/api/products/{id}` | POST/PUT/DELETE | Bearer + Vendor |
| Product | `/api/categories` | GET | none |
| Product | `/api/categories` | POST | Bearer + Admin |
| Product | `/api/vendors/{id}/products` | GET | Bearer + Vendor (+ ownership) |
| Cart | `/api/cart`, `/api/cart/items[/{productId}]` | GET/POST/PUT/DELETE | Bearer |
| Order | `/api/orders` | POST | Bearer + `emailVerified` claim |
| Order | `/api/orders`, `/api/orders/{id}` | GET | Bearer |
| Order | `/api/orders/{id}/confirm` | PUT | Bearer |
| Order | `/api/admin/orders` | GET | Bearer + Admin |
| Notification | — | — | no REST surface at all — pure RabbitMQ consumer, nothing for Angular to call |

Auth token mechanics (from `AuthController`/`TokenService`): login/refresh return `{jwt, refreshToken, email, displayName, role}`. Access JWT is HMAC-SHA256, 60min expiry, claims `userId`/`email`/`role`/`emailVerified` (lowercase string). Refresh token is opaque, server-side stored, 7-day expiry, single-use/rotated, sent as a JSON body field (`{"token": "..."}`) — never a cookie, never a header. This is why Decision #4's CORS policy omits `AllowCredentials()`.

## Step-by-step plan

### Sub-phase 7.0 — Scaffold, CORS fix, app shell

CORS fix in `Gateway/Gateway.Api/Program.cs` — add a named policy (`AngularUi`) reading `Cors:AllowedOrigins` (default `["http://localhost:4200"]`), `WithOrigins().AllowAnyHeader().AllowAnyMethod()`, no `AllowCredentials()`. Insert `app.UseCors(...)` before `app.UseAuthentication()` (confirmed current order: `UseSerilogRequestLogging()` → `UseAuthentication()`) so CORS preflight `OPTIONS` isn't rejected by the JWT bearer middleware. Add `Cors__AllowedOrigins__0=http://localhost:4200` to the `gateway` service in `docker-compose.yml`, matching the existing `JwtSettings__*` env-var pattern.

Then scaffold `ClientApp/`:
```bash
npx @angular/cli@latest new shopflow-ui --directory=. --routing --style=scss --strict --skip-git --package-manager=npm
ng add @angular/material --theme=azure-blue --typography=true --animations=enabled
npm install @ngrx/store @ngrx/effects @ngrx/store-devtools @ngrx/entity
```
Verify resolved CLI version is ≥17 before proceeding (Phase 6 precedent: verify live, don't assume). `proxy.conf.json` (`/api` → `http://localhost:5005`) wired into `angular.json`. `environment.ts` (`apiBaseUrl: ''`) / `environment.prod.ts` (`apiBaseUrl: 'http://localhost:5005'`). Root `app.routes.ts` with `login`/`register` + lazy `loadChildren` for `customer/`, `vendor/`, `admin/`. Minimal `shared/components/navbar/`.

**Verify:** `ng serve` at `localhost:4200`; browser console `fetch('/api/products')` (via proxy) and a direct `fetch('http://localhost:5005/api/products')` (bypassing the proxy, simulating the Docker topology) both succeed with no CORS error.

### Sub-phase 7.1 — core/auth

`token-store.service.ts` (access token in memory, refresh token in `sessionStorage` per Decision #1), `auth.service.ts` (wraps register/login/refresh/logout/verify-email, decodes JWT claims), `jwt.interceptor.ts` (attaches Bearer, skips anon auth endpoints), `token-refresh.interceptor.ts` (on 401, refresh-and-retry once — **must dedupe concurrent 401s to exactly one refresh call** via a `refreshInProgress$` gate, since the refresh token is single-use/rotated and N parallel refreshes would fail N-1 of them), `auth.guard.ts`/`role.guard.ts` (functional), `store/auth.*` (first NgRx slice per Decision #3), `login/`/`register/` components.

**Verify:** register → login against the real Gateway → Bearer header visible on `GET /api/users/me` → guard blocks a customer from a vendor/admin route → force an expired token, confirm transparent refresh-and-retry → fire several parallel authenticated calls with an expired token, confirm exactly one `/api/auth/refresh` call fires.

### Sub-phase 7.2 — customer/catalog — done

`core/services/product.ts`, `category.ts` (shared with vendor/admin later). `customer/catalog/` — filterable product list, product detail. Plain services + signals, no NgRx.

**Correction found during implementation, not knowable from the spec:** `GET /api/products` has no server-side category filter (`GetProductListQuery` takes no parameters at all — confirmed by reading the query/handler). Category filtering is done client-side in `CatalogList` via a computed signal over the full product list, not a query param.

Also relocated `extractErrorMessage` (originally written auth-only) to `core/http-error.util.ts` — Identity's and Product's `ExceptionHandlingMiddleware` use the identical `{message}` / `{errors}` shape, confirmed by reading both, so it's a shared concern from here on, not an auth-specific one.

**Verified:** browsed with zero login (proves the anon route works through the fixed-CORS Gateway); category filter/name-lookup checked against real category ids from the running `product-service`; a real 404 confirmed the `{message}` error shape end-to-end.

### Sub-phase 7.3 — customer/cart — done

`core/cart/store/*` (via `@ngrx/entity`, second and last NgRx slice per Decision #3). `customer/cart/cart-page` — cart view, quantity controls; navbar badge subscribes to the same store; "Add to Cart" wired into 7.2's `catalog-list`/`catalog-detail`, gated on `selectAuthUser` (shown only when logged in).

**Real API contract details found by reading `Cart.Api`, not guessable from the route table alone:**
- `AddCartItemRequest` requires `productName`/`unitPrice` from the client — Cart has no SQL/domain entity (Redis-backed, denormalized), so it never looks products up itself. The catalog pages already have this data loaded, so it's a natural fit, not extra plumbing.
- Adding an already-present product **increments its quantity server-side** rather than erroring or replacing — confirmed live (`POST` qty 1, then qty 2 on the same product → server returns qty 3).
- `UpdateCartItemCommandValidator` rejects `quantity < 1` (400, "use the delete endpoint instead") — confirmed live. `CartPage.updateQuantity` routes anything below 1 to `removeItem` instead of ever sending an invalid `PUT`.

**Correctness hazard, caught before it shipped, not found by testing:** logging out must dispatch a **local-only** `CartActions.resetState()`, never `CartActions.clearCart()` — the latter calls `DELETE /api/cart`, which would silently empty the user's real saved cart just because they logged out. `CartEffects.resetOnLogout$` and a dedicated test cover this distinction.

**Verified live:** merge-on-add (qty 1 + qty 2 → qty 3), the qty-0 rejection and its `{errors:[...]}` shape via `extractErrorMessage`, quantity update, item removal, and an empty cart afterward — all against the real `cart-service`, not mocks.

### Sub-phase 7.4 — customer/orders — done

`core/services/order.ts`; `customer/orders/checkout`, `order-history`, `order-detail`.

**Major hazard found and fixed, confirmed live, not guessable from the route table:** `PlaceOrderCommand` only creates the order in `Pending` status — `OrderPlacedEvent` (the thing that clears the cart and triggers the confirmation email) is published **only from `Confirm()`**, not from initial placement. So checkout is a two-step flow: `POST /api/orders` (Pending) → `PUT /api/orders/{id}/confirm` (Confirmed, cart cleared async via RabbitMQ). `Checkout` places the order and navigates to `OrderDetail`, which shows a "Confirm Order" button while `status === 'Pending'`.

**A second, more subtle hazard, proven live with curl before trusting the fix:** `POST /api/auth/verify-email` flips the DB flag, but a JWT is immutable once signed — the *current* access token still carries `emailVerified: "false"` until a new one is issued. Verified this exact sequence against the real backend: (1) unverified checkout → 403; (2) call verify-email with the old token → 200 OK; (3) retry checkout with the *same* old token → **still 403**; (4) call `/api/auth/refresh` → new token has `emailVerified: "true"` (confirmed by decoding it) because `RefreshTokenCommandHandler` re-fetches the user from the DB; (5) retry checkout with the new token → 201. Fixed by adding `AuthEffects.refreshAfterVerifyEmail$`, which calls `auth.refresh()` immediately after `verifyEmailSuccess` and folds the result into `restoreSessionSuccess` — without this, the UI would show "email verified" (the reducer's optimistic update) while every subsequent order attempt kept 403ing with no visible reason.

**Third finding:** Ocelot's own `RouteClaimsRequirement` rejection returns a `403` with an **empty body** (`content-length: 0`, confirmed live) — so `Checkout` detects this case purely by `HttpErrorResponse.status === 403`, not by trying to parse a message out of it (there isn't one).

The site-wide `VerifyEmailBanner` (`shared/components/`) is a one-click "Verify email now" action, not a "resend the email" one — the backend has no real send-a-link flow, `POST /api/auth/verify-email` just marks the currently-authenticated account verified directly.

**Verified live end-to-end**, not mocks: the full stale-claim sequence above, order creation, confirm (status Pending→Confirmed), and cart clearing via the async `OrderPlacedEvent` → RabbitMQ → Cart's `OrderPlacedConsumer` (confirmed `GET /api/cart` returns `[]` after confirm, not just asserted from reading the consumer's test).

### Sub-phase 7.5 — vendor/products (+ vendor/dashboard stub) — done

`vendor/products/{vendor-product-list, vendor-product-form}`, reusing `category.ts` and `product.ts` from 7.2 (extended with `getByVendorId`). `vendor/dashboard/vendor-dashboard` — client-computed stub per Decision #2, built only from `GET /api/vendors/{id}/products`.

**Finding confirmed by reading the handler, not guessable from the route table:** `DELETE /api/products/{id}` is a **soft delete** — `DeleteProductCommandHandler` calls `product.Deactivate()`, not a real delete, and there is no reactivate endpoint. Worse: `GetVendorProductsQueryHandler` (unlike the public catalog's `GetAllActiveAsync`) does **not** filter by `isActive` — a deactivated product vanishes from the public catalog but stays visible in the vendor's own product list forever, just flagged inactive. `VendorProductList` shows an Active/Inactive chip and disables "Deactivate" once already inactive; the button reads "Deactivate," not "Delete," to match reality.

**Verified live**, using the seeded dev admin (`admin@shopflow.com`, per `Identity.Api`'s `AdminSeed` config) to promote two fresh registrations to Vendor via `POST /api/admin/users/{id}/assign-role` — confirming yet again that a role change needs a fresh login/token, the same immutable-JWT lesson as 7.4. With two real vendor accounts: Vendor A creates a product; Vendor A viewing Vendor B's `GET /api/vendors/{id}/products` gets a `403` with an **empty body** (same `Forbid()` pattern as Ocelot's own rejection); Vendor B attempting to `PUT`/update Vendor A's product gets a `404` (`UpdateProductCommandHandler`'s ownership check throws `NotFoundException`, not `403` — indistinguishable from "doesn't exist," a defensible anti-enumeration choice); deactivating a product removes it from the public catalog immediately while staying visible (as `isActive: false`) in the vendor's own list.

### Sub-phase 7.6 — admin/users (+ admin/orders, admin/categories) — done

`admin/users/admin-users` — search, role assignment (`mat-select` per row), inline reset-password mini-form. `admin/orders/admin-orders` — read-only cross-customer list (`OrderService.getAllOrders()`, added alongside 7.4's customer-scoped methods). `admin/categories/admin-categories` — creation form + list, reusing `CategoryService` from 7.2. These last two extend beyond the spec's illustrative `admin/users`-only tree, to cover real routes that need a home somewhere.

**Naming detail confirmed by reading `UserProfileDto`, not guessable:** the JSON key is `isEmailVerified` (from `IsEmailVerified`), *not* `emailVerified` like the JWT claim — two different casings for conceptually the same flag, coming from two different serialization paths (JWT claims vs. a DTO record). `user.models.ts` deliberately keeps this distinct from `auth.models.ts`'s `AuthUser.emailVerified` rather than normalizing them, so a reader isn't misled into thinking they're the same wire format.

**Verified live** with the seeded dev admin: listed all 40 real users; created a category and confirmed it appears in `GET /api/categories` immediately — proving `CategoryService` is genuinely shared across catalog (7.2), vendor form (7.5), and here, not duplicated; assigned a fresh registration's role to Vendor and reset its password, then confirmed the old password stopped working and the new one logged in; confirmed a non-admin customer token gets a `403` (empty body, Ocelot-level, same pattern as every other role-gated route) on both `GET /api/admin/users` and `POST /api/categories`.

### Sub-phase 7.7 — Docker + docs

`ClientApp/Dockerfile` (multi-stage: `node:20-alpine` build → `nginx:alpine` serve, static-only per Decision #5) + `nginx.conf` (`try_files $uri $uri/ /index.html;`). Complete the `angular-ui` block in `docker-compose.yml` (build `./ClientApp`, port `4200:80`, `depends_on: gateway: condition: service_healthy`). `.github/workflows/angular.yml` (separate from the .NET workflow): `npm ci`, `ng build`, `ng test --watch=false --browsers=ChromeHeadless`.

Docs:
- `Documentations/Phases/Phase7.md` (post-implementation write-up, following this plan / the Phase6.md template) — cover the CORS prerequisite, the sessionStorage token-storage deviation, the vendor-dashboard stub limitation, the concurrent-401-refresh-dedupe hazard, live verification log.
- Optionally `Documentations/Architecture/Angular-UI.md`, for parity with the per-service architecture docs.
- Flip Phase 7 to ✅ in `Documentations/STATUS.md`, update "Immediate next steps."

**Verify:** `docker compose up -d --build` the full stack including `angular-ui`; walk all three roles end-to-end against the containerized Gateway with no `ng serve` involved; confirm a page reload keeps the session alive (refresh token in `sessionStorage`) without forcing re-login.

## Testing scope

**Toolchain correction (found in Sub-phase 7.0, not known when this plan was written): Angular 21's default `ng test` builder (`@angular/build:unit-test`) runs on Vitest, not Karma/Jasmine.** No extra setup needed — `ng test` works out of the box; do not add Karma. Reducers: pure-function unit tests (closest analogue to `Domain.Tests`). Effects: `provideMockActions` + mocked services (closest to `Application.Tests`' mocking style). Services: `provideHttpClientTesting()`, asserting request shape. Guards: mocked `Router`/`AuthService`. Interceptors: `HttpTestingController`, explicitly covering the concurrent-401-dedupe path from 7.1. Components: shallow `TestBed` tests only where there's real logic (form validation), not exhaustive presentation-component coverage. E2E is an explicit stretch goal, not required — if pursued, one Playwright critical-path smoke test (register→verify→login→browse→cart→checkout→confirm), matching Phase 6's own pragmatic-depth precedent (Postman/Newman instead of a dedicated test project) rather than uniform maximal coverage.

## Critical files

- `Gateway/Gateway.Api/Program.cs` — CORS insertion point (current order: `UseSerilogRequestLogging()` at line 64, `UseAuthentication()` at line 66); the hard prerequisite for every later sub-phase.
- `docker-compose.yml` — `angular-ui` block (commented out at lines 236-242) to complete in 7.7; `gateway` service env vars (lines 66-70) to extend with `Cors__AllowedOrigins__0`.
- `Documentations/ShopFlow-ProjectSpec.md` §"Angular UI" (~line 598) — the module tree and auth-flow steps this plan implements, and the two places (vendor dashboard, admin/orders+categories) it extends beyond the illustrative tree.
- `Documentations/Phases/Phase6-Plan.md` / `Phase6.md` — the documentation format Phase 7's write-up must match.
- `ClientApp/` — currently empty; the target for every sub-phase.
