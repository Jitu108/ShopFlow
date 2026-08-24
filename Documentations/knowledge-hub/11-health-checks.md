# Health Checks — `AspNetCore.HealthChecks.{SqlServer,Redis,Rabbitmq}`

## Abstract

Every ShopFlow service exposes a `/health` endpoint, but no two services check the same set of dependencies — each one's `AddHealthChecks()` call is tailored to exactly what that service actually depends on: SQL Server, Redis, RabbitMQ, some combination, or (for the Gateway) nothing but its own liveness. This file covers what ASP.NET Core health checks are, why ShopFlow deliberately varies them per service instead of using one shared check set, and the real `AddHealthChecks()`/`MapHealthChecks("/health")` code from multiple services showing the variation.

## What it is

ASP.NET Core's health check middleware exposes an endpoint that runs a set of registered `IHealthCheck` probes and reports the aggregate result (`Healthy`/`Degraded`/`Unhealthy`) as an HTTP status. The `AspNetCore.HealthChecks.*` NuGet family (from the community `xabaril/AspNetCore.Diagnostics.HealthChecks` project) supplies ready-made probes for specific dependencies — `AspNetCore.HealthChecks.SqlServer` opens a real connection and runs a trivial query against SQL Server, `AspNetCore.HealthChecks.Redis` pings a Redis connection, `AspNetCore.HealthChecks.Rabbitmq` opens a real AMQP connection to the broker. Registering one via `.AddSqlServer(...)`/`.AddRedis(...)`/`.AddRabbitMQ(...)` on the `AddHealthChecks()` builder adds it to the aggregate; `app.MapHealthChecks("/health")` (or, in the Gateway's case, the inline `UseHealthChecks("/health")` form — see [08-ocelot-gateway.md](./08-ocelot-gateway.md)) exposes the result over HTTP.

## Why ShopFlow uses it — tailored per service, not uniform

Docker Compose's `depends_on: condition: service_healthy` (used throughout [docker-compose.yml](../../docker-compose.yml)) needs a real signal for "is this container actually ready to receive traffic," not just "has the process started" — a service can be up and listening before its database connection pool or message broker connection is actually usable. Each service's health check set mirrors its *own* real infrastructure dependencies, not a copy-pasted uniform list, because a check against a dependency a service doesn't have would either fail to compile (no connection string exists to check) or falsely gate readiness on something irrelevant to that service:

| Service | Checks | Why |
| --- | --- | --- |
| Identity | SQL Server only | Owns `IdentityDb`; no Redis, no RabbitMQ dependency anywhere in the service |
| Product | SQL Server + Redis | Owns `ProductDb`; also uses Redis as a read-through cache in front of it (see `ICacheService`) |
| Cart | Redis only | No SQL Server anywhere in the service — Redis *is* the persistence, not a cache in front of one (see [Cart-Service.md](../Architecture/Cart-Service.md)) |
| Order | SQL Server + RabbitMQ | Owns `OrderDb`; publishes `OrderPlacedEvent`/`OrderShippedEvent` to RabbitMQ on order lifecycle transitions |
| Notification | RabbitMQ only | No database of its own; purely a RabbitMQ consumer that sends email |
| Gateway | none (bare `AddHealthChecks()`) | Has no direct infrastructure dependency of its own to probe — its "health" is just "is the process up," since the actual routing targets' health is each service's own `/health` |

## How it's used

### Identity — SQL Server only

[Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs):

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default") ?? string.Empty);
```

### Product — SQL Server *and* Redis

[Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs):

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default") ?? string.Empty)
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
```

### Cart — Redis only, no SQL Server check exists to add

[Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs):

```csharp
builder.Services.AddHealthChecks()
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
```

`Cart.API.csproj` doesn't even reference `AspNetCore.HealthChecks.SqlServer` — confirmed by reading [Cart.API.csproj](../../Services/Cart/Cart.Api/Cart.API.csproj), which lists only `AspNetCore.HealthChecks.Redis`. There is no SQL Server dependency anywhere in this service's stack to check.

### Order — SQL Server *and* RabbitMQ

[Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs):

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default") ?? string.Empty)
    .AddRabbitMQ(_ =>
    {
        var factory = new ConnectionFactory
        {
            HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
            UserName = builder.Configuration["RabbitMQ:User"] ?? "guest",
            Password = builder.Configuration["RabbitMQ:Pass"] ?? "guest"
        };
        return factory.CreateConnectionAsync();
    });
