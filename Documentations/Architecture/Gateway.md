# API Gateway (Ocelot) — Architecture Diagram

Unlike Identity/Product/Cart/Order/Notification (see their own docs in this folder), the Gateway is not a Clean Architecture service — per [Phase6.md](../Phases/Phase6.md) it's `ocelot.json` configuration plus two small pieces of middleware, "no custom code except middleware." This page is the diagram companion to that phase doc: what request reaches what, in what order, and why the middleware is ordered the way it is.

---

## 1. System Diagram — Request Routing

```text
                                   ┌────────────────────────┐
                                   │   Client (Postman /     │
                                   │   curl / future Angular)│
                                   └────────────┬─────────────┘
                                                │  HTTP :5005
                                                ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                        Gateway.Api  (Ocelot, port 5005 → 80)                 │
│                                                                                │
│   1. UseSerilogRequestLogging                                                 │
│   2. UseAuthentication            ── validates JWT if the route requires it   │
│   3. UseHealthChecks("/health")   ── inline, MUST run before UseOcelot        │
│   4. ClientId-stamping middleware ── RemoteIpAddress → "ClientId" header      │
│                                       (only if caller didn't supply one)      │
│   5. UseOcelot()                  ── terminal: routes, or 401 / 403 / 429     │
│                                                                                │
│   ocelot.json: 24 routes · 18 authenticated · 6 public · 1 claims-gated ·     │
│   all 24 rate-limited (100 req/min per ClientId, NFR-28)                      │
└───────────────┬───────────────┬───────────────┬───────────────┬──────────────┘
                │               │               │               │
     (mixed:    │  (mixed:      │  (all Bearer- │  (all Bearer-  │
      3 public +│   3 public +  │   protected —  │   protected —  │
      6 Bearer) │   5 Bearer)   │   no public    │   no public    │
                │               │   routes)      │   routes;      │
                │               │                │   POST /orders │
                │               │                │   additionally │
                │               │                │   needs the    │
                │               │                │   emailVerified│
                │               │                │   claim)       │
                ▼               ▼               ▼               ▼
      ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
      │identity-service│ │product-service│ │ cart-service │ │ order-service│
      │    :80        │ │     :80       │ │     :80      │ │     :80      │
      └──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘

      notification-service is NOT behind the gateway — it has no HTTP surface
      to route to (event-driven only, see Notification-Service.md).
```

Per-route auth is decided individually in `ocelot.json`, not per service — the four downstream services above are **not** uniformly public or uniformly protected. Concretely: `identity-service` carries 3 public routes (`register`/`login`/`refresh`) and 6 Bearer-protected ones; `product-service` carries 3 public routes (`GET /api/products`, `GET /api/products/{id}`, `GET /api/categories`) and 5 Bearer-protected ones (writes, plus `GET /api/vendors/{id}/products`); `cart-service` and `order-service` have no public routes at all. The `emailVerified` claim requirement is narrower still — it's attached to exactly **one** route, `POST /api/orders` (placing an order); `PUT /api/orders/{id}/confirm` (confirming one) is Bearer-protected only, with no claim requirement. See [§4](#4-route-inventory-at-a-glance) for the full per-route breakdown.

Every downstream host in `ocelot.json` (`identity-service`, `product-service`, `cart-service`, `order-service`) is a Docker Compose service name — the gateway only resolves them inside the `shopflow-net` Docker network, which is why `dotnet run` outside Docker is not a supported way to use it.

---

## 2. Why the Middleware Order Is Exactly This

Three hazards were found only by running the gateway, not by reading Ocelot's docs (see [Phase6.md](../Phases/Phase6.md) for the full writeups). Each one is a constraint on the diagram above:

```text
 WRONG ORDER (what the framework's usual convention suggests):
    ...  →  UseAuthentication  →  UseOcelot  →  [MapHealthChecks("/health")]
                                       ▲
                                       └── UseOcelot is TERMINAL. It claims every
                                           request — including /health — before
                                           endpoint routing ever gets a turn.
                                           Result: /health → 404, always.

 CORRECT ORDER (what's actually in Program.cs):
    UseAuthentication → UseHealthChecks("/health")  → ClientId stamp → UseOcelot
                              (inline middleware,        (Ocelot's rate
                               not endpoint-routed —      limiter 503s any
                               runs in sequence,           request with no
                               ahead of the terminal        pre-shared ClientId
                               UseOcelot call)              header — there is no
                                                              IP-based fallback)
```

