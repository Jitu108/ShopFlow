# Ocelot API Gateway

## Abstract

`Gateway.Api` is the single public entry point for all of ShopFlow's HTTP traffic — clients never call `identity-service`, `product-service`, `cart-service`, or `order-service` directly. It's built on Ocelot v25, a .NET reverse-proxy library that is entirely config-driven: the gateway has no controllers of its own, just [ocelot.json](../../Gateway/Gateway.Api/ocelot.json) and two small hand-written middlewares in [Program.cs](../../Gateway/Gateway.Api/Program.cs). This file covers what Ocelot is, why ShopFlow put a gateway in front of four independently-deployed services, and the real route/middleware/port details found by reading the actual config — including the documented macOS AirPlay port-5000 conflict.

## What it is

Ocelot is a .NET library, not a separate process or infrastructure product — it's added to an ASP.NET Core app via `AddOcelot(configuration)`/`UseOcelot()` and turns that app into a reverse proxy: it reads a routing table (`ocelot.json`'s `Routes` array), matches each incoming request's path/method against it, optionally runs authentication/claims/rate-limit checks, and forwards the request to a downstream host — all without a single `[HttpGet]` action anywhere in the project. Per [Gateway.md](../Architecture/Gateway.md), `Gateway.Api` is described as "not a Clean Architecture service... `ocelot.json` configuration plus two small pieces of middleware, no custom code except middleware" — there is no Domain/Application/Infrastructure split here at all, unlike every other ShopFlow service.

## Why ShopFlow uses it

With five backend services, a client (the Angular app, Postman, or any future consumer) would otherwise need to know five different base URLs and ports, and every one of those services would need to independently validate JWTs, apply rate limits, and handle CORS. Ocelot centralizes all three:

- **Single entry point** — clients only ever talk to `http://localhost:5005`; `ocelot.json` is the only place that knows `identity-service`, `product-service`, `cart-service`, and `order-service` exist as separate hosts.
- **Centralized auth-provider-key routing** — a route either lists `"AuthenticationProviderKeys": ["Bearer"]` or it doesn't; whether a request needs a valid JWT at all is decided once, per route, at the edge, before a downstream service is ever touched.
- **Port normalization** — every downstream `DownstreamHostAndPorts` entry points at container-internal port `80`, regardless of what host port that service publishes for direct/debugging access (e.g. Cart's own container listens on `80` even though its host-mapped dev port is `5019`) — clients never need to know or care about a service's individually-published port.

`notification-service` is deliberately **not** behind the gateway at all — it's event-driven only (consumes RabbitMQ messages, no HTTP surface beyond its own internal `/health`), so it has zero rows in `ocelot.json`.

## How it's used

### A real route entry

[`order-place`](../../Gateway/Gateway.Api/ocelot.json), the one route in the file that uses every optional field:

```json
{
  "Key": "order-place",
  "UpstreamPathTemplate": "/api/orders",
  "UpstreamHttpMethod": ["POST"],
  "DownstreamPathTemplate": "/api/orders",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [{ "Host": "order-service", "Port": 80 }],
  "AuthenticationOptions": { "AuthenticationProviderKeys": ["Bearer"] },
  "RouteClaimsRequirement": { "emailVerified": "true" }
}
```

`UpstreamPathTemplate`/`UpstreamHttpMethod` describe what the **client** calls; `DownstreamPathTemplate` + `DownstreamHostAndPorts` describe where Ocelot actually forwards it. In this file downstream always equals upstream verbatim — no path rewriting happens anywhere — and `Host` is always a **Docker Compose service name**, never `localhost` or an IP, because these names only resolve inside the `shopflow-net` Docker network. This is also why running the gateway via bare `dotnet run` outside Docker isn't a supported workflow.

A public (unauthenticated) route, by contrast, simply omits `AuthenticationOptions` entirely — [`product-catalog-list`](../../Gateway/Gateway.Api/ocelot.json):

```json
{
  "Key": "product-catalog-list",
  "UpstreamPathTemplate": "/api/products",
  "UpstreamHttpMethod": ["GET"],
  "DownstreamPathTemplate": "/api/products",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [{ "Host": "product-service", "Port": 80 }]
}
```

There's no "make this route public" flag — omitting the auth block is what makes it public. `ocelot.json` has 24 route entries total: 6 public, 17 Bearer-protected, and exactly 1 (`order-place`) additionally gated by `RouteClaimsRequirement`.

### The `Bearer` authentication provider key

`"AuthenticationProviderKeys": ["Bearer"]` refers to the JwtBearer scheme name the gateway registers in [Program.cs](../../Gateway/Gateway.Api/Program.cs):

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
```

`JwtBearerDefaults.AuthenticationScheme` is the literal string `"Bearer"` — Ocelot doesn't invent this key, it's the same ASP.NET Core authentication scheme name every other ShopFlow service registers, validated against the same shared `JwtSettings:Secret`/`Issuer`/`Audience` (see [07-jwt-authentication.md](./07-jwt-authentication.md)). A request that fails JWT validation is rejected with `401` **from the gateway itself** — `order-service` never sees it. `RouteClaimsRequirement` is checked only after the JWT is valid, and rejects with `403` if the named claim doesn't match exactly — `order-place`'s `{"emailVerified": "true"}` is the only such requirement in the file today, and it does a literal string comparison, so Identity must emit that claim as the string `"true"`, not a JSON boolean (see [07-jwt-authentication.md](./07-jwt-authentication.md)).

### The two real middlewares

Per [Gateway.md](../Architecture/Gateway.md), the gateway is "`ocelot.json` configuration plus two small pieces of middleware" — both are in [Program.cs](../../Gateway/Gateway.Api/Program.cs), and their placement relative to `UseOcelot()` is load-bearing:

```csharp
// UseHealthChecks (not MapHealthChecks) so this runs inline, ahead of UseOcelot's
// terminal middleware — endpoint-routed Map* calls are dispatched too late in the
// pipeline to ever be reached once Ocelot has taken over the request.
app.UseHealthChecks("/health");

// Ocelot's rate limiter identifies "the client" purely via a pre-shared ClientId
// header (an API-key model) — it has no IP fallback and 503s any request missing
// one. This system has no API-key concept anywhere else, so stamp the caller's
// remote IP in as the ClientId when the caller hasn't supplied one, giving
// NFR-28's "100 requests/minute per client" an IP-based meaning instead.
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.ContainsKey("ClientId"))
    {
        context.Request.Headers["ClientId"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
    await next();
});

