# Notification Service — Full Architecture Documentation

## Abstract

The Notification Service is made up of five .NET projects — three production projects and only two matching test projects — and does exactly one thing: listen for `OrderPlacedEvent` on RabbitMQ and email the customer an order confirmation. It's the leanest service in ShopFlow by a wide margin, and deliberately so, unlike the 8-project shape every other service (Identity, Product, Cart, Order — see their own docs in this folder) follows.

**What each project is, and why it's relevant:**

| Project | What it is | Why it exists |
| --- | --- | --- |
| `Notification.Application` | One interface (`IEmailService`) and one static email template class — **zero NuGet packages** | There's no MediatR here, because there's no controller to dispatch from; no FluentValidation, because there's no free-form user input to validate. The only "use case" is triggered entirely by an inbound event, not a request. |
| `Notification.Infrastructure` | `OrderPlacedConsumer` (MassTransit), `MailKitEmailService` (real SMTP), `EmailSettings` | Makes the one use case work against a real message bus and a real mail server, behind the interface Application declared. |
| `Notification.Api` | A full ASP.NET Core host with **no controllers at all** beyond the framework-provided `/health` | Exists purely so this service has something for Docker's healthcheck to probe — see the [Overview](#overview) for why a bare Worker Service wouldn't satisfy that. |

**No `Notification.Domain[.Tests]`, no `Notification.Api.Tests`** — both are deliberate scope decisions, confirmed in [Phase5.md](../Phases/Phase5.md), not oversights: nothing in this service has an entity, an invariant, or anything that throws a domain exception, so there's nothing for a Domain layer to hold; and there are no controllers beyond `/health` to justify a dedicated `Api.Tests` project (that one boot-check instead lives inside `Notification.Infrastructure.Tests` — see [§4](#4-test-projects)).

**How they're related, and why:**

The same directed dependency chain as every other service, just two layers shorter:

```text
Notification.Application    IEmailService, email template — zero NuGet packages, zero project references
       ↑
Notification.Infrastructure  MassTransit consumer, MailKit SMTP client, settings — depends on Application + ShopFlow.Shared
       ↑
Notification.Api             bare composition root, no controllers — depends on Application + Infrastructure
```

`Notification.Application` declares `IEmailService` without implementing it; `Notification.Infrastructure` implements it as `MailKitEmailService`. That's the entire inversion — there's no repository interface anywhere in this service, because there's nothing to persist.

---

## Overview

The Notification Service owns exactly one responsibility: order-confirmation email. It was built in Phase 5 **concurrently and independently** of Order Service by a second contributor, made possible by the fact that Notification depends only on the already-frozen `OrderPlacedEvent` contract shape in `ShopFlow.Shared` — not on any Order Service code at all.

Per the same phase's scope decisions that shaped Order Service, shipping is out of scope this phase (FR-35/`OrderShippedEvent`), so Notification implements **only** an `OrderPlacedConsumer` — there is no `OrderShippedConsumer`, and none of `ShopFlow.Shared`'s `OrderShippedEvent` fields are read anywhere in this codebase yet.

```text
Services/Notification/
├── Notification.Application/          Notification.Application.Tests/
├── Notification.Infrastructure/       Notification.Infrastructure.Tests/
└── Notification.Api/                  (no dedicated Api.Tests project)
```

**Why `Notification.Api` is a full ASP.NET Core host and not a bare `IHostedService` Worker Service**: NFR-13 requires `/health` on every service unqualified, and a bare hosted-service process has no Kestrel listener to serve one from. So `Program.cs` is `WebApplication.CreateBuilder(args)` like every other service's API layer — it just never calls `AddControllers()`/`MapControllers()`, and registers no authentication at all. Nothing calls this service over HTTP except Docker's own healthcheck probe (confirmed by `docker-compose.yml`: `notification-service` has no host port mapping, only an internal `curl -f http://localhost:80/health` healthcheck).

