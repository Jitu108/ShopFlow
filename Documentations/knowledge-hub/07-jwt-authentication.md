# JWT Authentication — `Microsoft.AspNetCore.Authentication.JwtBearer` + ASP.NET Core Identity

## Abstract

ShopFlow has one service that *is* an identity provider (Identity Service) and four that are pure *relying parties* (Product, Cart, Order, and the Gateway) — they accept a bearer token, validate its signature, and trust the claims inside it without ever calling back to Identity. This file traces the whole chain: how [TokenService](../../Services/Identity/Identity.Infrastructure/Jwt/TokenService.cs) mints a token, the identical `JwtSettings` shape every service binds independently, the claims a controller reads back off `User`, and the three named authorization policies (`RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail`) built on top of those claims.

## What it is

A JSON Web Token is a signed, self-contained bearer credential: a base64url header + payload + signature, where the payload carries claims (`userId`, email, role, …) and the signature (here, HMAC-SHA256 over a shared secret) lets *any* holder of that secret verify the token hasn't been tampered with, without a database round-trip. `Microsoft.AspNetCore.Authentication.JwtBearer` is the ASP.NET Core middleware that reads the `Authorization: Bearer <token>` header, validates the signature/issuer/audience/expiry, and populates `HttpContext.User` with the token's claims as a `ClaimsPrincipal`. ASP.NET Core Identity supplies the surrounding user-management primitives Identity Service builds on (`ApplicationUser`, `IPasswordHasher<TUser>`) — but ShopFlow does **not** use Identity's own cookie/session auth or its `SignInManager`; only the password hasher is reused, and token issuance is a hand-written `TokenService`, not `UseIdentityServer` or similar.

## Why ShopFlow uses it

ShopFlow is five independently-deployed microservices behind a gateway. A traditional session (server-side session state + cookie) would require a shared session store every service could read, adding a stateful dependency and a single point of coupling between otherwise-independent deployments. A signed bearer token needs none of that: any service holding the same `JwtSettings:Secret`/`Issuer`/`Audience` can validate a token Identity issued, entirely in-process, with zero network calls back to Identity and no shared cache. This is also why the same validation block is copy-pasted (not shared via a library) into Product, Cart, Order, and the Gateway's own `Program.cs` — see [§Shared JwtSettings pattern](#the-shared-jwtsettings-pattern) below.

## How it's used

### Issuing a token — `TokenService` (Identity only)

[TokenService.cs](../../Services/Identity/Identity.Infrastructure/Jwt/TokenService.cs) is the only place in ShopFlow that *signs* a JWT:

