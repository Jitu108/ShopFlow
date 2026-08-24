# Phase 7 — Angular UI

## Project Structure

```text
ClientApp/
├── Dockerfile                          multi-stage: node:20-alpine build → nginx:alpine serve
├── nginx.conf                          static files only, SPA fallback via try_files
├── proxy.conf.json                     dev-only: /api → localhost:5005
├── src/
│   ├── environments/{environment,environment.prod}.ts
│   └── app/
│       ├── core/
│       │   ├── auth/                   TokenStore, Auth, jwt.util, guards, interceptors, store/ (NgRx slice #1)
│       │   ├── cart/                   CartService, store/ (NgRx slice #2, @ngrx/entity)
│       │   ├── services/               product.ts, category.ts, order.ts, user.ts (plain HttpClient, no NgRx)
│       │   └── http-error.util.ts      shared across all services — same ExceptionHandlingMiddleware shape everywhere
│       ├── customer/{catalog, cart, orders}/
│       ├── vendor/{dashboard, products}/
│       ├── admin/{users, orders, categories}/
│       ├── login/, register/
│       └── shared/components/{navbar, verify-email-banner, coming-soon}
```

Standalone components throughout (no NgModules) — Angular 21's default, and directly compatible with the spec's folder-based module tree. `customer/`, `vendor/`, `admin/` are lazy-loaded route groups (`loadChildren`); `login`/`register` are lazy-loaded components (`loadComponent`) after an early bundle-budget regression showed why.

## Pre-work: the CORS gap

Grepped the Gateway and all 5 services before writing any Angular code: zero hits on `AddCors`/`UseCors`/`AllowAnyOrigin`. Every browser call from Angular to the Gateway would have failed CORS preflight. Fixed once, at the Gateway (`Gateway/Gateway.Api/Program.cs`) — the only service Angular ever talks to — with a config-driven allowlist (`Cors:AllowedOrigins`), not `AllowAnyOrigin`, and no `AllowCredentials()` since the refresh token travels as a JSON body field, never a cookie. Verified live with curl before writing any Angular code: preflight `OPTIONS` → `204` with correct headers; a disallowed `Origin` gets no `Access-Control-Allow-Origin` header at all.

## Decisions confirmed before implementation (see `Phase7-Plan.md`)

1. **Token storage** — access token in memory, refresh token in `sessionStorage` (user's explicit choice, a deliberate deviation from the spec's literal "memory only," trading a small security margin for not forcing re-login on every reload).
2. **NgRx scoped to exactly two slices: `auth` and `cart`** — the only states that are both genuinely global and multi-writer. Everything else (catalog, orders, vendor CRUD, admin lists) is plain `HttpClient` + signals.
3. **Standalone components**, functional guards/interceptors, lazy-loaded per-role route groups.
4. **CORS fix at the Gateway only**, config-driven, no `AllowCredentials()`.
5. **Dev via `ng serve` + `proxy.conf.json`; Docker via nginx serving static files only, no reverse proxy** — `environment.prod.ts` points straight at the Gateway.
6. **`vendor/dashboard` ships as a client-computed stub** (listing counts, stock value, low-stock count) — no backend route exposes vendor-scoped revenue data, and adding one would be backend scope creep in a UI-only phase.

## Toolchain findings (not knowable from the spec, found by actually running the tooling)

