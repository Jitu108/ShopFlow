# Phase 6 — API Gateway (Ocelot)

## Project Structure

```text
Gateway/
├── Dockerfile
└── Gateway.Api/
    ├── Gateway.API.csproj
    ├── Program.cs
    ├── ocelot.json
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── Settings/JwtSettings.cs
    └── Properties/launchSettings.json
```

A single project, no Domain/Application/Infrastructure split — per `ShopFlow-Approach.md`, the gateway is configuration, not a Clean Architecture service ("no custom code except middleware"). The one piece of actual code (see "Correctness hazards found" below) is a small middleware, which is exactly the carve-out that line anticipated.

## Pre-work: two known gaps closed before starting

Documented in `STATUS.md`'s "Gaps closed":

1. `Identity / Admin: List Users` was returning 400 — a real bug (`[FromQuery] string name` was implicitly required under `<Nullable>enable</Nullable>`). Fixed across all four layers, +4 tests.
2. `Product` cross-vendor IDOR check was returning 200 instead of 403 — not a code bug, just two Docker images (`product-service`, `cart-service`) that had been running 2+ days on stale builds. Rebuilt, re-verified.

Both were re-verified as passing again in this phase's full Postman run through the gateway (see "Live Verification" below).

## Verified route inventory

Built by reading every controller directly rather than trusting the spec's two illustrative routes (several controllers mix public and protected actions under the same path prefix — see `Phase6-Plan.md`'s table for the full reasoning). All 23 routes wired in `ocelot.json` exactly as planned; no deviations from the planned route/auth table.

## Decisions confirmed before implementation (see `Phase6-Plan.md`)

1. **No dedicated `Gateway.Tests` project.** Verified instead by running the existing Postman collection through the gateway's own port. This is the phase's actual test suite.
2. **Ocelot 25.0.0 on `net10.0`** — confirmed live against NuGet before scaffolding; no compatibility issue.

## Correctness hazards found and fixed during implementation

Three issues surfaced only by actually running the gateway, none of which were visible from reading Ocelot's docs alone:

1. **`MapHealthChecks("/health")` never executed.** Endpoint-routed `Map*` calls in the minimal hosting model get dispatched at the very end of the middleware pipeline (wherever the implicit `UseEndpoints()` lands), but `await app.UseOcelot()` is itself a terminal middleware that claims every request reaching it — including `/health` — before endpoint dispatch ever gets a turn. Every `/health` request came back `404` from Ocelot's own `DownstreamRouteFinderMiddleware`, not from a missing route. **Fix:** use `app.UseHealthChecks("/health")` (an inline middleware, not endpoint-routed) positioned before `UseOcelot()`, so it actually runs in sequence ahead of it.
2. **Rate limiting 503'd every single request, including public ones.** Ocelot's rate limiter identifies "the client" purely via a pre-shared `ClientIdHeader` (default name `ClientId`) — an API-key model, not an IP-based one. With no such header present (true for every real caller in this system — Postman, curl, a future Angular SPA — since nothing here has an API-key concept), Ocelot's rate limiter itself, not the downstream services, returned `503 Service Unavailable` with "Rate limiting client could not be identified" for every request on a rate-limited route, public or not. **Fix:** a small `app.Use(...)` middleware, positioned before `UseOcelot()`, stamps `context.Connection.RemoteIpAddress` into a `ClientId` header when the caller hasn't supplied one — giving NFR-28's "100 requests/minute per client" an IP-based meaning, the only meaning that makes sense here. This is the one piece of actual gateway code, and it fits within Approach.md's "no custom code except middleware" carve-out.
3. **The spec's illustrative `AuthenticationOptions: { AuthenticationProviderKey: "Bearer" }` doesn't match current Ocelot's schema.** Ocelot 25.0.0 uses `AuthenticationProviderKeys` (a plural array), not the singular `AuthenticationProviderKey` the spec's sample shows (from an older Ocelot version). Confirmed via Ocelot's own docs before writing `ocelot.json`, so this was caught before it became a runtime bug rather than after.

None of these were guessable from the spec or from Approach.md's "pure config" framing — all three were found by actually starting the gateway and exercising it, which is the strongest argument for the Postman-based verification approach (Decision #1) over never running it at all.

## Live Verification