```csharp
public string GenerateJwtToken(ApplicationUser user)
{
    var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
    {
        new("userId",         user.Id.ToString()),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Role,  user.Role.ToString()),
        new("emailVerified",  user.IsEmailVerified.ToString().ToLower())
    };

    var token = new JwtSecurityToken(
        issuer:             _settings.Issuer,
        audience:           _settings.Audience,
        claims:             claims,
        expires:            DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes),
        signingCredentials: credentials
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

Four claims travel in every token: a custom `userId` claim (a plain string type, not `ClaimTypes.NameIdentifier`), `ClaimTypes.Email`, `ClaimTypes.Role` (the `UserRole` enum — `Customer`, `Vendor`, or `Admin`, from [UserRole.cs](../../Services/Identity/Identity.Domain/Enums/UserRole.cs) — stringified), and a custom `emailVerified` claim. `emailVerified` is deliberately written as the lowercase literal string `"true"`/`"false"` (`.ToLower()`), not a JSON boolean — `RouteClaimsRequirement`/`RequireClaim` policy checks compare claim values as literal strings, so a `"True"` (C#'s default `bool.ToString()` casing) would silently never match.

`TokenService` also mints refresh tokens (`GenerateRefreshTokenAsync`, a 7-day-lived opaque token persisted via `IRefreshTokenRepository`) — a separate concern from the JWT itself, used only by [RefreshTokenCommandHandler](../../Services/Identity/Identity.Application/Commands/RefreshTokenCommandHandler.cs) to mint a new JWT without forcing a re-login.

### The shared `JwtSettings` pattern

Every service — including the Gateway — declares its **own** `JwtSettings` class, identically shaped (`Secret`, `Issuer`, `Audience`), bound from its own configuration:

```csharp
public class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public string Secret   { get; init; } = string.Empty;
    public string Issuer   { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
}
```

Only [Identity's copy](../../Services/Identity/Identity.Infrastructure/Settings/JwtSettings.cs) adds a fourth property, `ExpiryMinutes` — the only service that ever needs to compute a token expiry, since the other four only validate. The other four copies live at [Product.Infrastructure/Settings/JwtSettings.cs](../../Services/Product/Product.Infrastructure/Settings/JwtSettings.cs), [Cart.Infrastructure/Settings/JwtSettings.cs](../../Services/Cart/Cart.Infrastructure/Settings/JwtSettings.cs), [Order.Infrastructure/Settings/JwtSettings.cs](../../Services/Order/Order.Infrastructure/Settings/JwtSettings.cs), and [Gateway.Api/Settings/JwtSettings.cs](../../Gateway/Gateway.Api/Settings/JwtSettings.cs). There's no shared `ShopFlow.Shared`-style library for this type — each service's `Program.cs` independently does:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((jwtOpts, settings) =>
    {
        var s = settings.Value;
        jwtOpts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = s.Issuer,
            ValidateAudience = true,
            ValidAudience = s.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
```

(verbatim from [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs), and byte-for-byte the same shape in [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs), [Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs), [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs), and [Gateway.Api/Program.cs](../../Gateway/Gateway.Api/Program.cs)). The comment above the block in every file — `// Configure JWT options lazily so WebApplicationFactory config overrides are respected` — explains why `AddOptions<...>().Configure<IOptions<JwtSettings>>(...)` is used instead of configuring `TokenValidationParameters` directly inside `.AddJwtBearer(opts => ...)`: the lazy `IOptions<JwtSettings>` indirection lets `WebApplicationFactory`-based integration tests override `JwtSettings` in config *after* `Program.cs` has already run, without touching the validation logic itself. `ClockSkew = TimeSpan.Zero` removes the default 5-minute grace period ASP.NET Core otherwise allows on expiry checks — an expired token is rejected exactly at its `exp` time, not five minutes later.

What actually keeps all five services in sync in Docker is not the C# code but the environment — every service's [docker-compose.yml](../../docker-compose.yml) block sets the same three env vars from the same `.env` values:

```yaml
- JwtSettings__Secret=${JWT_SECRET}
- JwtSettings__Issuer=ShopFlow
- JwtSettings__Audience=ShopFlow
```

If `JWT_SECRET` ever differs between two services' containers, tokens issued by Identity would fail signature validation everywhere else — there is no runtime check that catches a mismatched secret except a 401 at the first protected call.

### Reading claims back out

No controller trusts a caller-supplied id for "whose data is this" — every service derives it from the validated token via a private computed property on the controller, the same pattern in all four:

```csharp
// Product.Api/Controllers/ProductsController.cs
private Guid VendorId => Guid.Parse(User.FindFirstValue("userId")!);

// Cart.Api/Controllers/CartController.cs
private Guid UserId => Guid.Parse(User.FindFirstValue("userId")!);

// Order.Api/Controllers/OrdersController.cs
private Guid CustomerId => Guid.Parse(User.FindFirstValue("userId")!);
private string CustomerEmail => User.FindFirstValue(ClaimTypes.Email)!;
private bool IsAdmin => User.IsInRole("Admin");
```

