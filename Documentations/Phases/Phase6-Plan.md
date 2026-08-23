# Phase 6 — API Gateway (Ocelot): Implementation Plan

## Context

ShopFlow follows a 7-phase build order (documented in `Documentations/ShopFlow-Approach.md` and tracked live in `Documentations/STATUS.md`). Phases 1–5 (Infrastructure, Identity, Product, Cart, Order + Notification) are complete — 313 tests passing. Phase 6 is the **API Gateway (Ocelot)**, currently pending: `Gateway/` is an empty directory, it's not in `ShopFlow.sln`, and its `docker-compose.yml` block exists only as a comment. The current branch, `dev/TJKG-010-ocelot-api-gateway`, is where this work belongs.

The gateway is the single entry point every client (the future Angular UI, or any external caller) talks to. It routes each upstream path to the right downstream service and enforces JWT auth + rate limiting once, at the edge — deliberately redundant with each service's own `[Authorize]` checks (defence-in-depth, NFR-02), not a replacement for them.

**Spec** (`Documentations/ShopFlow-ProjectSpec.md` §"API Gateway (Ocelot)", NFR-02/14/28): routes configured via `ocelot.json`, JWT Bearer auth per route, global rate limit of 100 req/min per client, gateway only meaningfully useful once downstream services are healthy.

**Approach.md's framing**: "Gateway is pure config — no custom code except middleware." Consistent with Notification Service's precedent (Phase 5) of skipping test projects where there's no real logic to unit-test.

## Pre-work already done: two known gaps closed

Before starting this phase, the two pre-existing Postman failures flagged in `STATUS.md` after Phase 5 were investigated and resolved, so Phase 6's own Postman verification won't be confused by unrelated pre-existing failures:

1. **`Identity / Admin: List Users` (400 instead of 200)** — a real bug. `UsersController.SearchUsers` bound `name` as non-nullable `string`, and `Identity.Api` has `<Nullable>enable</Nullable>`, so ASP.NET Core's implicit-required validation 400'd any request without `?name=` — exactly how the "list all users" Postman request calls it. Fixed by making `name` optional through all four layers (`UsersController` → `SearchUsersByNameQuery` → `IUserRepository.SearchByNameAsync` → `UserRepository` + `FakeUserRepository`), treating missing/blank as "no filter." +4 API tests (this endpoint had zero prior coverage). Verified live against a rebuilt `identity-service`.
2. **`Product / Get Vendor Products - Vendor A requests Vendor B id (403)` (200 instead of 403)** — not a code bug. `VendorsController`'s ownership check was already correct (fixed back in `TJKG-004-known-gap`, well before Phase 3). `shopflow-product`/`shopflow-cart` had simply been running 2+ days on stale images. Rebuilt and restarted both; re-verified live with two freshly-registered vendors that cross-vendor access now correctly 403s. No source change.

Full detail in `STATUS.md`'s "Gaps closed" section. Baseline is clean going into Phase 6.

## Decisions confirmed with the user before writing this plan

| # | Question | Resolution |
|---|---|---|
| 1 | NFR-22 says all code is TDD, but Approach.md calls the gateway "pure config" — does it get a `Gateway.Tests` project? | **No dedicated test project.** Verified instead via the existing Postman/Newman suite, re-pointed at the gateway's port instead of hitting each service directly — same verification style Phase 5 already used for cross-service checks, and consistent with Notification's precedent of skipping test projects where there's no real logic to unit-test. |
| 2 | Does Ocelot support `net10.0`, given every other service targets it and Ocelot has historically lagged new TFMs? | **Confirmed, not a blocker.** Checked live against NuGet: Ocelot 25.0.0 (current stable, published 2026-07-29) ships `net10.0` as a first-class target framework, alongside `net8.0`/`net9.0`. No multi-targeting or version pin workaround needed — take the latest stable. |

## Patterns to reuse (from Identity/Product/Cart/Order — do not reinvent)

