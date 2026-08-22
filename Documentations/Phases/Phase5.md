# Phase 5 — Order Service + Notification Service

## Project Structure

```text
Services/Order/
├── Order.Domain/                 Order.Domain.Tests/
├── Order.Application/            Order.Application.Tests/
├── Order.Infrastructure/         Order.Infrastructure.Tests/
└── Order.Api/                    Order.Api.Tests/

Services/Notification/
├── Notification.Application/          Notification.Application.Tests/
├── Notification.Infrastructure/       Notification.Infrastructure.Tests/
└── Notification.Api/
```

Order mirrors Product's 8-project, SQL-backed shape. Notification is a deliberately leaner 5-project shape — no `Domain[.Tests]` (nothing here has an entity, invariant, or anything that throws a domain exception) and no `Api.Tests` (no controllers exist beyond the framework-provided `/health`).

**Naming**: entities are `OrderEntity`/`OrderItemEntity`, not `Order`/`OrderItem` — same `CS0118` namespace-collision workaround `ProductEntity` already established in Phase 3 (every project in the service is rooted `Order.*`).

Five decisions were confirmed with the user before implementation (all changed the scope from a literal reading of the spec):

1. **No `RequireCustomer` policy.** The spec's `GET /api/orders [RequireCustomer]` uses a policy that doesn't exist anywhere in the codebase. Resolved as plain `[Authorize]` with ownership enforced via the `userId` JWT claim — matching how Cart and Product's vendor endpoints already handle "my own resource" access. A role gate would have been wrong here anyway (it would block Admins from customer-scoped routes).
2. **Shipping is deferred to a later phase.** FR-35 requires publishing `OrderShippedEvent` on shipment, but no ship endpoint is documented anywhere in the spec. Order Service in this phase only reaches `Pending → Confirmed`. No `Ship()` domain method, no `ShipOrderCommand`, no ship endpoint, and — following from this — Notification Service implements only `OrderPlacedConsumer`, not `OrderShippedConsumer`.
3. **`OrderShippedEvent` gained a `CustomerEmail` field**, added now even though shipping is deferred — safe, since nothing consumes this event yet, and it's groundwork for whichever later phase adds shipping.
4. **Identity Service gained a new endpoint**, `POST /api/auth/verify-email`. Investigation found `ApplicationUser.VerifyEmail()` existed and was unit-tested but was never called by any command/handler/controller — not even the seeded admin had `emailVerified: true`. This meant `POST /api/orders [RequireVerifiedEmail]` was unsatisfiable by any real login. Fixed as a small, isolated addition to the already-shipped Identity Service (see below).
5. Notification Service builds only the order-confirmation path this phase (FR-36); the shipped-email path (FR-37) moves alongside decision #2.

**Correctness hazard found and avoided**: Cart already binds a consumer to a RabbitMQ queue literally named `"order-placed-queue"`. Notification's receive endpoint uses a distinct name, `"notification-order-placed-queue"` — reusing Cart's name would have made Notification a second *competing* consumer on Cart's own queue (round-robin delivery) rather than an independent subscriber, silently breaking Cart's cart-clearing about half the time. Verified live (see below) that both queues exist, each with exactly one consumer.

---

## Identity Addendum