await app.UseOcelot();
```

Both comments are the actual reasoning left in the source, not paraphrased. `UseOcelot()` is `await`ed last and is **terminal** — nothing registered after it in `Program.cs` ever runs for a routed request, which is exactly why `/health` has to be wired via the older `UseHealthChecks(path)` inline-middleware form instead of the more modern `MapHealthChecks(path)` endpoint-routing form: an endpoint-routed health check would never get a turn once Ocelot has claimed the request.

The full middleware order in `Program.cs` is: `UseSerilogRequestLogging()` → `UseCors(AngularUiCorsPolicy)` → `UseAuthentication()` → `UseHealthChecks("/health")` → the `ClientId`-stamping middleware above → `UseOcelot()`. CORS is placed before `UseAuthentication()` specifically so a CORS preflight `OPTIONS` request (which carries no `Authorization` header) is answered before the JWT bearer middleware gets a chance to reject it — per the comment directly above `app.UseCors(...)` in the source.

### Rate limiting

`GlobalConfiguration.RateLimitOptions` in [ocelot.json](../../Gateway/Gateway.Api/ocelot.json) applies one shared policy across every route:

```json
"GlobalConfiguration": {
  "BaseUrl": "http://localhost:5005",
  "RateLimitOptions": {
    "ClientIdHeader": "ClientId",
    "RouteKeys": [ "identity-register", "identity-login", "..." ],
    "Limit": 100,
    "Period": "1m"
  }
}
```

All 24 route `Key`s are listed in `RouteKeys` — none are exempt. `ClientIdHeader: "ClientId"` is why the hand-written stamping middleware above exists at all: Ocelot's rate limiter is an API-key model with no IP-based fallback, and would otherwise `503` any request that didn't already carry a `ClientId` header.

## Gotchas & deviations

- **Port 5005, not 5000** — a real, documented deviation from the original plan. [docker-compose.yml](../../docker-compose.yml) publishes the gateway as `"5005:80"`, and `ocelot.json`'s `GlobalConfiguration.BaseUrl` is `"http://localhost:5005"`. Per the project's Phase 6 notes, the original stub used host port `5000`, which on macOS is silently claimed by the **AirPlay Receiver** service — any request to `localhost:5000` was intercepted by macOS and answered with `403 Forbidden` before ever reaching Docker, indistinguishable from an application-level rejection until traced with `lsof`. The fix was a pure host-port remap to `5005`; the container's internal port (`80`) and all of `ocelot.json`'s routing/auth/rate-limit logic were untouched.
- **`UseHealthChecks`, not `MapHealthChecks`, and it must run before `UseOcelot()`.** Using the endpoint-routed form here is a real, verified bug class (not hypothetical) — the framework's usual convention (`MapHealthChecks` alongside `MapControllers`) silently 404s forever once Ocelot's terminal `UseOcelot()` call is in the pipeline.
- **Ocelot's route templates carry no type constraint.** Unlike a downstream controller's own `[HttpGet("{id:guid}")]`, Ocelot's `{id}` placeholder in `ocelot.json` matches any single path segment — a malformed non-GUID id still reaches the downstream service, which is where the real validation (and any resulting 404) happens.
- **`DownstreamHostAndPorts` always has exactly one entry per route** — no load-balancing across multiple instances of a service is configured anywhere in this file.
- **`identity-refresh` (`POST /api/auth/refresh`) is a public route** (no `AuthenticationOptions` block) — the refresh token itself, not a bearer JWT, is what authorizes that call, so gating it behind `Bearer` would be a chicken-and-egg problem for a client whose access token has already expired.