`Order.Api`'s [OrdersController](../../Services/Order/Order.Api/Controllers/OrdersController.cs) is the only controller reading three different claim shapes off the same principal: the custom `userId` claim via `FindFirstValue`, the standard `ClaimTypes.Email` claim the same way, and the role claim via the framework's own `User.IsInRole(...)` (which reads the same `ClaimTypes.Role` claim `TokenService` wrote, through ASP.NET Core's built-in role-claim convention rather than a manual `FindFirstValue`).

### Authorization policies

Named policies are registered per-service in `Program.cs`, immediately after `AddAuthentication`/`AddJwtBearer` — not in a shared library, so each service only registers the policies it actually needs:

| Policy | Definition | Registered in | Used on |
| --- | --- | --- | --- |
| `RequireVendor` | `p.RequireRole("Vendor")` | [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs) | `ProductsController` create/update/delete, `VendorsController` |
| `RequireAdmin` | `p.RequireRole("Admin")` | Identity, Product, Order | `UsersController` admin actions, `CategoriesController`, `AdminOrdersController` |
| `RequireVerifiedEmail` | `p.RequireClaim("emailVerified", "true")` | [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs) and [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs) | `OrdersController.PlaceOrder` |

Identity's registration block, from [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs):

```csharp
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("RequireVendor",        p => p.RequireRole("Vendor"));
    opts.AddPolicy("RequireAdmin",         p => p.RequireRole("Admin"));
    opts.AddPolicy("RequireVerifiedEmail", p => p.RequireClaim("emailVerified", "true"));
});
```

A real usage, from [OrdersController.cs](../../Services/Order/Order.Api/Controllers/OrdersController.cs) — the one endpoint in ShopFlow gated by *both* a gateway-level claims check and a service-level policy on the same claim:

```csharp
[HttpPost]
[Authorize(Policy = "RequireVerifiedEmail")]
public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
```

And a `RequireVendor` usage from [ProductsController.cs](../../Services/Product/Product.Api/Controllers/ProductsController.cs):

```csharp
[HttpPost]
[Authorize(Policy = "RequireVendor")]
public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
```

## Gotchas & deviations

- **Cart has no named policies at all.** [Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs) calls plain `builder.Services.AddAuthorization()` with no `AddPolicy` calls — every `CartController` action carries only class-level `[Authorize]` (valid token required, nothing role-specific), since a cart has no concept of ownership beyond "this JWT's own cart." This is a deliberate simplification documented in [Cart-Service.md](../Architecture/Cart-Service.md), not an oversight.
- **`RequireVerifiedEmail` is enforced twice for `POST /api/orders`, independently.** The [Gateway's ocelot.json](../../Gateway/Gateway.Api/ocelot.json) rejects an unverified token at the edge via `RouteClaimsRequirement: { "emailVerified": "true" }` before the request ever reaches `order-service`; `OrdersController`'s own `[Authorize(Policy = "RequireVerifiedEmail")]` checks the identical claim again downstream. This is intentional defense-in-depth (see [Gateway.md §3](../Architecture/Gateway.md#3-auth-decision-flow--where-a-request-can-get-rejected)) — the gateway's checks narrow what *can* reach a service, but each service still enforces its own authorization rather than trusting the edge completely.
- **Only Identity's `JwtSettings` has `ExpiryMinutes`.** The other four services' copies of the class (Product, Cart, Order, Gateway) omit it — confirmed by reading all five `JwtSettings.cs` files; none of the four validating-only services ever construct a `JwtSecurityToken`, so there's nothing for them to expire.
- **`emailVerified` is a string claim compared as a literal string, not a bool.** Both `RequireClaim("emailVerified", "true")` and Ocelot's `RouteClaimsRequirement` do exact string matching against the claim value `TokenService` writes as `user.IsEmailVerified.ToString().ToLower()` — if that `.ToLower()` were ever dropped, the claim would read `"True"` and every one of these checks would start failing silently (a 401/403, not an exception).
- **`userId` is a custom claim type, not `ClaimTypes.NameIdentifier`.** Every `FindFirstValue("userId")` call across all four services depends on `TokenService` continuing to emit the literal string `"userId"` — there's no shared constant for this claim name; it's a repeated string literal in `TokenService.cs` and every controller.