```

Order's `.AddRabbitMQ(...)` overload takes a factory function returning a real AMQP `IConnection` (via `ConnectionFactory.CreateConnectionAsync()`), built from the same `RabbitMQ:Host`/`User`/`Pass` configuration keys the MassTransit setup above it in the same file already uses — not a connection string, unlike the SQL Server and Redis checks.

### Notification — RabbitMQ only, no HTTP controllers at all

[Notification.Api/Program.cs](../../Services/Notification/Notification.Api/Program.cs) registers the identical `AddRabbitMQ(...)` pattern as Order, but the file never calls `AddControllers()`, `AddAuthentication()`, or `AddSwaggerGen()` anywhere — `/health` is the *only* route this service exposes:

```csharp
builder.Services.AddHealthChecks()
    .AddRabbitMQ(sp =>
    {
        var factory = new ConnectionFactory
        {
            HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
            UserName = builder.Configuration["RabbitMQ:User"] ?? "guest",
            Password = builder.Configuration["RabbitMQ:Pass"] ?? "guest"
        };
        return factory.CreateConnectionAsync();
    });

// ...
var app = builder.Build();
app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
app.Run();
```

### Mapping the endpoint

All four HTTP-serving services (Identity, Product, Cart, Order) map the endpoint identically, near the end of the middleware pipeline:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
```

`/health` sits after `UseAuthentication()`/`UseAuthorization()` in the pipeline but the endpoint itself carries no `[Authorize]` — `MapHealthChecks` doesn't require a token by default, and none of these services add one, since Docker's own `healthcheck:` probe (see below) has no way to supply a JWT.

### Docker Compose wiring

Every service's [docker-compose.yml](../../docker-compose.yml) block uses the health endpoint as its own container healthcheck, and as the input to other services' `depends_on: condition: service_healthy`:

```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:80/health"]
  interval: 15s
  timeout: 5s
  retries: 3
```

This exact block appears for `identity-service`, `product-service`, `order-service`, and `cart-service`. `notification-service` uses the same `curl -f http://localhost:80/health` test but is wired with `depends_on: condition: service_started` on its dependency rather than `service_healthy` for at least one caller, and the `gateway` service depends on all four HTTP services with `condition: service_healthy` — the gateway won't accept traffic (per its own `depends_on`) until Identity, Product, Order, and Cart all report healthy, which in turn means their own SQL Server/Redis/RabbitMQ dependencies (which have their own `healthcheck:` blocks — e.g. `sqlserver`'s `sqlcmd -Q "SELECT 1"`, `redis`'s `redis-cli ping`, `rabbitmq`'s `rabbitmq-diagnostics ping`) already reported healthy first.

## Gotchas & deviations

- **The Gateway's `AddHealthChecks()` call takes no arguments at all.** [Gateway.Api/Program.cs](../../Gateway/Gateway.Api/Program.cs) has `builder.Services.AddHealthChecks();` with zero chained `.Add*()` calls — its `/health` endpoint reports "healthy" unconditionally as long as the process is running; it says nothing about whether any downstream service it routes to is actually reachable. A green gateway health check does not imply a green `identity-service`.
- **Cart has no SQL Server health check package referenced at all**, not merely an unused one — `AspNetCore.HealthChecks.SqlServer` doesn't appear in [Cart.API.csproj](../../Services/Cart/Cart.Api/Cart.API.csproj), consistent with Cart having zero SQL Server dependency anywhere in its stack (see [Cart-Service.md](../Architecture/Cart-Service.md)).
- **The Gateway's own `/health` is wired via inline `UseHealthChecks("/health")`, not `MapHealthChecks`,** unlike every other service — because `UseOcelot()` is terminal and would otherwise swallow an endpoint-routed health check before it ever ran. See [08-ocelot-gateway.md](./08-ocelot-gateway.md) for the full explanation.
- **No degraded/unhealthy differentiation is used anywhere** — every `.Add*()` call in every service uses the library defaults with no custom `failureStatus`, tags, or `HealthCheckOptions` (e.g. no custom JSON response writer). A single failing dependency reports the whole service `Unhealthy`, which is what Docker's `curl -f` check treats as a non-2xx failure regardless of how granular the underlying check results actually are.
