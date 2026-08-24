# Serilog Structured Logging — `Serilog.AspNetCore`

## Abstract

Every ShopFlow service's Api project references `Serilog.AspNetCore`, and four of the five actually wire it up in `Program.cs` to replace the framework's default `ILogger` pipeline with structured, sink-based logging. This file covers what Serilog is, why a distributed system of five services benefits from structured logs over plain text, the real setup code (identical across Product, Cart, Order, Notification, and the Gateway), and a genuine deviation found while reading the code: **Identity Service references the package but never calls `UseSerilog`.**

## What it is

Serilog is a structured logging library for .NET built around the idea that a log event isn't a formatted string — it's a message template plus a set of named properties (`"Handling {RequestName}"` with `RequestName` as a real, queryable field, not text baked into the message). Those events flow through a configurable pipeline of **sinks** (Console, files, Seq, Elasticsearch, etc.) rather than being written directly to one destination. `Serilog.AspNetCore` is the integration package that lets `Serilog` replace `Microsoft.Extensions.Logging`'s default provider via `builder.Host.UseSerilog(...)`, and adds `UseSerilogRequestLogging()` — one line of middleware that logs a single structured summary event per HTTP request (method, path, status code, elapsed time) instead of ASP.NET Core's own noisier per-request `Information`-level chatter.

## Why ShopFlow uses it

Plain `ILogger` text logs are fine for reading one service's console output live, but across five independently-running containers, grepping unstructured text for "which service, which request, what happened" doesn't scale. Serilog's message-template + property model means every log line carries queryable fields from the start — `RequestName`, elapsed time, status code — so even with only `WriteTo.Console()` configured today (no Seq/Elasticsearch sink yet), the events are already shaped for a future centralized log aggregator to ingest without changing any logging call sites. It also unifies ASP.NET Core's internal framework logs and each service's own `LoggingBehavior` pipeline logs (see [dotnet-backend-conventions](../../.claude/skills/dotnet-backend-conventions) for the MediatR pipeline) into the same sink and the same structured format.

## How it's used

### The real setup — identical across four services

[Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs), [Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs), [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs), [Notification.Api/Program.cs](../../Services/Notification/Notification.Api/Program.cs), and [Gateway.Api/Program.cs](../../Gateway/Gateway.Api/Program.cs) all open with the exact same block, verbatim:

```csharp
// ── Logging ──────────────────────────────────────────────────────────────────

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console());
```

`ReadFrom.Configuration(context.Configuration)` picks up the `Serilog` section of `appsettings.json` (minimum level, overrides — see below); `ReadFrom.Services(services)` lets any registered `ILogEventEnricher`/sink extensions resolve their own DI dependencies; `WriteTo.Console()` is the only sink configured anywhere in ShopFlow today — every service's structured logs currently go to stdout only (which is exactly what Docker captures via `docker compose logs`).

Later in the same files, `UseSerilogRequestLogging()` is added to the middleware pipeline, before the exception-handling middleware in the HTTP-serving services — from [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs):

```csharp
app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionHandlingMiddleware>();
```

The Gateway places it first in its pipeline too, ahead of CORS/auth/Ocelot (see [08-ocelot-gateway.md](./08-ocelot-gateway.md)), so every request — routed or rejected — gets one summary log line.

### The `appsettings.json` `Serilog` section

[Product.Api/appsettings.json](../../Services/Product/Product.Api/appsettings.json), [Cart.Api/appsettings.json](../../Services/Cart/Cart.Api/appsettings.json), [Order.Api/appsettings.json](../../Services/Order/Order.Api/appsettings.json), and [Notification.Api/appsettings.json](../../Services/Notification/Notification.Api/appsettings.json) all carry the identical section:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    }
  },
  "AllowedHosts": "*"
}
```

Both a plain `Logging` section (the framework default, effectively unused once `UseSerilog` takes over the `ILogger` provider) and a `Serilog` section coexist here — `ReadFrom.Configuration` reads the latter. `Microsoft.AspNetCore` is overridden down to `Warning` in every file so ASP.NET Core's own routing/hosting `Information`-level noise doesn't drown out application events; the one structured request-summary line from `UseSerilogRequestLogging()` still gets through regardless of that override, since it's logged at `Information` by Serilog's own request-logging middleware, not by the ASP.NET Core framework logger category being suppressed.

### Application-level structured logging — `LoggingBehavior`

Serilog's structured-property model is also what backs the MediatR pipeline behavior every service registers — e.g. `Product.Application`'s `LoggingBehavior<TRequest,TResponse>` logs `"Handling {RequestName}"` / `"Handled {RequestName}"` around every command/query, with `RequestName` captured as a real property rather than string-concatenated, exactly the pattern Serilog is designed for.

## Gotchas & deviations

- **Identity Service references `Serilog.AspNetCore` but never calls `UseSerilog` or `UseSerilogRequestLogging`.** [Identity.API.csproj](../../Services/Identity/Identity.Api/Identity.API.csproj) lists `<PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />`, but [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs) has no `using Serilog;`, no `builder.Host.UseSerilog(...)`, and no `app.UseSerilogRequestLogging()` anywhere in the file — confirmed by reading it in full. Its [appsettings.json](../../Services/Identity/Identity.Api/appsettings.json) also has no `Serilog` section, only the plain `Logging` block. This means Identity Service is the one service in ShopFlow still running on the framework's default `ILogger`/console provider — its logs are plain text, not structured events, and there's no per-request summary line the way every other service has. Whether this is an intentional omission or an incomplete migration isn't stated anywhere in the codebase; it should be treated as a known gap, not a deliberate architectural choice, since every other service (including the event-driven, no-HTTP-controllers Notification Service) does wire it up.
- **`WriteTo.Console()` is the only sink anywhere.** No file sink, no Seq, no Elasticsearch/OpenSearch sink is configured in any service — "structured" here currently means "structured on stdout," not yet centrally aggregated. Log correlation across services (e.g. tracing one request through Gateway → Product → RabbitMQ → Cart) would today require manually correlating timestamps across `docker compose logs` output from separate containers; there's no shared request/correlation ID enrichment (`Serilog.Enrichers.*`) configured anywhere.