**Correctness hazard avoided at design time**: Cart already binds a consumer to a RabbitMQ queue literally named `"order-placed-queue"`. If Notification's receive endpoint reused that same name, it would become a second, *competing* consumer on Cart's own queue — RabbitMQ would round-robin deliveries between the two services, so each individual `OrderPlacedEvent` would reach only one of Cart or Notification, roughly half the time each, silently breaking Cart's cart-clearing about half the time. Notification's queue is instead named **`"notification-order-placed-queue"`** — a distinct queue bound to the same `OrderPlacedEvent` exchange, so both services receive every message (true fan-out, not competing consumption). Verified live via RabbitMQ's management API: two queues on the exchange, each with exactly one consumer.

---

## 1. Notification.Application — One Interface, One Template

**[Notification.Application.csproj](../../Services/Notification/Notification.Application/Notification.Application.csproj)** — plain class library, **zero NuGet packages, zero project references**. The starkest contrast with every other service's Application layer in the repo: no MediatR (no controller ever dispatches into this layer — the only trigger is a consumed event, handled directly, the same "call the interface straight from the consumer" pattern Cart's own `OrderPlacedConsumer` already established), no FluentValidation (there's no free-form request body anywhere in this service to validate).

### Interfaces

**[IEmailService](../../Services/Notification/Notification.Application/Interfaces/IEmailService.cs)**:

```csharp
public record OrderLineItem(string ProductName, decimal UnitPrice, int Quantity);

public interface IEmailService
{
    Task SendOrderConfirmationAsync(
        string toEmail, Guid orderId, List<OrderLineItem> items, decimal total, CancellationToken ct);
}
```

**Exactly one method** — a direct consequence of the shipping-deferred scope decision, the same reasoning that left Order's `IOrderEventPublisher` with only one method. `OrderLineItem` is defined here, deliberately **not** reusing `ShopFlow.Shared.Events.OrderItemDto`, so that `Notification.Application` stays entirely free of any reference to the shared events library — the wire-format concern lives only in Infrastructure (mirroring the same seam Order's `IOrderEventPublisher` draws, just in the consuming direction instead of the publishing one).

### Templates

**[OrderConfirmationEmailTemplate](../../Services/Notification/Notification.Application/Templates/OrderConfirmationEmailTemplate.cs)** — a static class, two pure functions:

- `Subject(orderId)` → `"Order Confirmation - {orderId}"`
- `Body(orderId, items, total)` → a plain-text (not HTML) itemized breakdown, built with a `StringBuilder`: a thank-you line, one line per item (`  - {ProductName} x{Quantity} @ {UnitPrice:F2} = {lineTotal:F2}`), then a blank line and `Total: {total:F2}` — all money formatted via `ToString("F2", CultureInfo.InvariantCulture)`.

Plain text rather than HTML — matching this project's stub-quality ethos elsewhere (e.g. Identity's payment-confirmation stub) — and it needs no templating-engine dependency as a result. An empty `items` list doesn't throw; the body just skips straight from "Order summary:" to the blank line and total.

---

## 2. Notification.Infrastructure — Event Consumer, Real SMTP, Settings

**[Notification.Infrastructure.csproj](../../Services/Notification/Notification.Infrastructure/Notification.Infrastructure.csproj)** references Application + **`ShopFlow.Shared`**, plus `MailKit` (**new to the repo this phase, first use**), `MassTransit.RabbitMQ` (pinned `8.5.10`, same reason as Cart/Order), `Microsoft.Extensions.Options`. No EF Core, no StackExchange.Redis — this service has neither a database nor a cache.

### Events

**[OrderPlacedConsumer : IConsumer\<OrderPlacedEvent\>](../../Services/Notification/Notification.Infrastructure/Events/OrderPlacedConsumer.cs)** — the same shape as Cart's consumer of the same name and same event: constructor-injects `IEmailService`, calls it directly from `Consume()`, no MediatR in between. Maps each `OrderItemDto` from the event to an `OrderLineItem`, then calls `SendOrderConfirmationAsync(message.CustomerEmail, message.OrderId, items, message.Total, ct)`. An event with zero items still sends an email — `SendOrderConfirmationAsync` receives an empty list and a `0m` total rather than being skipped.

