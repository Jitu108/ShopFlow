# Phase 5 — Order Service + Notification Service: Implementation Plan

## Context

ShopFlow follows a 7-phase build order (documented in `Documentations/ShopFlow-Approach.md` and tracked live in `Documentations/STATUS.md`). Phases 1-4 (Infrastructure, Identity Service, Product Service, Cart Service) are complete — 215 tests passing. Phase 5 is **Order Service + Notification Service**, currently pending: `Services/Order/` and `Services/Notification/` are empty directories, neither is in `ShopFlow.sln`, and their `docker-compose.yml` blocks exist only as comments. The current branch, `dev/TJKG-009-order-and-notification-service`, is where this work belongs.

Order Service owns checkout and the order lifecycle; Notification Service listens for order events and sends transactional email. Together they're the first phase to exercise the RabbitMQ/MassTransit pub-sub path with a real publisher — Cart already consumes `OrderPlacedEvent`, but a manually-published test event has stood in for a real Order Service until now.

**Spec** (`Documentations/ShopFlow-ProjectSpec.md` §3 lines 367-427 Order, §5 lines 460-488 Notification, FR-27–39, NFR-21):
- Order endpoints: `POST /api/orders`, `GET /api/orders`, `GET /api/orders/{id}`, `PUT /api/orders/{id}/confirm`, `GET /api/admin/orders`
- Order publishes `OrderPlacedEvent` on confirmation (not placement)
- Notification: stateless MassTransit consumer(s), MailKit email, no DB, no inbound API
- Dependencies: Identity + Product + RabbitMQ (all done/running)

**Five gaps found between spec and shipped code, all resolved with the user before writing this plan:**

