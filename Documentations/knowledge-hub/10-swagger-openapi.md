# Swagger / OpenAPI — `Swashbuckle.AspNetCore` + `Microsoft.AspNetCore.OpenApi`

## Abstract

Four of ShopFlow's five Api projects — Identity, Product, Cart, and Order — register both `Swashbuckle.AspNetCore` (Swagger UI + `/swagger.json` generation) and `Microsoft.AspNetCore.OpenApi` (the framework's own `AddOpenApi`/`MapOpenApi`) side by side, with a JWT bearer security scheme wired into the generated spec. Notification Service has neither, since it has no HTTP surface beyond `/health`. This file covers what Swagger/OpenAPI is, why it matters across five independently-built services, and the exact `AddSwaggerGen`/`UseSwagger` code found in each service's `Program.cs`.

## What it is

OpenAPI is a machine-readable specification format (JSON/YAML) describing an HTTP API's routes, request/response shapes, and auth requirements. Swagger UI is an interactive web page generated from that spec, letting a developer browse every endpoint and fire real requests from the browser without writing a client. `Swashbuckle.AspNetCore` generates the OpenAPI document from ASP.NET Core's own controller/action metadata (`[HttpPost]`, route templates, model binding) and serves the UI; `Microsoft.AspNetCore.OpenApi` is Microsoft's own, newer, lighter-weight document generator (`AddOpenApi()`/`MapOpenApi()`) that ships in the framework itself. ShopFlow registers both in the same four services — Swashbuckle for the interactive `/swagger` UI, `AddOpenApi()`/`MapOpenApi()` for the underlying `/openapi/v1.json` document — rather than picking one exclusively.

## Why ShopFlow uses it

With four independently-built HTTP services (Identity, Product, Cart, Order) that a client — or another developer on the team — needs to call correctly, a live, always-current interactive spec is far more useful during development than reading controller source or a hand-maintained Postman collection. It's also the fastest way to manually exercise an endpoint with a real bearer token during development, which is why the JWT bearer scheme is wired directly into the generated spec (see below) rather than left for a developer to configure a token manually on every request.

## How it's used

### The real registration — identical shape across four services

From [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs) (byte-for-byte the same in [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs), [Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs), and [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs)):

```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token. Example: eyJhbG..."
    });
    opts.AddSecurityRequirement(doc =>
    {
        var requirement = new OpenApiSecurityRequirement();
        requirement.Add(new OpenApiSecuritySchemeReference("Bearer", doc), new List<string>());
        return requirement;
    });
});
builder.Services.AddOpenApi();
```

`AddEndpointsApiExplorer()` is what lets Swashbuckle discover minimal-API/controller endpoints via ASP.NET Core's own `ApiExplorer` metadata. `AddSecurityDefinition("Bearer", ...)` declares an HTTP-scheme security definition named `Bearer` with `BearerFormat: "JWT"` — this is what makes Swagger UI show an **Authorize** button where a developer pastes a raw JWT (no need to type `Bearer <token>`, just the token itself, per the `Description` hint). `AddSecurityRequirement` then applies that definition globally to every operation in the generated document, so every endpoint in the UI shows a lock icon and accepts the same pasted token — even the public ones (there's no per-operation opt-out coded here; the security requirement is document-wide, not scoped to only the `[Authorize]`-decorated actions).

### Wiring it into the pipeline — Development only

Every one of the four services gates both the Swashbuckle UI and the framework's own OpenAPI endpoint behind an environment check, immediately after `app.Build()`:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

(verbatim from [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs); identical in [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs), [Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs), and [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs)) — meaning the interactive UI and the raw spec document are both unavailable in any non-Development environment (`ASPNETCORE_ENVIRONMENT` is set to `Development` in every service's block in [docker-compose.yml](../../docker-compose.yml), so this is on by default in the local stack). `app.MapOpenApi()` exposes the framework-generated document at `/openapi/v1.json`; `app.UseSwagger()` exposes Swashbuckle's own generated document (by default at `/swagger/v1/swagger.json`); `app.UseSwaggerUI()` serves the interactive page.

### The `/swagger` endpoint

`UseSwaggerUI()` with no options mounts the interactive page at the default path, `/swagger` (redirecting from `/swagger/index.html`). Per [Cart-Service.md](../Architecture/Cart-Service.md)'s configuration section, Cart's own dev instance is reachable at `http://localhost:5019/swagger`; the same default path applies to Identity (`5015`), Product (`5016`), and Order (`5020`) per their respective [launchSettings.json](../../Services/Cart/Cart.Api/Properties/launchSettings.json) dev ports. No service overrides `RoutePrefix` or the generated document's route template, so `/swagger` is consistent across all four.

## Gotchas & deviations

- **No custom `SwaggerDoc` title/version is registered anywhere.** All four `AddSwaggerGen` calls configure only the `Bearer` security definition/requirement — none call `opts.SwaggerDoc("v1", new OpenApiInfo { Title = ..., Version = ... })`. Every service's generated document therefore falls back to Swashbuckle's own default document metadata rather than a ShopFlow-specific title distinguishing, say, "Product Service API" from "Order Service API" in the UI's own header.
- **Notification Service has no Swagger/OpenAPI wiring at all** — no `Swashbuckle.AspNetCore` or `Microsoft.AspNetCore.OpenApi` package reference in [Notification.API.csproj](../../Services/Notification/Notification.Api/Notification.API.csproj), consistent with it having no HTTP controllers to document; its only route is `/health`.
- **The security requirement is applied document-wide, not per-operation.** Because `AddSecurityRequirement` isn't scoped to only `[Authorize]`-decorated actions, Swagger UI shows the same lock icon on genuinely public endpoints (e.g. Product's `GET /api/products`) as on protected ones — cosmetically implying auth is needed everywhere, even though the actual `[Authorize]`/policy attributes (or their absence) on the controller action are what really gate the request.
- **Gateway (`Gateway.Api`) has no Swagger/OpenAPI setup of its own** — it has no controllers to document (it's pure Ocelot routing, see [08-ocelot-gateway.md](./08-ocelot-gateway.md)), so there is no aggregated, gateway-level API document; a developer exploring the full public surface has to open each service's own `/swagger` individually, or rely on the Postman collection under [Documentations/postman/](../../Documentations/postman/).