Beyond the Postman run (below), the gateway was exercised directly, first standalone (`dotnet run`, downstream services unreachable by design — proves the gateway's own logic in isolation) and then inside Docker against the real running stack:

1. **`/health`** → `200 Healthy`.
2. **Public routes reachable with no token** — `GET /api/products` through the gateway returned real product data from `product-service`, unauthenticated, confirming public/protected route separation is correct (not everything behind Ocelot requires a token).
3. **Protected routes reject with no token** — `GET /api/users/me` → `401`, without ever attempting to reach `identity-service` (confirmed: no corresponding request in `identity-service`'s own logs for that call).
4. **Valid token flows through with downstream authorization still enforced (defense-in-depth, NFR-02)** — a real customer JWT (via the gateway's own `/api/auth/register`) successfully hit `GET /api/users/me` (`200`), and the same token attempting `POST /api/products` (a `RequireVendor`-gated write) got `403` from **Product Service itself**, proving the JWT was forwarded through Ocelot with its claims intact rather than swallowed at the edge.
5. **`RouteClaimsRequirement` blocks at the gateway, not downstream** — placing an order with an unverified-email token returned `403` in **4.5ms**, and the gateway's own log shows exactly why: `ClaimValueNotAuthorizedError: claim value: false is not the same as required value: true for type: emailVerified`. Cross-checked against `order-service`'s logs for the same window: nothing but health-check pings — the request never reached Order Service at all.
6. **Rate limiting** — 105 rapid requests to one route: 98–99 succeeded, 6–7 came back `429`, both via `docker exec` (container-internal) and via the real host-published port. Confirmed live in the gateway's own logs too (`Route '/api/orders' must return rate limiting headers with the following data: 99/100 resets at ...`).
7. **Full Postman collection through the gateway** — see below.

### Host port 5000 conflict (macOS-specific, not a code issue)

The spec's illustrative `GlobalConfiguration.BaseUrl` and the original commented-out `docker-compose.yml` stub both used host port `5000`. On this development machine (macOS), port 5000 is already claimed by **AirPlay Receiver** (Control Center's `AirTunes` service, on by default on modern macOS) — any host-side request to `localhost:5000` was silently intercepted by macOS and answered with `403 Forbidden` before ever reaching Docker, which looked identical to an application-level rejection until traced with `lsof`. Confirmed the gateway container itself was working correctly the whole time by testing via `docker exec` (bypassing the host port entirely). **Resolution (user decision):** remapped the gateway's host port from `5000` to **`5005`** in `docker-compose.yml` and `ocelot.json`'s `BaseUrl`, rather than asking the user to disable a macOS system service. Purely a host-port mapping change — the container's internal port (`80`) and all `ocelot.json` routing/auth/rate-limit config are unaffected.

### Full Postman run via the gateway

Added `Documentations/postman/ShopFlow.gateway.postman_environment.json` — all four `*BaseUrl` variables point at `http://localhost:5005` (the gateway) instead of each service's own direct port. Ran the existing, unmodified `ShopFlow.postman_collection.json` (all 4 folders — Identity, Product, Cart, Order) against it via Newman:

```text
64 requests, 118 assertions, 0 failed
```

Notably, both of the two known gaps closed as pre-work (above) passed cleanly **through the gateway** too: `Admin: List Users` → `200` with a valid array, and `Get Vendor Products - Vendor A requests Vendor B id` → `403`. No collection changes were needed — routing every request through Ocelot instead of directly to each service was transparent to every existing test.

## NuGet Packages

| Package | Project | Status |
| --- | --- | --- |
| `Ocelot 25.0.0` | `Gateway.Api` | ✅ Added — confirmed `net10.0`-native before adopting |
| `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0` | `Gateway.Api` | ✅ Added — same version every other service uses |
| `Serilog.AspNetCore 9.0.0` | `Gateway.Api` | ✅ Added |

## docker-compose.yml

`gateway` block uncommented, plus fixes beyond the original stub: added the `healthcheck` block every other service has (the stub was missing one), added `JwtSettings__Issuer`/`__Audience` env vars alongside the pre-existing `__Secret` (matching every other service's pattern), and remapped the host port to `5005:80` (see above). `depends_on: condition: service_healthy` on `identity-service`/`product-service`/`order-service`/`cart-service` — `notification-service` correctly excluded, it has no HTTP surface for the gateway to route to.

## How to Run

```bash
docker compose up -d --build gateway
```

**URLs:**

| URL | Purpose |
| --- | --- |
| `http://localhost:5005` | Gateway base URL (Docker) — note: not port 5000, see the port-conflict note above |
| `http://localhost:5005/health` | Gateway's own liveness check |

Running the gateway via plain `dotnet run` outside Docker is not a supported workflow: `ocelot.json`'s downstream hosts are Docker Compose service names (`identity-service`, `product-service`, etc.), which only resolve inside the `shopflow-net` Docker network. This is intentional — the gateway's entire purpose is routing between containers, consistent with Approach.md's framing that it's wired "only after all downstream services are healthy."

**Run the verification suite:**

```bash
npx newman run Documentations/postman/ShopFlow.postman_collection.json \
  -e Documentations/postman/ShopFlow.gateway.postman_environment.json
```