- **`UseHealthChecks`, not `MapHealthChecks`** — the endpoint-routed form is dispatched too late in the minimal-hosting pipeline to ever run once `UseOcelot()` has taken over the request.
- **The `ClientId` stamping middleware runs before `UseOcelot()`** — Ocelot's rate limiter is an API-key model (`ClientIdHeader: "ClientId"`), not IP-based, and 503s anything missing that header. This is the one hand-written piece of gateway logic, and it exists solely to give NFR-28 ("100 req/min per client") an IP-based meaning in a system that has no API-key concept anywhere else.
- **`UseOcelot()` is `await`ed last and is terminal** — nothing after it in `Program.cs` ever executes for a routed request.

---

## 3. Auth Decision Flow — Where a Request Can Get Rejected

```text
Request
   │
   ▼
┌─────────────────────────┐   route has no AuthenticationOptions
│ Does ocelot.json's route│───────────────────────────────────────► forwarded, no token needed
│ require "Bearer"?       │   (6 routes — e.g. GET /api/products,
└───────────┬─────────────┘    POST /api/auth/register)
            │ yes (18 routes)
            ▼
┌─────────────────────────┐   missing / invalid / expired JWT
│ JwtBearer validates the │───────────────────────────────────────► 401, from the GATEWAY —
│ token (Issuer/Audience/ │                                          never reaches the
│ signing key — same      │                                          downstream service
│ secret every service    │
│ shares)                 │
└───────────┬─────────────┘
            │ valid
            ▼
┌─────────────────────────┐   route also has RouteClaimsRequirement
│ Does the route also     │   (only 1 route today: POST /api/orders
│ require a specific claim│    needs emailVerified: "true")
│ value?                  │
└───────────┬─────────────┘
            │
     ┌──────┴───────┐
     │ claim missing │──────────────────────────────────────────────► 403, from the GATEWAY,
     │ or wrong value│                                                  in ~4.5ms — order-service
     └───────────────┘                                                  never sees the request
            │ claim matches (or none required)
            ▼
┌─────────────────────────┐
│ ClientId rate limiter    │  over 100 req/min for this ClientId
│ (100 req / 1 min, all   │────────────────────────────────────────► 429, from the GATEWAY
│  24 routes)              │
└───────────┬──────────────┘
            │ under limit
            ▼
     Forwarded downstream — where the SAME token is checked AGAIN
     (RequireVendor / RequireAdmin / RequireVerifiedEmail policies
     already documented in each service's own architecture doc).
     This is deliberate defense-in-depth (NFR-02): the gateway's
     checks narrow what CAN reach a service; each service still
     enforces its own authorization independently rather than
     trusting the edge completely.
```

The claims-requirement short-circuit is the sharpest proof this is a real gate rather than a pass-through: an unverified-email token hitting `POST /api/orders` was rejected by the gateway in 4.5ms with zero corresponding entries in `order-service`'s own logs for that window.

---

## 4. Route Inventory at a Glance

| Category | Count | Example | Auth at gateway |
| --- | --- | --- | --- |
| Public | 6 | `POST /api/auth/register`, `GET /api/products` | none |
| Bearer-protected | 17 | `GET /api/users/me`, `POST /api/cart/items` | `AuthenticationProviderKeys: ["Bearer"]` |
| Bearer + claim-gated | 1 | `POST /api/orders` | Bearer **+** `RouteClaimsRequirement: { emailVerified: "true" }` |
| **Total** | **24** | — | all 24 also rate-limited, 100 req/min per `ClientId` |

`notification-service` has zero rows here — it's excluded from `ocelot.json` entirely, consistent with it having no HTTP surface beyond its own internal `/health` (see [Notification-Service.md](./Notification-Service.md)).

---

## 5. Anatomy of `ocelot.json` — Field Reference

`ocelot.json` has exactly two top-level members: a `Routes` array (24 entries, one per upstream endpoint) and one `GlobalConfiguration` object (settings shared by every route). Every route entry has the same shape; only the last two fields below are ever omitted.

### 5.1 A fully-annotated route