### Email

**[MailKitEmailService : IEmailService](../../Services/Notification/Notification.Infrastructure/Email/MailKitEmailService.cs)** — the one class in the repo that talks real SMTP: builds a `MimeMessage` (`From`/`To`/`Subject` from settings + the template, `Body` as `TextPart("plain")`), then `SmtpClient.ConnectAsync` → `AuthenticateAsync` → `SendAsync` → `DisconnectAsync`, all against `MailKit.Net.Smtp.SmtpClient` with `SecureSocketOptions.Auto`.

### Settings

**[EmailSettings](../../Services/Notification/Notification.Infrastructure/Settings/EmailSettings.cs)** — `Host`, `Port` (`int`), `From`, `Password`, all `init`-only, mirroring `JwtSettings`' shape everywhere else in the repo. **Bound differently from every other settings class in ShopFlow**: rather than `Configure<EmailSettings>(section)` against a nested config section, `Program.cs` reads four flat top-level keys — `SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM`, `SMTP_PASSWORD` — the same keys `.env.example` already provisions — and builds one `EmailSettings` instance via an object initializer, registered directly as `IOptions<EmailSettings>` through `Options.Create(...)`. `Port` falls back to `25` if the config value doesn't parse as an `int`; `Host` falls back to `"localhost"`; `From` falls back to `"noreply@shopflow.com"`; `Password` falls back to an empty string.

---

## 3. Notification.Api — Bare Composition Root, No Controllers

**[Notification.API.csproj](../../Services/Notification/Notification.Api/Notification.API.csproj)** (`Sdk="Microsoft.NET.Sdk.Web"`) references Application + Infrastructure, plus only `AspNetCore.HealthChecks.Rabbitmq`, `MassTransit.RabbitMQ`, `Serilog.AspNetCore`. **No MediatR, no FluentValidation, no Swashbuckle, no `Microsoft.AspNetCore.Authentication.JwtBearer`** — nothing in this package list assumes there's a controller, a validated request, an API doc, or an authenticated caller, because none of those exist here.

### Endpoints

```text
GET    /health    → 200 OK — health status (RabbitMQ only)
```

The only route this service serves. No `[Authorize]` anywhere in the project — there is nothing to authorize.

### Composition root — Program.cs

**[Program.cs](../../Services/Notification/Notification.Api/Program.cs)**:

