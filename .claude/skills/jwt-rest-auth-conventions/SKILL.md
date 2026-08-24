---
name: jwt-rest-auth-conventions
description: How JWT auth and REST route protection work across the ShopFlow Ocelot gateway and downstream .NET services. Use when adding a new API route, gateway route entry, authorization policy, or anything touching bearer tokens, roles, or claims.
---

# JWT / REST Auth Conventions

## Token issuance

Only `Identity.Api` issues tokens (`/api/auth/login`, `/register`, `/refresh`). All other services **validate** the same JWT (shared `JwtSettings__Secret`/`Issuer`/`Audience` env vars across services and the gateway) — they never issue their own.

## Gateway (Ocelot) routes

Every downstream route needs an entry in [Gateway/Gateway.Api/ocelot.json](../../../Gateway/Gateway.Api/ocelot.json):

- `UpstreamPathTemplate`/`UpstreamHttpMethod` — what the browser calls through the gateway.
- `DownstreamPathTemplate` + `DownstreamHostAndPorts` pointing at the target service's Compose hostname (e.g. `product-service`, port `80` — the container's internal port, not its published host port).
- Any route that requires a logged-in user adds `"AuthenticationOptions": { "AuthenticationProviderKeys": ["Bearer"] }`. Routes without it (e.g. public catalog GETs, register/login) are intentionally open — don't add the auth block to those.
- A route needing more than "logged in" adds `RouteClaimsRequirement`, e.g. `order-place` requires `{"emailVerified": "true"}` — this is enforced at the gateway, before the request reaches the service.
- Every route key must also be listed under `GlobalConfiguration.RateLimitOptions.RouteKeys` — a new route left out of that list is unlimited by omission, which is easy to miss.

## Per-service policies

Each service defines its own policies in `Program.cs` (see `Product.Api/Program.cs`, `Identity.Api/Program.cs`) after `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`:

```csharp
opts.AddPolicy("RequireVendor", p => p.RequireRole("Vendor"));
opts.AddPolicy("RequireAdmin",  p => p.RequireRole("Admin"));
```

Apply with `[Authorize(Policy = "RequireVendor")]` on the controller action — plain `[Authorize]` with no policy only checks "has a valid token," not role.

## Identifying the caller inside a service

Never trust a vendor/user id passed in the request body or query string for ownership checks. Pull it from the validated token instead:

```csharp
private Guid VendorId => Guid.Parse(User.FindFirstValue("userId")!);
```

Use the resulting id when building the command (e.g. `new CreateProductCommand(VendorId, ...)`), so a vendor can never act on another vendor's data by forging a different id in the payload.

## Adding a new protected endpoint — checklist

1. Add/confirm the controller action's `[Authorize(Policy = "...")]`.
2. Add the Ocelot route with `AuthenticationOptions` (and `RouteClaimsRequirement` if it needs more than login).
3. Add the route key to `RateLimitOptions.RouteKeys`.
4. If the action is ownership-scoped, source the id from `User.FindFirstValue`, not the request payload.