| # | Gap found | Resolution |
|---|---|---|
| 1 | Spec shows `GET /api/orders [RequireCustomer]`, but no such policy exists anywhere in the codebase (only `RequireVendor`/`RequireAdmin`/`RequireVerifiedEmail`) | Plain `[Authorize]` + ownership via the `userId` JWT claim — matches how Cart and Product's vendor endpoints already handle "my own resource" access. No new policy. |
| 2 | FR-35 requires publishing `OrderShippedEvent` "on shipment," but no ship endpoint is documented anywhere in the spec's endpoint list | **Deferred to a later phase.** Order Service in Phase 5 only reaches `Pending → Confirmed`. No `Ship()` domain method, no `ShipOrderCommand`, no ship endpoint. |
| 3 | `OrderShippedEvent` (in `Shared/ShopFlow.Shared/Events/`) carries no customer email, and Notification has no DB to look one up | Add `CustomerEmail` to `OrderShippedEvent` now — safe, nothing consumes this event yet. Groundwork for whenever a later phase adds shipping. |
| 4 | `ApplicationUser.VerifyEmail()` exists and is unit-tested but is **never called** by any command/handler/controller — not even the seeded admin has `emailVerified: true`. `POST /api/orders [RequireVerifiedEmail]` is unsatisfiable by any real login today. | Add a small self-service `POST /api/auth/verify-email` to Identity (a "done" service) now, so real end-to-end testing of order placement is possible. |
| 5 | *(Follows from #2)* Since nothing will publish `OrderShippedEvent` in Phase 5, should Notification still build `OrderShippedConsumer`? | **No.** Notification Service in Phase 5 implements only `OrderPlacedConsumer` (FR-36). `OrderShippedConsumer` and the shipped-email template move to whichever later phase adds the ship endpoint. |

**Correctness hazard found (not a decision — a bug to avoid):** Cart already binds a consumer to a RabbitMQ queue literally named `"order-placed-queue"` (`Services/Cart/Cart.Api/Program.cs:65`). If Notification's `OrderPlacedConsumer` binds to a queue with that same name, RabbitMQ treats it as a second competing consumer on Cart's existing queue, not a second independent subscriber — messages would round-robin between the two services instead of both receiving every event, and Cart would silently stop clearing carts about half the time. **Notification's receive endpoint must be named `"notification-order-placed-queue"`.**

## Patterns to reuse (from Identity/Product/Cart — do not reinvent)

- **Scaffold**: Order gets the full 8-project shape (`{Svc}.Domain[.Tests]`, `.Application[.Tests]`, `.Infrastructure[.Tests]`, `.Api[.Tests]`), mirroring **Product** (SQL-backed — the closer precedent than Redis-only Cart). Notification gets a leaner 5-project shape — no `Domain[.Tests]` (nothing to hold: no entity, no invariant, nothing that throws a domain exception) and no `Api.Tests` (no controllers beyond framework-provided `/health`). API csproj files are named `{Svc}.API.csproj` / `{Svc}.API.Tests.csproj` (capital API, confirmed via `git ls-files`) even though the folder is `{Svc}.Api`.
- **Entity naming**: `Services/Product/Product.Domain/Entities/ProductEntity.cs` is `ProductEntity`, not `Product`, to dodge a `CS0118` collision with the `Product.*` namespace root every project in that service shares. Order has the identical collision risk — entities must be `OrderEntity`/`OrderItemEntity`.
- **Command/handler files**: separate files (`XCommand.cs` + `XCommandHandler.cs`, same folder) — the consistent convention in Product (100%) and the majority in Identity. (`AssignRoleCommand.cs` combining both in one file is an outlier, not the pattern to copy.)
- **MassTransit pinned to `8.5.10`** (Apache-2.0) — `9.0.0+` requires a paid commercial license and fails at container startup (`Documentations/STATUS.md`, `Documentations/Phases/Phase4.md`). Order and Notification must use the same pin.
- **No EF Core migrations anywhere** — every service uses `Database.EnsureCreated()` at Development startup. Order follows suit for `OrderDb`.
- **Ownership-mismatch-as-404**: `UpdateProductCommandHandler` throws `NotFoundException` (not 403) when a vendor touches another vendor's product. Order's `ConfirmOrderCommand`/`GetOrderByIdQuery` follow the same pattern.
- **JWT claims** (`Identity.Infrastructure/Jwt/TokenService.cs`): `"userId"` (string), `ClaimTypes.Email`, `ClaimTypes.Role`, `"emailVerified"`. Read directly from claims in controllers — never trust the request body for identity.
- **MassTransit wiring / consumer shape**: copy `Services/Cart/Cart.Api/Program.cs`'s `AddMassTransit` block and `Services/Cart/Cart.Infrastructure/Events/OrderPlacedConsumer.cs` verbatim as the starting template (Order adapts it to publish-only; Notification adapts it with a distinct queue name).
- **Solution file**: `dotnet sln add --solution-folder <name> <csproj>`, never hand-edit `ShopFlow.sln`.

## Step-by-step plan

### 1. `Shared/ShopFlow.Shared` — add `CustomerEmail` to `OrderShippedEvent`
```csharp
public record OrderShippedEvent(Guid OrderId, string CustomerEmail, string TrackingNumber, DateTime ShippedAt);
```
Safe — nothing consumes this event yet (unlike `OrderPlacedEvent`, which Cart depends on and whose shape must not move).

### 2. Identity addendum — fix the dead `VerifyEmail()` code
- `Identity.Application/Commands/VerifyEmailCommand.cs` + `VerifyEmailCommandHandler.cs` (separate files) — `VerifyEmailCommand(Guid UserId) : IRequest`. Handler loads the user (`NotFoundException` if missing), calls the existing `user.VerifyEmail()`, persists via `IUserRepository.UpdateAsync`. Mirrors `AssignRoleCommand`'s shape.
- `AuthController` — new `[HttpPost("verify-email")] [Authorize]` action, `UserId` from the `userId` claim, `200 OK`. Doesn't reissue a token — caller re-logs-in afterward for a JWT with `emailVerified: true` (same two-step pattern the Postman suite already uses for role assignment).
- TDD: `VerifyEmailCommandHandlerTests` (happy path, not-found) → handler; `AuthController` verify-email test (401 no-auth, 200 happy) → controller. ~4-5 tests.

### 3. Scaffold the 8 Order projects
`Services/Order/Order.Domain[.Tests]`, `Order.Application[.Tests]`, `Order.Infrastructure[.Tests]`, `Order.Api[.Tests]` (csproj `Order.API.csproj`). Package versions — reuse exactly what's already pinned elsewhere, don't introduce different ones: `MediatR 12.5.0`, `FluentValidation 11.11.0`, `FluentValidation.DependencyInjectionExtensions 11.11.0`, `Microsoft.Extensions.Logging.Abstractions 10.0.0`, `FluentAssertions 6.12.2`, `NSubstitute 5.3.0`, `MassTransit.RabbitMQ 8.5.10` (pinned), `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`, `Serilog.AspNetCore 9.0.0`, `Microsoft.AspNetCore.Mvc.Testing 10.0.0`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.0`, `Testcontainers.MsSql 4.4.0`, `AspNetCore.HealthChecks.SqlServer 9.0.0`, plus new-to-repo `AspNetCore.HealthChecks.Rabbitmq` (pick latest compatible with the other `9.0.0`-era HealthChecks packages).

### 4. Order.Domain
- `Enums/OrderStatus.cs`: `Pending | Confirmed | Shipped | Delivered | Cancelled` (full enum per the spec's domain model; only `Pending`/`Confirmed` reachable by any code path this phase — `Shipped`/`Delivered`/`Cancelled` have no driving requirement yet, same treatment already implied for `Delivered`/`Cancelled` in the spec).
- `Entities/OrderEntity.cs`: `Id, CustomerId, CustomerEmail, Status, TotalAmount, CreatedAt, UpdatedAt, OrderItems[]`. `Create(customerId, customerEmail, items)` validates, computes `TotalAmount`, sets `Pending`. `Confirm()` guards `Status == Pending` (else `DomainException`), sets `Confirmed`. No `Ship`/`Deliver`/`Cancel`. `CustomerEmail` is captured once at placement (from the JWT) and persisted as a snapshot — same philosophy as `OrderItemEntity.ProductName`/`UnitPrice` — so `ConfirmOrderCommandHandler` can build `OrderPlacedEvent.CustomerEmail` from `order.CustomerEmail` directly.
- `Entities/OrderItemEntity.cs`: `Id, OrderId, ProductId, ProductName, UnitPrice, Quantity`. `Create(...)` validates name non-blank, price ≥ 0, quantity ≥ 1 — same shape as `ProductEntity.Validate`.
- `Exceptions/DomainException.cs`, `NotFoundException.cs` — copied from Product.
- TDD: `OrderEntityTests`, `OrderItemEntityTests` first (pure, no mocks) → entities.

### 5. Order.Application
| Command/Query | Handler responsibility |
|---|---|
| `PlaceOrderCommand(CustomerId, CustomerEmail, List<OrderItemRequestDto> Items)` → `OrderDto` | Map items → `OrderItemEntity.Create`, `OrderEntity.Create`, `IOrderRepository.AddAsync`. No event published (FR-34 ties `OrderPlacedEvent` to confirmation, not placement). |
| `ConfirmOrderCommand(OrderId, CustomerId)` → `OrderDto` | Load (`NotFoundException` if missing/not owned); `order.Confirm()` (`DomainException`→400 if not `Pending`); persist; `IOrderEventPublisher.PublishOrderPlacedAsync(order, ct)`. |
| `GetOrderByIdQuery(OrderId, RequesterId, IsAdmin)` → `OrderDto` | Load with items; `NotFoundException` if missing or (`!IsAdmin && order.CustomerId != RequesterId`). |
| `GetMyOrdersQuery(CustomerId)` → `IReadOnlyList<OrderDto>` | `IOrderRepository.GetByCustomerIdAsync`. |
| `GetAllOrdersQuery()` → `IReadOnlyList<OrderDto>` | `IOrderRepository.GetAllAsync` — admin-only, enforced at controller. No pagination (matches `GetProductListQuery`). |

- `Interfaces/IOrderRepository.cs`: `GetByIdAsync` (must `Include(OrderItems)`), `GetByCustomerIdAsync`, `GetAllAsync`, `AddAsync`, `UpdateAsync`.
- `Interfaces/IOrderEventPublisher.cs`: **`PublishOrderPlacedAsync` only.** Declared here, implemented in Infrastructure — keeps `ShopFlow.Shared` out of `Order.Application` entirely, matching Cart's real architecture (`Shared` referenced only by `Cart.Infrastructure` — "a wire concern, not a domain concept," per `Phase4.md`). This deliberately deviates from the spec's own illustrative handler snippet (which publishes directly from Application) — the snippet is a teaching illustration, Phase 4's shipped code is tested precedent.
- `Validators/PlaceOrderCommandValidator.cs`: items not empty; per-item `ProductName` not blank, `UnitPrice ≥ 0`, `Quantity ≥ 1`.
- `Behaviors/ValidationBehavior.cs`, `LoggingBehavior.cs` — copied verbatim from Product, namespace-adjusted.
- `Mapping/OrderMappingExtensions.cs` — hand-written `.ToDto()`, no AutoMapper (matches `ProductMappingExtensions`).
- TDD: validator tests → handler tests (NSubstitute mocks of `IOrderRepository`/`IOrderEventPublisher`) → behavior tests (near-verbatim copy of Cart's/Product's).

### 6. Order.Infrastructure
- `Persistence/AppDbContext.cs`: `DbSet<OrderEntity> Orders`. `HasMany(x => x.OrderItems).WithOne().HasForeignKey(oi => oi.OrderId).OnDelete(Cascade)` — same call shape as `ApplicationUser` → `RefreshTokens`, already proven in this codebase (this is the first *aggregate-owned* one-to-many in the repo — prove it early). `Status` via `HasConversion<int>()` (matches `UserRole`). Money columns `decimal(18,2)`. App-generated `Guid` ids (`ValueGeneratedNever()`).
- `Persistence/Repositories/OrderRepository.cs`: `GetByIdAsync` must eager-load via `Include(o => o.OrderItems)`.
- `Events/OrderEventPublisher.cs : IOrderEventPublisher` — the only class referencing both `MassTransit.IPublishEndpoint` and `ShopFlow.Shared.Events`; maps `OrderEntity` → `OrderPlacedEvent` here.
- `Settings/JwtSettings.cs` — copied from Product (Secret/Issuer/Audience only).
- TDD: `OrderRepositoryTests` (Testcontainers.MsSql, real SQL Server — add/get-with-items/cascade-delete) → repository. `OrderEventPublisherTests` (`AddMassTransitTestHarness`, assert `Publish` fired with correct shape) → publisher.

### 7. Order.Api
```
POST   /api/orders                    [Authorize(Policy = "RequireVerifiedEmail")]  → 201
GET    /api/orders                    [Authorize]                                   → 200
GET    /api/orders/{id:guid}          [Authorize]                                   → 200 / 404
PUT    /api/orders/{id:guid}/confirm  [Authorize]                                   → 200 / 404 / 400
GET    /api/admin/orders              [Authorize(Policy = "RequireAdmin")]          → 200
```
- Two controllers — `Controllers/OrdersController.cs`, `Controllers/AdminOrdersController.cs` — matching Product's split-by-audience precedent. `CustomerId`/`CustomerEmail`/`IsAdmin` always from JWT claims, never the body.
- `Middleware/ExceptionHandlingMiddleware.cs` — copied from Product.
- `Program.cs` — same banner order as Cart/Product (Logging → Settings → DB → Repos/Services → MediatR → FluentValidation+behaviors → MassTransit → Auth → Health → Controllers/Swagger → dev seeding → middleware). `AddMassTransit` has **no `AddConsumer`, no `ReceiveEndpoint`** — Order only publishes. `RequireVerifiedEmail`/`RequireAdmin` policies copied verbatim from `Identity.Api/Program.cs`. Health checks: SQL Server + RabbitMQ. JWT Bearer configured lazily via `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)` (not `AddJwtBearer(opts => ...)`) so `WebApplicationFactory` config overrides work in tests.
- TDD: `OrdersControllerTests`, `AdminOrdersControllerTests` (`WebApplicationFactory` + fakes, mirrors `CartApiFactory`/`ProductApiFactory`).
- `Services/Order/Dockerfile` — multi-stage, needs to copy from `Shared/` too (like Cart's, since Order references `ShopFlow.Shared`).

Order Service total estimate: **~55-65 tests.**

### 8. Scaffold the 5 Notification projects
`Services/Notification/Notification.Application[.Tests]`, `Notification.Infrastructure[.Tests]`, `Notification.Api` (csproj `Notification.API.csproj`, no `.Api.Tests` project — see step 11). New package: MailKit (pick latest stable — first service to need it).

### 9. Notification.Application
- `Interfaces/IEmailService.cs`: `SendOrderConfirmationAsync(toEmail, orderId, items, total, ct)` — the only method (no shipped-email method this phase, per decision #5).
- `Templates/OrderConfirmationEmailTemplate.cs`: `Subject(orderId)`, `Body(orderId, items, total)` — **plain text, not HTML**. Justified by this project's existing stub-quality ethos (same tier as the payment-confirmation stub) — avoids a templating-engine dependency for a feature the spec itself scopes as fire-and-forget, and is trivially unit-testable as string assertions.
- TDD: `OrderConfirmationEmailTemplateTests` (pure string assertions) first.

### 10. Notification.Infrastructure
- `Events/OrderPlacedConsumer.cs : IConsumer<OrderPlacedEvent>` — same shape as Cart's: inject `IEmailService`, call it directly from `Consume()`. **No MediatR, no FluentValidation anywhere in this service** — Cart's own consumer calls its repository directly with nothing dispatched in between, and there's no controller here to justify MediatR's usual role, nor free-form input to validate.
- `Email/MailKitEmailService.cs : IEmailService` — MailKit `SmtpClient`, settings from `SMTP_HOST`/`SMTP_PORT`/`SMTP_FROM`/`SMTP_PASSWORD` (`.env.example` already provisions all four).
- `Settings/EmailSettings.cs`.
- TDD: `OrderPlacedConsumerTests` (NSubstitute `IEmailService`) → consumer. `MailKitEmailServiceTests` against a real SMTP catcher via Testcontainers (see step 14) → email service.

### 11. Notification.Api
- `Program.cs` — **must be `WebApplication.CreateBuilder(args)`**, not a bare Worker Service: NFR-13 requires `/health` for every service unqualified, and a bare `IHostedService` host has no Kestrel listener to serve one from. No controllers, no Swagger, no auth. `AddMassTransit` registers exactly one consumer on `ReceiveEndpoint("notification-order-placed-queue", ...)` — **this exact name, not `"order-placed-queue"`** (see the correctness hazard in Context). `AddHealthChecks().AddRabbitMQ()`. Ends with `public partial class Program { }`.
- No `Notification.Api.Tests` project — no controllers exist to test beyond framework-provided `/health`. Instead, fold one `HealthCheckTests.cs` into `Notification.Infrastructure.Tests` as cheap insurance that `Program.cs` boots.
- `Services/Notification/Dockerfile` — multi-stage, also copies from `Shared/`.

Notification Service total estimate: **~10-12 tests.**

### 12. docker-compose.yml
Uncomment the `order-service` and `notification-service` blocks (currently lines ~130-198). Fix the `notification-service` stub's missing healthcheck line (Order's stub has one, Notification's doesn't — add one matching Order's shape). Add a new dev-only `smtp4dev` service (`rnwood/smtp4dev` image — actively maintained, unlike MailHog) so the confirmation email can be observed locally; wire it only into `notification-service`'s dev/local SMTP env vars, not `.env.example`'s real production placeholders. Both new Dockerfiles need `context: .` + explicit `dockerfile: Services/{X}/Dockerfile` (like Cart's, not Product's plain `build:`), since both reference `Shared/ShopFlow.Shared`.

### 13. Solution file
```
dotnet sln ShopFlow.sln add --solution-folder Order Services/Order/Order.Domain/Order.Domain.csproj
... (remaining 7 Order.* projects, same --solution-folder Order)
dotnet sln ShopFlow.sln add --solution-folder Notification Services/Notification/Notification.Application/Notification.Application.csproj
... (remaining 4 Notification.* projects, same --solution-folder Notification)
```

### 14. Verification
- **Build/test gate**: `dotnet build ShopFlow.sln && dotnet test ShopFlow.sln` — including the Identity addendum, before touching Docker.
- **Live Docker Compose round trip**: `docker compose up -d sqlserver rabbitmq redis identity-service cart-service order-service notification-service smtp4dev`.
  - Register + login a customer; call the new `POST /api/auth/verify-email`; **re-login** for a token with `emailVerified: true`.
  - `POST /api/orders`, `GET /api/orders`, `GET /api/orders/{id}` — confirm item snapshots/total match what was submitted.
  - `PUT /api/orders/{id}/confirm` — confirm status flips to `Confirmed`; confirm Cart's cart clears (`redis-cli EXISTS cart:{userId}` → 0); confirm the confirmation email lands in smtp4dev's web UI with correct recipient/subject/body.
  - Admin login, `GET /api/admin/orders` — confirm visibility across customers.
- **RabbitMQ management UI (`localhost:15672`) topology check** — the concrete check that catches the queue-naming hazard: confirm the `OrderPlacedEvent` exchange has **two** bound queues (`order-placed-queue` and `notification-order-placed-queue`), each with exactly 1 active consumer, and that a single publish increments the consumed-message count on **both**.
- **Postman**: add an "Order" folder to `Documentations/postman/ShopFlow.postman_collection.json` after "Cart" — place order, place with empty items (400), get mine, get by id, get someone else's by id (404), confirm, confirm twice (400), confirm someone else's order (404), admin list all, non-admin hits admin route (403). Add `orderBaseUrl` to both environment files. Update the collection README to move Order off the "not implemented yet" list, note Notification has no HTTP surface to test directly (verified via smtp4dev instead), and note shipping is deferred.
- Finish by writing `Documentations/Phases/Phase5.md` (following the `Phase4.md` template) and updating `Documentations/STATUS.md`'s phase table and test-count totals, explicitly recording the deferred-shipping scope so whichever later phase adds it has a clear starting point.

## Critical files
- `Documentations/ShopFlow-ProjectSpec.md` (Order §3, Notification §5, Authorization Policies, Complexity Anchors)
- `Services/Cart/Cart.Api/Program.cs` + `Cart.Infrastructure/Events/OrderPlacedConsumer.cs` — MassTransit wiring and consumer shape template
- `Services/Product/Product.Infrastructure/Persistence/AppDbContext.cs` + `Product.Application/Commands/UpdateProductCommandHandler.cs` — EF Core SQL config and ownership-mismatch-as-404 pattern
- `Services/Identity/Identity.Infrastructure/Persistence/AppDbContext.cs` + `Identity.Domain/Entities/ApplicationUser.cs` — proven aggregate one-to-many (`RefreshTokens`) pattern for `OrderItems`
- `Services/Identity/Identity.Application/Commands/AssignRoleCommand.cs` + `Identity.Api/Program.cs` — precedent for `VerifyEmailCommand`, and the exact policy definitions to copy
- `Shared/ShopFlow.Shared/Events/*.cs` — event contracts
- `docker-compose.yml` (lines ~130-198), `ShopFlow.sln` — integration wiring targets
- `Documentations/ShopFlow-TDD-Guide.md`, `Documentations/Phases/Phase4.md` — TDD sequencing and phase closeout template