✅ implemented — `Identity.Application/Commands/VerifyEmailCommand.cs` + `VerifyEmailCommandHandler.cs` (mirrors `AssignRoleCommand`'s shape: load user, mutate, persist). `AuthController` gained `POST /api/auth/verify-email [Authorize]`, reading `userId` from the JWT claim. Like role assignment, it doesn't reissue a token — the caller re-logs-in afterward for a JWT with `emailVerified: true` (the same two-step pattern the Postman suite already used for "Admin: Assign Role" → "Login (post role change)"). +4 tests (2 handler, 2 controller).

## Shared Library Change

✅ implemented — `Shared/ShopFlow.Shared/Events/OrderShippedEvent.cs` gained a `CustomerEmail` field: `OrderShippedEvent(Guid OrderId, string CustomerEmail, string TrackingNumber, DateTime ShippedAt)`. Confirmed zero existing consumers before changing it (unlike `OrderPlacedEvent`, which Cart already depends on).

---

## Order Service

### Domain Layer

**Entities**: ✅ implemented

- `OrderEntity` — `Id, CustomerId, CustomerEmail, Status, TotalAmount, CreatedAt, UpdatedAt, OrderItems[]`. `Create(customerId, customerEmail, items)` validates and computes `TotalAmount` from items, sets `Pending`. `Confirm()` guards `Status == Pending` (else `DomainException`), sets `Confirmed`. `CustomerEmail` is captured once at placement (from the JWT) and persisted as a snapshot — same philosophy as `OrderItemEntity.ProductName`/`UnitPrice`.
- `OrderItemEntity` — `Id, OrderId, ProductId, ProductName, UnitPrice, Quantity`. `Create(...)` validates name non-blank, price ≥ 0, quantity ≥ 1.

**Enums**: `OrderStatus` — `Pending | Confirmed | Shipped | Delivered | Cancelled`, the full enum per the spec's domain model, but only `Pending`/`Confirmed` are reachable by any code path this phase — no `Ship()`/`Deliver()`/`Cancel()` method exists, since nothing in this phase's FRs drives them (same treatment Phase 3 already gave a couple of Product's would-be transitions).

**Exceptions**: ✅ implemented — `DomainException`, `NotFoundException` (copied from Product, namespace-adjusted).

### Application Layer

**Commands + Handlers:**

| Command | Handler responsibility | Status |
| --- | --- | --- |
| `PlaceOrderCommand` | Maps request items to `OrderItemEntity`, `OrderEntity.Create(...)`, `IOrderRepository.AddAsync`. No event published — FR-34 ties `OrderPlacedEvent` to confirmation, not placement | ✅ Done |
| `ConfirmOrderCommand` | Loads by id (`NotFoundException` if missing or not owned — ownership-mismatch-as-404, matching `UpdateProductCommandHandler`'s precedent); `order.Confirm()`; persists; publishes `OrderPlacedEvent` via `IOrderEventPublisher` | ✅ Done |

**Queries + Handlers:**

| Query | Handler responsibility | Status |
| --- | --- | --- |
| `GetOrderByIdQuery` | Loads with items; `NotFoundException` if missing or (`!IsAdmin && order.CustomerId != RequesterId`) | ✅ Done |
| `GetMyOrdersQuery` | Returns the caller's own orders | ✅ Done |
| `GetAllOrdersQuery` | Returns every order — admin-only, enforced at the controller; no pagination (matches `GetProductListQuery`) | ✅ Done |

**DTOs**: ✅ implemented — `OrderDto`, `OrderItemDto` (response, has `Id`), `OrderItemRequestDto` (request, no `Id`). `Status` is exposed as a string (`order.Status.ToString()`), matching how `UserRole` is exposed in Identity's `AuthResponse`/`UserProfileDto`.

**Interfaces**: ✅ implemented

- `IOrderRepository` — `GetByIdAsync` (must `Include(OrderItems)`), `GetByCustomerIdAsync`, `GetAllAsync`, `AddAsync`, `UpdateAsync`.
- `IOrderEventPublisher` — **`PublishOrderPlacedAsync` only.** Declared in Application, implemented in Infrastructure, keeping `ShopFlow.Shared` out of `Order.Application`'s dependency graph entirely — matching Cart's actual architecture (`Shared` referenced only by `Cart.Infrastructure`), not the spec's own illustrative handler snippet (which publishes directly from the Application layer).

**Validators (FluentValidation)**: ✅ implemented — `PlaceOrderCommandValidator` (items not empty; per-item `ProductName` not blank, `UnitPrice ≥ 0`, `Quantity ≥ 1` via `RuleForEach`). No validator for `ConfirmOrderCommand` or any query — no free-form body to validate.

**Pipeline Behaviors**: ✅ implemented (copied from Cart/Product, namespace-adjusted) — `ValidationBehavior<TRequest,TResponse>`, `LoggingBehavior<TRequest,TResponse>`.

**Mapping**: ✅ implemented — `OrderMappingExtensions.ToDto()`, hand-written extension methods, no AutoMapper (matches `ProductMappingExtensions`).

### Infrastructure Layer

**Persistence**: ✅ implemented

- `AppDbContext` — `DbSet<OrderEntity> Orders`. `HasMany(x => x.OrderItems).WithOne().HasForeignKey(oi => oi.OrderId).OnDelete(Cascade)` — same call shape as `ApplicationUser → RefreshTokens`, EF Core's convention-based backing-field detection handles `_orderItems`/`OrderItems` without an explicit `.HasField(...)` call. This is the **first aggregate-owned one-to-many in the repo** (Category↔Product is a loose reference collection, not a true aggregate) — proven correct via `OrderRepositoryTests` before any handler code was built on top of it. `Status` via `HasConversion<int>()` (matches `UserRole`'s convention). Money columns `decimal(18,2)`. App-generated `Guid` ids.
- `OrderRepository : IOrderRepository` — `GetByIdAsync`/`GetByCustomerIdAsync`/`GetAllAsync` all eager-load via `Include(o => o.OrderItems)`.
- Database: `OrderDb` (SQL Server), created via `Database.EnsureCreated()` at Development startup — no EF Core migrations, matching every other service in this repo.

**Events**: ✅ implemented

- `OrderEventPublisher : IOrderEventPublisher` — the only class in Order that references both `MassTransit.IPublishEndpoint` and `ShopFlow.Shared.Events`, mapping `OrderEntity` → `OrderPlacedEvent`.

**Settings**: `JwtSettings` (Secret/Issuer/Audience only — Order never issues tokens), copied from Product.

### API Layer

**Endpoints**: ✅ implemented

```text
POST   /api/orders                    [Authorize(Policy = "RequireVerifiedEmail")]  → 201
GET    /api/orders                    [Authorize]                                   → 200
GET    /api/orders/{id:guid}          [Authorize]                                   → 200 / 404
PUT    /api/orders/{id:guid}/confirm  [Authorize]                                   → 200 / 404 / 400
GET    /api/admin/orders              [Authorize(Policy = "RequireAdmin")]          → 200
```

Two controllers — `OrdersController`, `AdminOrdersController` — matching Product's split-by-audience precedent (`ProductsController`/`VendorsController`). `CustomerId`/`CustomerEmail`/`IsAdmin` always read from JWT claims, never the request body.

**Middleware**: ✅ implemented — `ExceptionHandlingMiddleware`, same exception-to-status mapping as Product/Cart/Identity.

**Program.cs wiring**: ✅ implemented

- Same composition banner order as Cart/Product. `AddMassTransit` has **no `AddConsumer`, no `ReceiveEndpoint`** — Order only publishes, so this block is shorter than Cart's/Notification's.
- `RequireVerifiedEmail`/`RequireAdmin` policies copied verbatim from Identity's `Program.cs`.
- Health checks: SQL Server + RabbitMQ (`AddRabbitMQ` from the newly-added `AspNetCore.HealthChecks.Rabbitmq` package — its actual signature takes `Func<IServiceProvider, Task<IConnection>>`, built via `ConnectionFactory.CreateConnectionAsync()`; confirmed by reflecting the installed assembly rather than guessing, after an initial guessed signature failed to compile).
- JWT Bearer configured lazily via `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)`, same as every other service, so `WebApplicationFactory` test config overrides take effect.

**Health check**: ✅ implemented — `/health` (SQL Server + RabbitMQ).

### Test Projects

**Order.Domain.Tests** (14 tests): ✅ implemented — `OrderEntityTests`, `OrderItemEntityTests` (Create + guard-clause coverage for both, plus `Confirm`'s state-guard).

**Order.Application.Tests** (25 tests) — NSubstitute mocks of `IOrderRepository`/`IOrderEventPublisher`: ✅ implemented — `PlaceOrderCommandHandlerTests`, `ConfirmOrderCommandHandlerTests` (incl. not-found, ownership-mismatch, already-confirmed), `GetOrderByIdQueryHandlerTests` (incl. admin-can-view-others), `GetMyOrdersQueryHandlerTests`, `GetAllOrdersQueryHandlerTests`, `PlaceOrderCommandValidatorTests`, `ValidationBehaviorTests`, `LoggingBehaviorTests`.

**Order.Infrastructure.Tests** (6 tests) — Testcontainers, real containers: ✅ implemented

- `OrderRepositoryTests` (5, real SQL Server via `Testcontainers.MsSql`) — add/get-with-items roundtrip, unknown id, get-by-customer filters correctly, get-all, update persists a status change. First proof in the repo that an EF Core aggregate with an owned collection actually round-trips.
- `OrderEventPublisherTests` (1, real in-process MassTransit bus via `AddMassTransitTestHarness`) — confirming an order publishes `OrderPlacedEvent` with the correct `OrderId`/`CustomerEmail`/`Total`.

**Order.Api.Tests** (17 tests) — `WebApplicationFactory`: ✅ implemented

- Fixtures (mirroring Cart's/Product's pattern): `OrderApiFactory` (swaps `IOrderRepository` for `FakeOrderRepository`; removes every MassTransit-namespaced service descriptor `Program.cs` registered and replaces with `AddMassTransitTestHarness`, so API tests never attempt a real broker connection — `IOrderEventPublisher` stays wired to the real `OrderEventPublisher`, which resolves `IPublishEndpoint` from the harness instead), `FakeOrderRepository`, `JwtTokenHelper` (includes the `emailVerified` claim, unlike Product's copy — Order genuinely needs it).
- `OrdersControllerTests` (14) — place order (401 no auth, 403 unverified email, 201 happy path + total calculation, 400 empty items), get mine (401, filters to own orders only), get by id (401, 404 unknown, 200 as owner, 404 as non-owner), confirm (401, 200 + asserts `OrderPlacedEvent` was actually published via the harness, 400 already-confirmed, 404 non-owner).
- `AdminOrdersControllerTests` (3) — 401 no auth, 403 non-admin, 200 admin sees orders across all customers.

**Total: 62 tests, all passing.**

---

## Notification Service

### Application Layer

✅ implemented — `Interfaces/IEmailService.cs` (`SendOrderConfirmationAsync(toEmail, orderId, items, total, ct)` — the only method, per the shipping-deferred scope decision; `OrderLineItem` record defined alongside it, kept deliberately separate from `ShopFlow.Shared.Events.OrderItemDto` so this layer stays Shared-free). `Templates/OrderConfirmationEmailTemplate.cs` — plain text, not HTML (`Subject(orderId)`, `Body(orderId, items, total)`) — matches this project's stub-quality ethos elsewhere (the payment-confirmation stub) and needs no templating-engine dependency. Zero NuGet packages beyond the test SDK — no MediatR, no FluentValidation anywhere in this service: Cart's own `OrderPlacedConsumer` precedent calls its repository directly with nothing dispatched in between, and there's no controller here to justify MediatR's usual role, nor free-form input to validate.

### Infrastructure Layer

✅ implemented

- `Events/OrderPlacedConsumer.cs : IConsumer<OrderPlacedEvent>` — same shape as Cart's: constructor-injects `IEmailService`, calls it directly from `Consume()`, mapping `OrderItemDto` → `OrderLineItem`.
- `Email/MailKitEmailService.cs : IEmailService` — MailKit `SmtpClient` connect/authenticate/send, `TextPart("plain")` body.
- `Settings/EmailSettings.cs` — bound directly from the flat `SMTP_HOST`/`SMTP_PORT`/`SMTP_FROM`/`SMTP_PASSWORD` keys `.env.example` already provisions (not a nested config section) — since `EmailSettings` uses `init`-only properties mirroring `JwtSettings`' shape, it's built once via an object initializer in `Program.cs` and registered as `IOptions<EmailSettings>` directly, rather than through the usual `Configure<T>(section)` mutation pattern.

### API Layer

✅ implemented — `Notification.Api/Program.cs` is a full `WebApplication.CreateBuilder(args)` host, **not** a bare Worker Service: NFR-13 requires `/health` for every service unqualified, and a bare `IHostedService` host has no Kestrel listener to serve one from. No controllers, no Swagger, no authentication — nothing calls this service over HTTP except Docker's healthcheck probe. `AddMassTransit` registers exactly one consumer on `ReceiveEndpoint("notification-order-placed-queue", ...)` — deliberately **not** `"order-placed-queue"` (see the correctness hazard above). Health check: RabbitMQ only (no SQL Server, no cache — this service has neither).

### Test Projects

**Notification.Application.Tests** (5 tests): ✅ implemented — `OrderConfirmationEmailTemplateTests`, pure string assertions, no mocks.

**Notification.Infrastructure.Tests** (5 tests) — real containers, no mocked infra at the boundary (NFR-25's "real infra over mocks" philosophy, extended from SQL/Redis/RabbitMQ to SMTP): ✅ implemented

- `OrderPlacedConsumerTests` (NSubstitute `IEmailService`) — publishing triggers the confirmation email with correctly-mapped arguments.
- `MailKitEmailServiceTests` — against a real `rnwood/smtp4dev:v3` container via Testcontainers' generic `Testcontainers` package (no dedicated SMTP module exists); verified smtp4dev's actual REST contract (`GET /api/messages`, `GET /api/messages/{id}/plaintext`) by running the real container and inspecting it directly, rather than guessing the shape.
- `HealthCheckTests` (folded into this project rather than standing up a fifth, near-empty `Api.Tests` project) — `WebApplicationFactory<Program>` neutralizes the RabbitMQ health check and swaps MassTransit for the test harness, so the test stays a fast, network-free boot check.

**No `Notification.Domain[.Tests]`, no `Notification.Api.Tests`** — a deliberate scope decision, not an oversight: nothing in this service has an entity/invariant to test in isolation, and there are no controllers beyond the framework-provided `/health`.

**Total: 10 tests, all passing.**

---

## Live End-to-End Verification

Beyond the 76 new automated tests (62 Order + 10 Notification + 4 Identity addendum), the full phase was verified against real, running infrastructure:

1. **Rebuilt and restarted `identity-service`** so the new `verify-email` endpoint was actually live (the running container had been up for 2 days on the pre-Phase-5 image).
2. **Full real-login round trip**: registered a customer via `POST /api/auth/register` (real login token had `emailVerified: false`, confirming Finding A from planning was real); called `POST /api/auth/verify-email`; re-logged in and decoded the resulting JWT to confirm `emailVerified: "true"` — the first time this claim has ever been true for a real (non-synthetic-test-JWT) login in this codebase.
3. **Placed and confirmed a real order** via `order-service` (`POST /api/orders` → `Pending`, correct item snapshot and computed total; `PUT /api/orders/{id}/confirm` → `Confirmed`) against real SQL Server (`OrderDb`, schema auto-created via `EnsureCreated()` — confirmed the `Orders`/`OrderItems` tables and the `FK_OrderItems_Orders_OrderId ... ON DELETE CASCADE` constraint from container logs).
4. **RabbitMQ topology check** (`http://localhost:15672` management API) — confirmed the `ShopFlow.Shared.Events:OrderPlacedEvent` exchange has **two** bound queues, `order-placed-queue` (Cart, pre-existing) and `notification-order-placed-queue` (Notification, new), **each with exactly 1 consumer** — the concrete proof the queue-naming decision avoided the competing-consumer hazard. Confirmed Cart's queue showed 0 messages ready after a publish (i.e., Cart actually consumed and processed the event for real, not just received it).
5. **Real email delivery**: added `smtp4dev` (`rnwood/smtp4dev:v3`) to `docker-compose.yml` as a dev-only SMTP catcher; registered a second customer, verified, placed and confirmed an order, and confirmed via smtp4dev's REST API that a real email arrived — correct recipient, subject (`Order Confirmation - {orderId}`), and body (itemized breakdown + total) — sent by the real `MailKitEmailService` over a real SMTP connection, not mocked.
6. **Postman**: added an "Order" folder (13 requests: register/verify/login-refresh for a verified customer, place [+ 401/400], get mine, get by id [+ 404], confirm [+ 400 already-confirmed], admin list-all, admin route forbidden-for-customer) to `ShopFlow.postman_collection.json`, positioned after "Cart". Ran the **entire collection** (all 5 folders, 64 requests) via Newman against the live Docker stack: the new Order folder passed 13/13 requests with 0 failed assertions. (Four pre-existing failures surfaced in the Identity/Product folders, unrelated to this phase's changes — see "Known gaps" in `STATUS.md`.)
7. **`dotnet build ShopFlow.sln && dotnet test ShopFlow.sln`** — full solution, all 18 test projects, **309 tests, 0 failed**.

**Practical build sequencing**: the Identity addendum and Shared event change were done first (cheapest fix point for anything downstream). Order Service was then built fully, TDD, inside-out (Domain → Application → Infrastructure → API), by one contributor. Notification Service was built concurrently and independently by a second contributor — genuinely in parallel, as the original plan intended, since Notification only depends on the already-frozen `OrderPlacedEvent` contract shape, not on any Order Service code. Integration wiring (solution file, `docker-compose.yml`, Postman, this document) was done last, once both were complete.

---

## NuGet Packages

| Package | Project(s) | Status |
| --- | --- | --- |
| `MediatR 12.5.0` | `Order.Application`, `Order.Api` | ✅ Added |
| `FluentValidation 11.11.0` / `.DependencyInjectionExtensions` | `Order.Application[.Tests]`, `Order.Api` | ✅ Added |
| `Microsoft.Extensions.Logging.Abstractions 10.0.0` | `Order.Application[.Tests]` | ✅ Added |
| `FluentAssertions 6.12.2` | all `.Tests` projects (both services) | ✅ Added |
| `NSubstitute 5.3.0` | `Order.Application.Tests`, `Order.Infrastructure.Tests` | ✅ Added |
| `Microsoft.EntityFrameworkCore.SqlServer 10.0.0` | `Order.Infrastructure[.Tests]` | ✅ Added |
| `Testcontainers.MsSql 4.4.0` | `Order.Infrastructure.Tests` | ✅ Added |
| `MassTransit.RabbitMQ 8.5.10` (pinned) | `Order.Infrastructure`, `Order.Api`, `Notification.Infrastructure`, `Notification.Api` | ✅ Added — resolves `RabbitMQ.Client 7.2.1` in both services |
| `AspNetCore.HealthChecks.SqlServer 9.0.0` | `Order.Api` | ✅ Added |
| `AspNetCore.HealthChecks.Rabbitmq 9.0.0` (new to repo) | `Order.Api`, `Notification.Api` | ✅ Added — matches the repo's existing 9.0.0-era HealthChecks family |
| `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0` | `Order.Api` | ✅ Added |
| `Serilog.AspNetCore 9.0.0` | `Order.Api`, `Notification.Api` | ✅ Added |
| `Microsoft.AspNetCore.Mvc.Testing 10.0.0` | `Order.Api.Tests`, `Notification.Infrastructure.Tests` | ✅ Added |
| `MailKit 4.17.0` (new to repo, first use) | `Notification.Infrastructure` | ✅ Added — latest stable, resolves `MimeKit 4.17.0` |
| `Testcontainers 4.4.0` (generic, no dedicated SMTP module exists) | `Notification.Infrastructure.Tests` | ✅ Added — pinned to match `Testcontainers.MsSql`/`Testcontainers.Redis` 4.4.0 already used elsewhere, not the newer default |

---

## How to Run

```bash
# 1. Start SQL Server, RabbitMQ, and the dev-only SMTP catcher
docker compose up -d sqlserver rabbitmq smtp4dev

# 2. Run Order Service
dotnet run --project Services/Order/Order.Api

# 3. Run Notification Service
dotnet run --project Services/Notification/Notification.Api
```

**URLs:**

| URL | Purpose |
| --- | --- |
| `http://localhost:5020` (Order, local) / `http://localhost:5003` (Docker) | API base |
| `.../swagger` (Order only — Notification has no HTTP API beyond `/health`) | Swagger UI |
| `.../health` | Health check (both services) |
| `http://localhost:5099` | smtp4dev web UI — view emails Notification actually sent |

**Run tests:**

```bash
dotnet test ShopFlow.sln
```

> `Order.Infrastructure.Tests` and `Notification.Infrastructure.Tests` use Testcontainers — Docker must be running.

---

## TDD Order for Phase 5

```text
Order Service:
 1. ✅ Domain entity tests        → OrderEntity, OrderItemEntity
 2. ✅ Validator tests            → PlaceOrderCommandValidator
 3. ✅ Command/query handler tests → Place/Confirm, GetById/GetMine/GetAll
 4. ✅ Pipeline behavior tests    → ValidationBehavior, LoggingBehavior
 5. ✅ Repository test            → OrderRepository (Testcontainers, real SQL Server)
 6. ✅ Event publisher test       → OrderEventPublisher (real in-process MassTransit bus)
 7. ✅ API endpoint tests         → WebApplicationFactory

Notification Service (built concurrently, independently):
 1. ✅ Email template tests       → OrderConfirmationEmailTemplate
 2. ✅ Event consumer test        → OrderPlacedConsumer (NSubstitute IEmailService)
 3. ✅ Email service test         → MailKitEmailService (real smtp4dev container)
 4. ✅ Health check test          → Program.cs boot check

Identity addendum:
 1. ✅ Command handler test       → VerifyEmailCommandHandler
 2. ✅ API endpoint test          → AuthController.VerifyEmail
```