- Node 21.5 (ambient at the start of this phase) is unsupported by current Angular tooling — Angular only supports even-numbered LTS lines. Development used Node 22 LTS via `nvm`; the Docker image and CI both pin **Node 20 LTS** (matching the README's stated "Node.js 20+" and independently confirmed to build and test cleanly).
- Angular CLI's `@latest` resolved to **Angular 22**, but NgRx's latest stable release (`21.1.1`) only supports up to Angular 21 (NgRx 22 exists only as a release candidate). The scaffold is pinned to **Angular 21.2** rather than adopting a pre-release NgRx.
- Angular 21's default `ng test` builder runs on **Vitest**, not Karma/Jasmine as assumed when the phase was planned — no extra setup needed, and `--browsers=ChromeHeadless` (a Karma-era flag) does not apply; CI uses `ng test --watch=false`.

## Correctness hazards found and fixed, by sub-phase

**The most serious one, found only by actually loading the app in a browser — every curl-based check in this entire phase missed it:** the `provideAppInitializer` in `app.config.ts` dispatched `AuthActions.restoreSession()` and only *then* subscribed to its response via `firstValueFrom(actions$.pipe(ofType(restoreSessionSuccess, restoreSessionFailure), take(1)))`. On a fresh browser with no `sessionStorage` refresh token — i.e. every first-ever visit — `Auth.tryRestoreSession()` resolves *synchronously* (`of(null)` emits immediately on subscription), so the whole round trip (dispatch → effect → `restoreSessionFailure` dispatched) completed before the initializer's own subscription was even created. `Actions` is a hot `Subject` — it never replays to a late subscriber — so that response was missed forever, `firstValueFrom`'s promise never resolved, and Angular's bootstrap hung indefinitely with **zero console output and zero thrown exceptions**: a permanently blank page, `document.readyState === "complete"`, `<app-root>` with 0 children, no error anywhere. Confirmed live in both the production Docker build and a plain `ng serve` dev build via Chrome's DevTools Protocol (headless Chrome, `Runtime.evaluate`/`Runtime.exceptionThrown`/`Network.responseReceived` capture) — nothing short of actually executing the JS in a real browser could have surfaced this, since curl never runs JavaScript at all. **Fixed** by reordering: subscribe first, dispatch second (now `core/auth/restore-session-on-init.ts`, extracted out of `app.config.ts` specifically so it's unit-testable). A regression test (`restore-session-on-init.spec.ts`) reproduces the exact synchronous, no-refresh-token path with a 1-second race against a rejection, and was verified to actually fail against the old (buggy) ordering before being trusted.

**Methodology gap this exposes:** every other hazard in this phase was caught because curl-based verification against the real backend is a good discipline — but it is fundamentally incapable of catching a client-side JS timing bug like this one, since curl never executes JavaScript. This bug shipped through all of 7.0–7.7's "verified live" checks undetected. A real-browser smoke test (even a manual "open it and look" — this one didn't even need automation, a human loading the page once would have seen the blank screen immediately) should be part of the standard verification loop for any UI phase, not just an afterthought once Docker is wired up.

Every hazard below this one was found by actually exercising the real backend with curl before trusting the design, not by reading docs or guessing:

1. **(7.1) Concurrent-401 refresh storm.** The refresh token is server-side single-use/rotated (confirmed live: reusing one after a successful refresh returns `401`) — if N requests 401 concurrently, only one refresh call may fire, or N−1 of them fail outright. `TokenRefreshGate` (a `providedIn: 'root'` singleton sharing one in-flight `Observable` via `shareReplay(1)`) fixes this; covered by an `HttpTestingController` test firing 3 concurrent 401s and asserting exactly one refresh call.
2. **(7.1) JWT claims serialize under full `ClaimTypes` URIs**, not plain keys (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress`, not `"email"`) — confirmed by decoding a real token from a live register call.
3. **(7.4) Checkout is two steps, not one.** `POST /api/orders` only creates a `Pending` order; `OrderPlacedEvent` (cart-clearing, confirmation email) fires only from `PUT /api/orders/{id}/confirm`.
4. **(7.4) JWTs are immutable — verifying email doesn't fix the *current* token.** Proven live: verify-email → `200`, then an immediate retry with the *same* old token → still `403`. Only `/api/auth/refresh` (which re-reads the user from the DB) produces a token with the updated claim. Fixed with `AuthEffects.refreshAfterVerifyEmail$`.
5. **(7.5) `DELETE /api/products/{id}` is a soft delete** with no reactivate endpoint, and the vendor's own product list — unlike the public catalog query — does not filter by `isActive`, so a deactivated product stays visible (flagged inactive) to its owner forever.
6. **(7.6) `UserProfileDto`'s JSON key is `isEmailVerified`**, not `emailVerified` like the JWT claim — same concept, two different casings from two different serialization paths.

Two smaller findings worth recording:

- **(7.2) `GET /api/products` has no server-side category filter** — `GetProductListQuery` takes zero parameters. Filtering is client-side.
- **(7.1) An uncaught `router.navigateByUrl()`/`navigate()` to an unregistered route throws `NG04002` as an unhandled rejection** in any test harness — surfaced only by running the interceptor and checkout tests for real, fixed by registering matching stub routes in those specs.

## Live Verification

Every sub-phase was verified against the real running backend stack via curl (not mocks) before being considered done. Summary, in order:

- **7.0** — CORS: preflight `204` with correct headers; disallowed origin gets no CORS header; both the dev-proxy path and the direct-to-Gateway path (simulating Docker) work.
- **7.1** — Real register/login round trip; JWT claim shapes match the decoder exactly; refresh-token single-use/rotation confirmed (second use of the same token → `401`).
- **7.2** — Anonymous catalog browsing works through the fixed-CORS Gateway with zero login; a real `404` confirmed the shared `{message}` error shape.
- **7.3** — Adding the same product twice merges to a server-computed total quantity (1+2→3); reducing quantity below 1 is rejected with the `{errors:[...]}` shape; removal and clearing both verified.
- **7.4** — The full stale-claim sequence (unverified → `403`; verify with old token → still `403`; refresh → new token has the claim → `201`); order confirm transitions `Pending`→`Confirmed`; `GET /api/cart` genuinely returns `[]` afterward (async `OrderPlacedEvent` → RabbitMQ → Cart's `OrderPlacedConsumer`).
- **7.5** — Two real vendor accounts (promoted via the seeded dev admin): cross-vendor product-list access → `403` empty body; cross-vendor update → `404`; deactivating removes a product from the public catalog while it stays visible (inactive) to its owner.
- **7.6** — Listed all real users; created a category and confirmed it's immediately visible via a plain `GET /api/categories` call (proves `CategoryService` is genuinely shared across catalog/vendor/admin); full role-assign + password-reset round trip; non-admin gets `403` on both admin-only routes.
- **7.7** — `docker compose up -d angular-ui` (the full transitive dependency chain: `angular-ui` → `gateway` → `{identity,product,order,cart}` → `{sqlserver,redis,rabbitmq}`) brings up a healthy container; the served bundle's baked-in API URL is confirmed to be `http://localhost:5005` (not the dev proxy); a request from `Origin: http://localhost:4200` to the containerized Gateway at `:5005` succeeds with the correct CORS header — the exact production topology, verified end-to-end.

One transient hiccup during 7.7, not a Phase 7 bug: rebuilding six service images simultaneously briefly stressed the long-running shared `sqlserver`/`rabbitmq` containers, causing one `docker compose up` invocation to abort on a dependency health check. Confirmed both containers' healthchecks were passing again moments later (`docker inspect`), and a retry succeeded cleanly.

**Real-browser check, added after 7.7's initial "done":** loaded the running `angular-ui` container in actual Chrome (headless, via the DevTools Protocol — `Runtime.evaluate` to inspect `document.readyState`/`<app-root>`'s child count, `Runtime.exceptionThrown`/`Log.entryAdded`/`Network.responseReceived` to capture console output and network activity) rather than trusting curl-only verification. This is what caught the app-initializer hang described above — the page loaded to `readyState: "complete"` with an empty `<app-root>` and no console output at all. After the fix: `appRootChildren: 3`, and `document.body.innerText` showing the real rendered catalog (product names, prices, stock, category filter) fetched live from the running `product-service`. Also noted a Docker-environment quirk unrelated to the app itself: `docker compose up --build` occasionally resolves its `buildx bake` step extremely slowly (once ~20 minutes, once >25 minutes before being cancelled) in this environment, while a direct `docker build` of the same Dockerfile with the same layer cache completes in under a minute — when a compose build hangs, building directly and letting compose pick up the resulting image (`docker compose up -d --no-build <service>`) is the reliable workaround.

**Test suite:** 65 unit tests (Vitest), covering reducers, effects, guards, interceptors, components with real branching logic, and a regression test for the app-initializer hang above (verified to actually fail against the old buggy ordering before being trusted).

## npm Packages

| Package | Version | Notes |
| --- | --- | --- |
| `@angular/core` (+ common, forms, router, compiler, platform-browser) | 21.2.0 | Pinned below CLI's `@latest` (22) — see toolchain findings |
| `@angular/material`, `@angular/cdk` | 21.2.14 | Material 3, azure-blue theme |
| `@ngrx/store`, `@ngrx/effects`, `@ngrx/entity`, `@ngrx/store-devtools` | 21.1.1 | Latest stable compatible with Angular 21 |
| `vitest`, `jsdom` | 4.0.8 / 28.0.0 | Angular 21's default `ng test` runner |

## docker-compose.yml

`angular-ui` block uncommented and completed: `build: ./ClientApp`, `ports: "4200:80"`, `depends_on: gateway: condition: service_healthy`, plus a `healthcheck` (missing from the original stub, added for consistency with every other service — `curl -f http://localhost:80/`). `gateway`'s env vars gained `Cors__AllowedOrigins__0=http://localhost:4200`, matching the existing `JwtSettings__*` pattern.

## How to Run

```bash
docker compose up -d --build angular-ui
```

**URLs:**

| URL | Purpose |
| --- | --- |
| `http://localhost:4200` | Angular UI (Docker) |
| `http://localhost:5005` | API Gateway — the only backend URL the UI ever calls |

**Local dev** (live reload, `ng serve`'s `proxy.conf.json` forwards `/api` to the Gateway — requires the rest of the stack already running via `docker compose up -d`):

```bash
cd ClientApp
npm ci
npm start          # http://localhost:4200, proxying /api to :5005
```

**Run the test suite:**

```bash
cd ClientApp
npx ng test --watch=false
```