- **Dockerfile**: multi-stage SDK-build → aspnet-runtime, `ASPNETCORE_URLS=http://+:80`, curl installed for the container healthcheck — copy Product's Dockerfile shape (single project, no `Shared/` reference needed, so no `context: .` / multi-path COPY like Cart's/Order's).
- **Program.cs banner order**: Logging → Settings → services → auth → health → middleware — same convention every other service follows.
- **JWT Bearer wiring**: `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)`, same as every other service, validating the same `JWT_SECRET`/`Issuer`/`Audience`. Ocelot references this scheme per-route via `AuthenticationOptions.AuthenticationProviderKey`.
- **Solution file**: `dotnet sln add --solution-folder Gateway <csproj>`, never hand-edit `ShopFlow.sln`.
- **Serilog**: `UseSerilog(...)` + `UseSerilogRequestLogging()`, matching Product/Order.

## Verified route inventory (read directly from each controller, not guessed from the spec)

The spec's own `ocelot.json` sample only shows two illustrative routes. The actual routing table needs one entry per distinct auth requirement, not one per controller — several controllers mix public and protected actions under the same prefix. Confirmed by reading every controller:

| Service | Route | Method(s) | Gateway auth |
|---|---|---|---|
| Identity | `/api/auth/register` | POST | none (public) |
| Identity | `/api/auth/login` | POST | none (public) |
| Identity | `/api/auth/refresh` | POST | none (public — refresh token in body, not a bearer token) |
| Identity | `/api/auth/logout` | POST | Bearer |
| Identity | `/api/auth/verify-email` | POST | Bearer |
| Identity | `/api/users/me` | GET | Bearer |
| Identity | `/api/admin/users` | GET | Bearer (+ `RequireAdmin` enforced downstream) |
| Identity | `/api/admin/users/{id}/assign-role` | POST | Bearer (+ `RequireAdmin` downstream) |
| Identity | `/api/admin/users/{id}/reset-password` | POST | Bearer (+ `RequireAdmin` downstream) |
| Product | `/api/products`, `/api/products/{id}` | GET | none (public catalog browsing — Phase 7's customer/catalog needs this unauthenticated) |
| Product | `/api/products` | POST | Bearer (+ `RequireVendor` downstream) |
| Product | `/api/products/{id}` | PUT, DELETE | Bearer (+ `RequireVendor` downstream) |
| Product | `/api/categories` | GET | none (public) |
| Product | `/api/categories` | POST | Bearer (+ `RequireAdmin` downstream) |
| Product | `/api/vendors/{id}/products` | GET | Bearer (+ `RequireVendor` + ownership downstream) |
| Cart | `/api/cart/**` | GET/POST/PUT/DELETE | Bearer (whole controller is `[Authorize]`) |
| Order | `/api/orders` | POST | Bearer (+ `RequireVerifiedEmail` downstream — matches the spec's `RouteClaimsRequirement` example) |
| Order | `/api/orders`, `/api/orders/{id}` | GET | Bearer |
| Order | `/api/orders/{id}/confirm` | PUT | Bearer |
| Order | `/api/admin/orders` | GET | Bearer (+ `RequireAdmin` downstream) |
| Notification | — | — | not routed — no HTTP surface besides its own container-internal `/health` |

Confirmed the `emailVerified` JWT claim is issued as a lowercase string (`"true"`/`"false"`, `TokenService.cs:33`) — matches the spec's `RouteClaimsRequirement": { "emailVerified": "true" }` example verbatim, no adjustment needed. Confirmed no path collisions: Identity's and Order's admin routes both live under `/api/admin/*` but diverge on the next segment (`users` vs `orders`), so they're unambiguous separate route entries.

## Step-by-step plan

### 1. Scaffold `Gateway/Gateway.Api`
Single ASP.NET Core project — no Domain/Application/Infrastructure split; this is configuration, not a Clean Architecture service. `net10.0`. NuGet: `Ocelot 25.0.0`, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`, `Serilog.AspNetCore 9.0.0`.

### 2. `ocelot.json`
One route block per row in the verified table above (public routes carry no `AuthenticationOptions`; protected routes carry `AuthenticationOptions: { AuthenticationProviderKey: "Bearer" }`; the Order-placement route additionally carries `RouteClaimsRequirement: { "emailVerified": "true" }`). `GlobalConfiguration.RateLimitOptions`: `EnableRateLimiting: true`, `Period: "1m"`, `Limit: 100` (NFR-28). `DownstreamHostAndPorts` uses the docker-compose service names/ports (`identity-service:80`, `product-service:80`, `order-service:80`, `cart-service:80`) — Notification is omitted entirely, it has nothing to route to.

### 3. `Program.cs`
- JWT Bearer scheme named `"Bearer"` (matching `AuthenticationProviderKey`), configured against the same `JwtSettings` every other service uses.
- `builder.Services.AddOcelot()`, `app.UseOcelot()` as the terminal middleware.
- Serilog request logging.
- A `/health` endpoint for the gateway's *own* container healthcheck — this doesn't exist in the commented-out docker-compose stub and needs adding; it's a liveness check on the gateway process itself, not a check of downstream health (NFR-14's "only routes to healthy downstream services" is handled by `depends_on: condition: service_healthy` in docker-compose, not by anything Ocelot does at request time).

### 4. `Gateway/Dockerfile`
Copy Product's shape (single-project build/publish, no `Shared/` reference).

### 5. `docker-compose.yml`
Uncomment the `gateway` block, add the missing healthcheck (the existing commented stub doesn't have one — every other service does), keep `ports: "5000:80"`, keep the existing `depends_on` on `identity-service`/`product-service`/`order-service`/`cart-service` (`condition: service_healthy` — Notification correctly excluded, it has no HTTP surface for the gateway to route to).

### 6. Solution file
`dotnet sln ShopFlow.sln add --solution-folder Gateway Gateway/Gateway.Api/Gateway.API.csproj`.

### 7. Verification (no dedicated test project — see Decision #1)
- `dotnet build ShopFlow.sln` — confirm the new project compiles clean alongside the other 18.
- `docker compose up -d --build` full stack, gateway included.
- Manual round trip through the gateway port (5000) instead of each service's own port: register/login (public, no token needed), an authenticated `GET /api/users/me`, a `RequireVendor`-gated product write as a non-vendor (expect 403 — proving downstream enforcement still holds even reached via the gateway), an `emailVerified`-gated order placement with an unverified token (expect the gateway itself to reject via `RouteClaimsRequirement`, before it ever reaches Order Service).
- Rate limiting: fire >100 requests/minute at one route, confirm a `429` appears.
- **Postman**: duplicate (or parametrize) the existing environment files with a `gatewayBaseUrl` pointing at `localhost:5000`, and run the full collection against the gateway instead of direct per-service URLs — this *is* Phase 6's test suite, per Decision #1. All previously-passing requests should still pass; the ones exercising 403/401/429 at the gateway layer are the actual new coverage this phase adds.
- Confirm Notification Service is unaffected — it was never in the gateway's `depends_on` or `ocelot.json`, and shouldn't need to be.

### 8. Docs
- `Documentations/Phases/Phase6.md` (post-implementation write-up, following the `Phase5.md` template — routes actually wired, any deviations found while implementing, live verification log).
- Flip Phase 6 to ✅ in `Documentations/STATUS.md`'s phase table, update "Immediate next steps" to point at Phase 7.

## Critical files

- `Documentations/ShopFlow-ProjectSpec.md` ("API Gateway (Ocelot)" section, NFR-02/14/28) — the illustrative `ocelot.json` shape and rate-limit requirement.
- `Services/Identity/Identity.Infrastructure/Jwt/TokenService.cs` — exact JWT claim shapes (`userId`, `emailVerified`) the gateway's `RouteClaimsRequirement` must match.
- Every `*.Api/Controllers/*.cs` across the 5 services — the verified route/auth table above was built from these directly; re-check before wiring `ocelot.json` in case anything's changed since this plan was written.
- `docker-compose.yml` (the commented `gateway:` block, and each service's `healthcheck:` shape to copy) — integration wiring target.
- `Services/Product/Dockerfile` — the single-project Dockerfile template to copy (Gateway has no `Shared/` dependency, so it's a closer match than Cart's/Order's multi-path Dockerfile).
- `Documentations/postman/ShopFlow.postman_collection.json` + both environment files — this phase's actual verification surface, per Decision #1.