- Registers `EmailSettings` as described in [§2](#2-notification-infrastructure--event-consumer-real-smtp-settings) — a `Singleton` built once via `Options.Create(...)`, not the usual `Configure<T>(section)` mutation pattern every other settings class in the repo uses.
- Registers `IEmailService` as **Scoped** (`MailKitEmailService`).
- **`AddMassTransit`**: `AddConsumer<OrderPlacedConsumer>()`; `UsingRabbitMq(...)` bound to `RabbitMQ:Host`/`User`/`Pass` (fallback `localhost`/`guest`/`guest`); `ReceiveEndpoint("notification-order-placed-queue", ...)` with the same `UseMessageRetry(r => r.Exponential(3, 1s, 10s, 2s))` policy Cart and Order both use.
- Registers `/health` against **RabbitMQ only** — no SQL Server, no Redis, since this service has neither.
- **No `AddControllers()`, no `AddAuthentication()`, no `AddSwaggerGen()`, no JWT bearer configuration at all** — the shortest `Program.cs` composition root of any service in the repo.
- `app.MapHealthChecks("/health")` is the only endpoint mapping call — no `MapControllers()`.
- `public partial class Program { }` at the bottom, for `WebApplicationFactory<Program>` — used only by `HealthCheckTests` in the Infrastructure.Tests project (see [§4](#4-test-projects)), since there's no dedicated Api.Tests project here.

---

## 4. Test Projects

| Test project | Targets | Style | Notable packages |
| --- | --- | --- | --- |
| **Notification.Application.Tests** (5 tests) | `Notification.Application` | Pure unit, no mocks — `OrderConfirmationEmailTemplateTests` asserts on the generated subject/body strings directly (order ID present, each item's name/quantity present, formatted total present, empty-items case doesn't throw and still shows the total) | xunit, FluentAssertions |
| **Notification.Infrastructure.Tests** (5 tests) | `Notification.Infrastructure` **and** `Notification.Api` (both referenced) | Real containers, no mocked infrastructure at the boundary — NFR-25's "real infra over mocks" philosophy extended from SQL/Redis/RabbitMQ to SMTP | + NSubstitute, `Microsoft.AspNetCore.Mvc.Testing`, **`Testcontainers`** (generic, not a dedicated module) |

**Notification.Infrastructure.Tests breakdown**:
- `OrderPlacedConsumerTests` (2, NSubstitute `IEmailService` + a real in-process MassTransit bus via `AddMassTransitTestHarness`) — publishing `OrderPlacedEvent` with items triggers a correctly-mapped confirmation email; publishing one with zero items still sends an email, with an empty list and a `0m` total.
- `MailKitEmailServiceTests` (2, against a **real `rnwood/smtp4dev:v3`** container via the generic `Testcontainers` package — no dedicated Testcontainers SMTP module exists) — sending actually delivers a message with the correct recipient/subject; the delivered body actually contains the item name and formatted total. Both poll smtp4dev's real REST contract (`GET /api/messages`, `GET /api/messages/{id}/plaintext`) directly — that shape was confirmed by running the container and inspecting it, not guessed.
- `HealthCheckTests` (1) — **folded into this project rather than standing up a fifth, near-empty `Api.Tests` project**, the concrete reason there is no `Notification.Api.Tests` anywhere in the solution. Spins up `Notification.Api`'s real `Program` via `WebApplicationFactory<Program>`, swaps MassTransit for the test harness (same descriptor-removal trick `CartApiFactory`/`OrderApiFactory` use) and clears all health check registrations (`HealthCheckServiceOptions.Registrations.Clear()`) so `/health` returns `200` without needing a live RabbitMQ — a fast, network-free confirmation that the host actually boots and serves the one route this service has. The real RabbitMQ health check and broker wiring are exercised separately, via the live Docker Compose round-trip described in [Phase5.md](../Phases/Phase5.md), not by this test.

**This is why `Notification.Infrastructure.Tests` references `Notification.Api` as a project** — the only test project in the repo that reaches across into its own service's Api layer, rather than a dedicated `*.Api.Tests` project doing so.

---

## 4.5 Project Dependency Wiring

```text
┌───────────────────────────────────────────────────────────────────┐
│                      Notification Service                          │
│                                                                     │
│   Production Code                     Test Projects                │
│   ───────────────                     ─────────────                │
│                                                                     │
│   ┌────────────────────┐             ┌───────────────────────────┐│
│   │ Notification.API   │◄────────────│ Notification.Infra.Tests   ││
│   └──────────┬──────────┘   refs     │ (also refs Notification.   ││
│              │ refs                  │  Infrastructure directly)  ││
│              ▼                       └──────────────┬─────────────┘│
│   ┌─────────────────────────┐                       │ refs         │
│   │ Notification.Infrastructure │◄─────────────────────┘              │
│   └──────────┬───────────────┘                                     │
│              │ refs                    ┌──────────────────────────┐│
│              ▼                         │ Notification.App.Tests   ││
│   ┌─────────────────────────┐          └─────────────┬────────────┘│
│   │ Notification.Application │◄──────────────────────┘ refs        │
│   └─────────────────────────┘                                     │
│         (no deps)                                                  │
│                                                                     │
│   Notification.Infra also refs ──► Shared/ShopFlow.Shared          │
│   (no Notification.Domain[.Tests], no Notification.Api.Tests)      │
└───────────────────────────────────────────────────────────────────┘
```

| Project | References |
| --- | --- |
| `Notification.Application` | — |
| `Notification.Infrastructure` | `Notification.Application` + `ShopFlow.Shared` |
| `Notification.API` | `Notification.Application` + `Notification.Infrastructure` |
| `Notification.Application.Tests` | `Notification.Application` |
| `Notification.Infrastructure.Tests` | `Notification.Infrastructure` **+ `Notification.API`** |

Two structural differences from every other service's wiring table: there's no Domain layer at the bottom of the chain at all, and the test-project-to-production-project mapping is not one-to-one — `Notification.Infrastructure.Tests` is the only test project in the repo that reaches into two production projects directly.

---

## 5. Request Flow — End to End Example

There is no HTTP request to trace here — the only flow this service has starts from a message already in flight:

1. **Order Service** confirms an order (`PUT /api/orders/{id}/confirm`) and publishes `OrderPlacedEvent` via `IPublishEndpoint` — see [Order-Service.md §6](./Order-Service.md#6-request-flow--end-to-end-example).
2. **Infrastructure**: MassTransit delivers the event to this service's own `notification-order-placed-queue` receive endpoint — a completely independent delivery from whatever Cart's `order-placed-queue` receives, since both are separate queues bound to the same exchange (fan-out, not competing consumption — see [Overview](#overview)).
3. `OrderPlacedConsumer.Consume` maps `message.Items` (`List<OrderItemDto>`) to `List<OrderLineItem>` and calls `IEmailService.SendOrderConfirmationAsync(message.CustomerEmail, message.OrderId, items, message.Total, ct)` — directly, with no MediatR pipeline, no validation behavior, no logging behavior in between, since **Application** has none of those to offer.
4. **Infrastructure**: `MailKitEmailService` builds the message via `OrderConfirmationEmailTemplate.Subject`/`Body` (**Application**'s only other piece), connects to the configured SMTP host, authenticates, and sends.
5. Nothing is returned to anyone — there's no caller waiting on a response. Success or failure here is only ever observed via logs, via MassTransit's own retry policy (three attempts, exponential backoff) if `SendAsync` throws, or — in the live end-to-end verification documented in [Phase5.md](../Phases/Phase5.md) — via smtp4dev's own inbox.

This is the shortest, flattest flow of any service in the repo: two projects, one interface call, no layer does any branching at all beyond the item-mapping `Select`.

---

## 6. Configuration & Running

Configuration lives in [appsettings.Development.json](../../Services/Notification/Notification.Api/appsettings.Development.json) — `RabbitMQ:Host/User/Pass` plus the four **flat** `SMTP_HOST`/`SMTP_PORT`/`SMTP_FROM`/`SMTP_PASSWORD` keys (not nested under an `EmailSettings` section — see [§2](#2-notification-infrastructure--event-consumer-real-smtp-settings)). The base `appsettings.json` has none of these, the same "Development-only for now" pattern as Cart/Order/Product. **There is no `Properties/launchSettings.json`** for this service — it has no meaningful HTTP profile to launch beyond the default Kestrel binding.

```bash
docker compose up -d rabbitmq smtp4dev
dotnet run --project Services/Notification/Notification.Api
```

- No externally-published port in `docker-compose.yml` for `notification-service` itself — only an internal `curl -f http://localhost:80/health` container healthcheck; nothing outside Docker's own health monitoring ever calls this service over HTTP
- `smtp4dev` web UI: `http://localhost:5099` — the dev-only SMTP catcher where confirmation emails this service actually sends can be viewed (both a web UI and the REST API `MailKitEmailServiceTests` polls)
- No Swagger — there's nothing to document
- `dotnet test ShopFlow.sln` — `Notification.Infrastructure.Tests` needs Docker running (Testcontainers spins up a generic `rnwood/smtp4dev:v3` container; no SQL Server, no Redis)

---

## Summary — what each layer answers

| Layer | Answers |
| --- | --- |
| `Notification.Application` | What does an order-confirmation email say, and what's the one operation needed to send it? |
| `Notification.Infrastructure` | How is that fulfilled — a RabbitMQ consumer on its own dedicated queue, a real SMTP client, flat env-var-driven settings? |
| `Notification.Api` | How does this service stay alive and observable — with literally nothing else to expose over HTTP? |