This is [`order-place`](../../Gateway/Gateway.Api/ocelot.json) — chosen because it's the one route that uses every optional field:

```jsonc
{
  "Key": "order-place",                          // ── unique route identifier ──
  "UpstreamPathTemplate": "/api/orders",          // ── what the CLIENT calls ──
  "UpstreamHttpMethod": ["POST"],                 // ── which verb(s) match ──
  "DownstreamPathTemplate": "/api/orders",        // ── what Ocelot rewrites the path to ──
  "DownstreamScheme": "http",                     // ── protocol used to reach it ──
  "DownstreamHostAndPorts": [                     // ── where it actually goes ──
    { "Host": "order-service", "Port": 80 }
  ],
  "AuthenticationOptions": {                      // ── gate #1: is there a valid JWT? ──
    "AuthenticationProviderKeys": ["Bearer"]
  },
  "RouteClaimsRequirement": {                     // ── gate #2: does a claim match? ──
    "emailVerified": "true"
  }
}
```

| Field | Meaning | Notes from this codebase |
| --- | --- | --- |
| **`Key`** | A unique label for the route, used only internally (by `GlobalConfiguration.RateLimitOptions.RouteKeys` — see [§5.3](#53-globalconfiguration)) — never seen by a client | Named `{service}-{action}` (`identity-register`, `product-create`, `order-admin-list`, ...). Purely a naming convention chosen for this repo, not an Ocelot requirement — Ocelot only needs the value to be unique. |
| **`UpstreamPathTemplate`** | The path the **client** requests, exactly as the gateway's own public API surface | Always mirrors the real controller route one-for-one — e.g. Order's own `[Route("api/orders")]` becomes `/api/orders` here too. The gateway never renames a path for clients. |
| **`UpstreamHttpMethod`** | Which HTTP verb(s) this route block matches | Usually one verb per `Key` (`identity-login` = `["POST"]` only). Where a controller genuinely mixes verbs under one path template, one route block lists several — e.g. `cart-items` matches `["POST", "PUT", "DELETE"]` together, because `Cart.Api`'s `POST/PUT/DELETE /api/cart/items...` actions all share that same upstream shape. |
| **`DownstreamPathTemplate`** | The path Ocelot **rewrites the request to** before forwarding it | In every route in this file, downstream equals upstream verbatim — the gateway does no path translation anywhere. This field exists so a route *could* differ (e.g. exposing `/api/v1/foo` upstream while calling `/api/foo` downstream), but nothing here uses that capability. |
| **`DownstreamScheme`** | Protocol used for the downstream hop | `"http"` on every single route — there's no TLS between the gateway and any service; only the client-to-gateway hop needs to be secured, and that's Ocelot's own Kestrel binding, configured outside this file. |
| **`DownstreamHostAndPorts`** | An array of `{ Host, Port }` targets Ocelot can forward to | Always exactly one entry per route in this file — no load-balancing between multiple instances is configured. `Host` is always a **Docker Compose service name** (`identity-service`, `product-service`, `cart-service`, `order-service`), never `localhost` or an IP — these names only resolve inside the `shopflow-net` Docker network, which is why running the gateway via bare `dotnet run` outside Docker doesn't work (see [§1](#1-system-diagram--request-routing)). `Port` is always `80` — every downstream service's *container-internal* port, not its host-published port (e.g. Order's own container listens on `80` even though its host-mapped port is `5020`/`5003`). |
| **`AuthenticationOptions`** *(optional — omitted on 6 of 24 routes)* | Requires a valid JWT before the route is even considered reachable | `AuthenticationProviderKeys: ["Bearer"]` refers to the JwtBearer scheme name registered in `Program.cs` (`AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer()`) — **not** a raw string Ocelot invented. Omitting this block entirely, rather than setting some "no auth" flag, is what makes a route public. |
| **`RouteClaimsRequirement`** *(optional — present on exactly 1 of 24 routes)* | A dictionary of claim-type → required-value; the request is rejected unless every listed claim matches **exactly** | Only `order-place` carries this, `{ "emailVerified": "true" }` — enforcing FR-… "customers must verify their email before placing an order" **at the edge**, before Order Service is ever touched (see the auth flow in [§3](#3-auth-decision-flow--where-a-request-can-get-rejected)). The value is compared as a literal string against the JWT's claim value, which is why the claim must be emitted as the string `"true"`/`"false"`, not a JSON boolean, when Identity issues the token. |

### 5.2 Path placeholders

Ocelot's templating syntax uses exactly two distinct placeholder *types*, but they appear across **nine** of the file's 24 route entries (seven distinct upstream templates, since `/api/products/{id}` alone is reused by three separate `Key`s for three different verbs):

| Placeholder | Meaning | Distinct templates it appears in | Route `Key`s using it |
| --- | --- | --- | --- |
| `{id}` | Matches exactly one path segment, forwarded through unchanged | `/api/products/{id}` · `/api/vendors/{id}/products` · `/api/orders/{id}` · `/api/orders/{id}/confirm` · `/api/admin/users/{id}/assign-role` · `/api/admin/users/{id}/reset-password` | `product-catalog-detail`, `product-update`, `product-delete` (all three on `/api/products/{id}`, one per verb), `vendor-products`, `order-get-by-id`, `order-confirm`, `identity-admin-assign-role`, `identity-admin-reset-password` |
| `{everything}` | A catch-all that matches the rest of the path, however many segments | `/api/cart/{everything}` | `cart-items` — covers `POST /api/cart/items`, `PUT /api/cart/items/{productId}`, and `DELETE /api/cart/items/{productId}` with a single route block, since those three verbs share the `/api/cart/items...` prefix but not a fixed number of trailing segments |

Both placeholder names are arbitrary labels (Ocelot doesn't care what's inside the braces) and, unlike each service's own ASP.NET route constraints (e.g. `[HttpGet("{id:guid}")]` inside `ProductsController`), **Ocelot's route templates carry no type constraint** — `{id}` matches any single segment, not just a GUID. A malformed ID still reaches the downstream controller, which is where the real `{id:guid}` constraint (and any resulting 404) actually applies.

### 5.3 `GlobalConfiguration`

The one object in the file that isn't a route — settings that apply across all of them:

```jsonc
"GlobalConfiguration": {
  "BaseUrl": "http://localhost:5005",
  "RateLimitOptions": {
    "ClientIdHeader": "ClientId",
    "RouteKeys": [ "identity-register", "identity-login", /* ...all 24 Keys... */ ],
    "Limit": 100,
    "Period": "1m"
  }
}
```

| Field | Meaning | Notes from this codebase |
| --- | --- | --- |
| **`BaseUrl`** | The externally-visible base URL Ocelot reports/uses for itself | `http://localhost:5005` — not `5000`, because of the macOS AirPlay port conflict documented in [Phase6.md](../Phases/Phase6.md). This is purely descriptive metadata for Ocelot's own use (e.g. in generated links); it doesn't change what port Kestrel actually binds to — that's controlled by `docker-compose.yml`'s port mapping and `ASPNETCORE_URLS`. |
| **`RateLimitOptions.ClientIdHeader`** | Which request header Ocelot reads to identify "the client" for rate-limiting purposes | `"ClientId"` — an API-key-style header, not IP-based by default. Since nothing in this system has an API key, `Program.cs`'s hand-written middleware stamps the caller's remote IP into this exact header before `UseOcelot()` runs, whenever the caller didn't supply one (see [§2](#2-why-the-middleware-order-is-exactly-this)). |
| **`RateLimitOptions.RouteKeys`** | Which routes (by their `Key`) this global rate-limit policy applies to | All 24 `Key` values are listed — every route in the file is rate-limited, none exempted. This is what lets every route opt into rate limiting via one shared block instead of repeating a per-route `RateLimitOptions` object 24 times. |
| **`RateLimitOptions.Limit` / `Period`** | The actual quota | `100` requests per `"1m"` (one minute), per distinct `ClientId` value — implementing NFR-28 verbatim. Exceeding it returns `429` directly from Ocelot, before the request is forwarded anywhere (confirmed live — see [Phase6.md](../Phases/Phase6.md)'s rate-limiting verification). |

---

For the full narrative — the macOS AirPlay port-5000 conflict, the Postman-through-the-gateway verification run, and the two pre-existing bugs closed before this phase started — see [Phase6.md](../Phases/Phase6.md) and [Phase6-Plan.md](../Phases/Phase6-Plan.md).
