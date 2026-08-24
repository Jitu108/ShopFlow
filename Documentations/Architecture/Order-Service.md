# Order Service — Full Architecture Documentation

## Abstract

The Order Service is made up of eight .NET projects — four production projects and four matching test projects — that together implement order placement, confirmation, and retrieval. It mirrors **Product's** 8-project, SQL-backed shape almost exactly (see [Product-Service.md](./Product-Service.md)) rather than Cart's Redis-only shape ([Cart-Service.md](./Cart-Service.md)): `OrderEntity`/`OrderItemEntity` are true EF Core aggregate entities with private setters and factory methods, backed by a real SQL Server database, not a cache or a hash. Its own architectural first: **the first true aggregate-owned one-to-many relationship in the repo** — `OrderEntity.OrderItems` is a real parent/child EF Core mapping with cascade delete, proven to round-trip via Testcontainers before any handler code was built on top of it. (Category↔Product in Product Service is a loose reference collection, not an owned aggregate.)

**What each project is, and why it's relevant:**

| Project | What it is | Why it exists |
| --- | --- | --- |
| `Order.Domain` | `OrderEntity`, `OrderItemEntity`, `OrderStatus`, and the exceptions that name what can go wrong | The vocabulary every other project uses to talk about an order and its line items, and the one place the `Pending → Confirmed` transition is guarded. |
| `Order.Application` | The use cases — place an order, confirm an order, get one/mine/all — each as a MediatR command/query + handler, plus a validator, mapping extensions, and the two interfaces (`IOrderRepository`, `IOrderEventPublisher`) they need | Where the order workflow lives, including *who* is allowed to see which order and *when* `OrderPlacedEvent` actually gets published (on confirmation, not placement). |
| `Order.Infrastructure` | The concrete technology: EF Core against SQL Server, a MassTransit/RabbitMQ publisher, a MassTransit **request client** that asks Product a question and waits for the answer, JWT *validation* settings | Makes the use cases work against a real database and a real message bus, behind the interfaces Application declared. **No longer purely a publisher** — Order now has an outbound synchronous-style dependency on Product being reachable (see below), even though it still registers no `IConsumer<T>` of its own. |
| `Order.Api` | The ASP.NET Core host — two controllers split by audience, exception-to-HTTP-status middleware, and the `Program.cs` composition root | The only project any client or other service talks to. |

**How they're related, and why:**

Same directed dependency chain as every other service:

```text
Order.Domain            entities, enum, exceptions — zero dependencies
       ↑
Order.Application       use cases (CQRS) — depends only on Domain
       ↑
Order.Infrastructure    EF Core, MassTransit publisher, JWT settings — depends on Domain + Application + ShopFlow.Shared
       ↑
Order.Api               controllers, middleware, DI composition root — depends on Application + Infrastructure
```

`Order.Application` declares `IOrderRepository` and `IOrderEventPublisher` without implementing either; `Order.Infrastructure` implements both; `Order.Api` wires the choices together in `Program.cs`. That inversion is what lets `Order.Application.Tests` mock both interfaces with NSubstitute, and what let `Order.Api.Tests` swap SQL Server and the message bus for an in-memory fake and a test harness without touching a single handler. Notably, `IOrderEventPublisher` — not `ShopFlow.Shared` itself — is the seam: `Order.Application` never references the shared events library at all, keeping the wire-format concern entirely in Infrastructure (matching Cart's actual dependency shape, not the spec's own illustrative handler snippet, which would have published directly from Application).

The four test projects mirror this chain one-to-one — see [§5](#5-test-projects).

The sections below walk each of the eight projects in full, then trace one request (`PUT /api/orders/{id}/confirm`) end-to-end through all four production layers in [§6](#6-request-flow--end-to-end-example).

---

## Overview

The Order Service owns order placement, confirmation, and retrieval — the fourth service built (Phase 5, alongside Notification). It never authenticates anyone itself — it validates JWTs issued by Identity against the **same shared secret/issuer/audience** as every other service. Per [Phase5.md](../Phases/Phase5.md), building it required two small changes upstream: Identity gained a `POST /api/auth/verify-email` endpoint (an existing-but-unwired `ApplicationUser.VerifyEmail()` method was never callable from any real login before this), and `ShopFlow.Shared`'s `OrderShippedEvent` gained a `CustomerEmail` field in anticipation of a shipping phase that hasn't happened yet.

It follows **Clean Architecture** with **CQRS via MediatR**, identical in shape to Product:

```text
Services/Order/
├── Order.Domain/                Order.Domain.Tests/
├── Order.Application/           Order.Application.Tests/
├── Order.Infrastructure/        Order.Infrastructure.Tests/
└── Order.Api/                   Order.Api.Tests/
```

> **Naming note**: entities are `OrderEntity`/`OrderItemEntity`, not `Order`/`OrderItem` — the same `CS0118` namespace-collision workaround `ProductEntity` established in Phase 3 (every project in the service is rooted `Order.*`).

**Scope deliberately narrower than the full domain model**: `OrderStatus` defines the full `Pending | Confirmed | Shipped | Delivered | Cancelled` lifecycle, but only `Pending → Confirmed` is reachable by any code path — there is no `Ship()`/`Deliver()`/`Cancel()` domain method, no corresponding command, and no endpoint. Shipping (FR-35, `OrderShippedEvent`) is explicitly deferred to a later phase; Notification Service correspondingly implements only an order-*placed* consumer, not an order-*shipped* one.

---

## 1. Order.Domain — Entities, Enum, Exceptions

**[Order.Domain.csproj](../../Services/Order/Order.Domain/Order.Domain.csproj)** — plain class library, **no NuGet packages, no project references**, same isolation guarantee as every other `*.Domain` project.

### Entities

**[OrderEntity](../../Services/Order/Order.Domain/Entities/OrderEntity.cs)** — private setters, private parameterless constructor, mutation only through named methods:

| Member | Purpose |
| --- | --- |
| `Id, CustomerId, CustomerEmail, Status, TotalAmount, CreatedAt, UpdatedAt` | State, all privately settable |
| `OrderItems` | `IReadOnlyList<OrderItemEntity>` exposed over a private backing `List<OrderItemEntity>` — the collection is never handed out mutably |
| `Create(customerId, customerEmail, items)` (static factory) | Throws `DomainException` on empty `customerId`, blank `customerEmail`, or zero items; otherwise sets `Id = Guid.NewGuid()`, `Status = Pending`, `CreatedAt = UpdatedAt = DateTime.UtcNow`, appends every item, and computes `TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity)` itself — the caller never supplies a total directly |
| `Confirm()` | Guards `Status == Pending` (else `DomainException($"Cannot confirm an order in '{Status}' status.")`); sets `Status = Confirmed`, bumps `UpdatedAt`. **There is no `Ship()`/`Deliver()`/`Cancel()`** — those `OrderStatus` values exist in the enum but are unreachable this phase |

`CustomerEmail` is captured once at placement time (from the JWT, via the handler — see [§2](#2-orderapplication--use-cases-cqrs)) and persisted as an immutable snapshot, the same snapshot philosophy `OrderItemEntity.ProductName`/`UnitPrice` already apply to product data — if a customer's email or a product's name/price changes later, historical orders keep showing what was true when the order was made.

**[OrderItemEntity](../../Services/Order/Order.Domain/Entities/OrderItemEntity.cs)** — `Id, OrderId, ProductId, ProductName, UnitPrice, Quantity`, all privately settable. `Create(productId, productName, unitPrice, quantity)` throws `DomainException` on blank name, negative price, or quantity < 1; otherwise sets `Id = Guid.NewGuid()` and the given fields. **No `Update`, no mutator of any kind** — once created, a line item is immutable for the rest of its life (unlike `ProductEntity`, which supports `Update`/`Deactivate`).

### Enums

**[OrderStatus](../../Services/Order/Order.Domain/Enums/OrderStatus.cs)** — `Pending, Confirmed, Shipped, Delivered, Cancelled`. The full lifecycle per the spec's domain model, but — as noted above — only the first two values are ever assigned by running code this phase. This is the same treatment Product gave a couple of its own would-be entity transitions (e.g., no `Activate()` counterpart to `Deactivate()`).

### Exceptions

Copied from Product, namespace-adjusted — identical two-type shape:

| Exception | Thrown when | Mapped to |
| --- | --- | --- |
| `DomainException` (base) | Invalid order/item construction (`OrderEntity.Create`, `OrderItemEntity.Create`); confirming a non-`Pending` order (`OrderEntity.Confirm`) | 400 |
| `NotFoundException(entityName, key)` | Order not found by ID; **also thrown when an order exists but belongs to a different customer** — `ConfirmOrderCommandHandler` and `GetOrderByIdQueryHandler` both reuse "not found" rather than a distinct "forbidden" error for ownership mismatches, the same information-hiding pattern `UpdateProductCommandHandler` established in Product | 404 |

---

## 2. Order.Application — Use Cases (CQRS)

**[Order.Application.csproj](../../Services/Order/Order.Application/Order.Application.csproj)** references only `Order.Domain`, plus `MediatR`, `FluentValidation`, `Microsoft.Extensions.Logging.Abstractions` — the same three packages as Cart's Application layer, and for the same reason (a fully implemented `LoggingBehavior`). **No reference to `ShopFlow.Shared`** — see the inversion note in the [Abstract](#abstract).

### Commands + Handlers

**[Commands/](../../Services/Order/Order.Application/Commands/)**

| Command | Returns | Handler responsibility |
| --- | --- | --- |
| `PlaceOrderCommand(CustomerId, CustomerEmail, Items: List<OrderItemRequestDto>)` | `OrderDto` | Maps each request item to `OrderItemEntity.Create(...)`, then `OrderEntity.Create(...)` (both **Domain** factories — either can throw `DomainException` if something bypasses the validator), `IOrderRepository.AddAsync`. **No event is published here** — FR-34 ties `OrderPlacedEvent` to *confirmation*, not placement, so a placed-but-unconfirmed order never reaches RabbitMQ |
| `ConfirmOrderCommand(OrderId, CustomerId)` | `OrderDto` | Loads by `OrderId` (`NotFoundException` if missing); **ownership check** — `order.CustomerId != command.CustomerId` also throws `NotFoundException`, not a 403 (same collapse-to-404 pattern as Product); **new stock-availability gate** — `IStockAvailabilityChecker.CheckAsync(order.OrderItems, ct)`, and if the result's `IsAvailable` is `false`, throws `DomainException` naming every insufficient `ProductId` **before `order.Confirm()` is ever called** — the order stays `Pending`, nothing is persisted, no event goes out; only once stock checks out does it call `order.Confirm()` (which can still itself throw `DomainException` if already confirmed); `UpdateAsync`; **then** `IOrderEventPublisher.PublishOrderPlacedAsync(order, ct)` |

The naming is deliberately literal to the event contract, not the command: confirming an order is what publishes `OrderPlacedEvent` — a naming choice inherited from the shared event contract Cart already depends on, not renamed for Order's sake.

**Why confirmation validates rather than decrements**: stock is no longer moved at order confirmation at all — Product's `CartStockAdjustedConsumer` already reserved it the moment each item was added to the customer's cart (see [Cart-Service.md §2](../Architecture/Cart-Service.md#2-cartapplication--use-cases-cqrs) and [Product-Service.md §3](../Architecture/Product-Service.md#3-productinfrastructure--persistence-caching-messaging-jwt-settings)). Decrementing again here would double-count the same units for the ordinary cart→confirm path. The stock check exists as a **defensive re-verification**, not the primary reservation mechanism — it exists because reservation itself isn't airtight: `ProductRepository.UpdateAsync` has no optimistic-concurrency protection, so two concurrent cart mutations against the same product can still race each other at the database level (a narrower window than the old confirm-time-decrement design had, but not a closed one). This gate is what turns that residual race into a clean `400` at confirmation instead of a silently-oversold order.

### Queries + Handlers

**[Queries/](../../Services/Order/Order.Application/Queries/)**

| Query | Returns | Handler responsibility |
| --- | --- | --- |
| `GetOrderByIdQuery(OrderId, RequesterId, IsAdmin)` | `OrderDto` | Loads by `OrderId` (`NotFoundException` if missing); throws the same `NotFoundException` if **not admin and not the owner** (`!IsAdmin && order.CustomerId != RequesterId`) — the only query in the codebase so far whose authorization check is parameterized into the query itself rather than left entirely to the controller |
| `GetMyOrdersQuery(CustomerId)` | `IReadOnlyList<OrderDto>` | `GetByCustomerIdAsync` → map. Always scoped to the caller — there's no way to pass someone else's ID in through this query's shape |
| `GetAllOrdersQuery` (no params) | `IReadOnlyList<OrderDto>` | `GetAllAsync` → map. Every order, across every customer — authorization is enforced entirely at the controller level (`[Authorize(Policy = "RequireAdmin")]` on the whole controller), not inside the handler. No pagination, matching `GetProductListQuery`'s precedent |

### DTOs

**[DTOs/](../../Services/Order/Order.Application/DTOs/)** — `OrderDto(Id, CustomerId, CustomerEmail, Status, TotalAmount, CreatedAt, UpdatedAt, OrderItems: List<OrderItemDto>)`, `OrderItemDto(Id, ProductId, ProductName, UnitPrice, Quantity)` (response shape, carries `Id`), and `OrderItemRequestDto(ProductId, ProductName, UnitPrice, Quantity)` (request shape, **no `Id`** — line item IDs are always server-generated). `Status` is serialized as a `string` (`order.Status.ToString()`) via the mapping extensions below, the same convention Identity uses for `UserRole` in its own DTOs.

### Interfaces (the inversion point)

**[IOrderRepository](../../Services/Order/Order.Application/Interfaces/IOrderRepository.cs)**:

```csharp
public interface IOrderRepository
{
    Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrderEntity>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken ct);
    Task AddAsync(OrderEntity order, CancellationToken ct);
    Task UpdateAsync(OrderEntity order, CancellationToken ct);
}
```

No `DeleteAsync` at all — there is no delete use case anywhere in the order lifecycle, soft or hard.

**[IOrderEventPublisher](../../Services/Order/Order.Application/Interfaces/IOrderEventPublisher.cs)**:

```csharp
public interface IOrderEventPublisher
{
    Task PublishOrderPlacedAsync(OrderEntity order, CancellationToken ct);
}
```

**Exactly one method** — a direct consequence of shipping being out of scope: there's no `PublishOrderShippedAsync` counterpart to declare, let alone implement, this phase.

**[IStockAvailabilityChecker](../../Services/Order/Order.Application/Interfaces/IStockAvailabilityChecker.cs)** — the newest interface in this layer, and Order's first-ever *inbound* dependency on another service's state:

```csharp
public interface IStockAvailabilityChecker
{
    Task<StockAvailabilityResult> CheckAsync(IReadOnlyList<OrderItemEntity> items, CancellationToken ct);
}

public record StockAvailabilityResult(bool IsAvailable, IReadOnlyList<Guid> InsufficientProductIds);
```

Takes `OrderItemEntity` directly — a **Domain** type, not a DTO — the same inversion discipline `IOrderEventPublisher` already established: `Order.Application` still never references `ShopFlow.Shared` at all, even for this new cross-service call. `StockAvailabilityResult` is Order's own result shape, distinct from (though field-for-field identical to) `ShopFlow.Shared.Events.CheckStockResponse` — the wire-format record only ever appears in `Order.Infrastructure`'s implementation, below.

### Mapping

**[OrderMappingExtensions](../../Services/Order/Order.Application/Mapping/OrderMappingExtensions.cs)** — hand-written extension methods, `ToDto()` for both `OrderEntity` and `OrderItemEntity`, no AutoMapper — matching `ProductMappingExtensions`'s precedent exactly.

### Validators (FluentValidation)

**[PlaceOrderCommandValidator](../../Services/Order/Order.Application/Validators/PlaceOrderCommandValidator.cs)** — `CustomerEmail` not empty; `Items` not empty; `RuleForEach(x => x.Items).SetValidator(new OrderItemRequestDtoValidator())`, itself validating each item's `ProductName` (not empty), `UnitPrice` (≥ 0), `Quantity` (≥ 1) — the same per-item shape `AddCartItemCommandValidator` enforces in Cart, just applied across a list instead of a single item. **No validator for `ConfirmOrderCommand` or any query** — none of them carry free-form input to validate.

### Pipeline Behaviors

**[Behaviors/](../../Services/Order/Order.Application/Behaviors/)** — copied from Cart/Product, namespace-adjusted, identical shape: `ValidationBehavior<TRequest,TResponse>`, `LoggingBehavior<TRequest,TResponse>`. Same registration order in `Program.cs` (`ValidationBehavior` then `LoggingBehavior`).

---

## 3. Order.Infrastructure — Persistence, Event Publishing, JWT Settings

**[Order.Infrastructure.csproj](../../Services/Order/Order.Infrastructure/Order.Infrastructure.csproj)** references Domain + Application + **`ShopFlow.Shared`**, plus `Microsoft.EntityFrameworkCore.SqlServer`, `MassTransit.RabbitMQ` (pinned `8.5.10` — see below), `Microsoft.Extensions.Options`. No JWT-signing package — Order only validates tokens, same as Cart/Product.

### Persistence

**[AppDbContext](../../Services/Order/Order.Infrastructure/Persistence/AppDbContext.cs)** — `DbSet<OrderEntity> Orders` only (no separate `DbSet<OrderItemEntity>`; items are reached exclusively through their parent). `OnModelCreating`:

- `OrderEntity` → table `Orders`: app-generated `Id` (`ValueGeneratedNever()`), `CustomerId` required with a non-unique `HasIndex` (supports `GetByCustomerIdAsync`), `CustomerEmail` required ≤256 chars, `Status` via `HasConversion<int>()` (matching `UserRole`'s convention), `TotalAmount` as `decimal(18,2)`, `CreatedAt`/`UpdatedAt` required.
- **`HasMany(x => x.OrderItems).WithOne().HasForeignKey(oi => oi.OrderId).OnDelete(DeleteBehavior.Cascade)`** — the first true aggregate-owned one-to-many in the codebase (see [Abstract](#abstract)). EF Core's convention-based backing-field detection resolves the private `_orderItems` list without an explicit `.HasField(...)` call. Cascade delete means deleting an `Order` row also deletes its `OrderItems` rows — though moot in practice today, since no delete-order use case exists.
- `OrderItemEntity` → table `OrderItems`: app-generated `Id`, `ProductId` required, `ProductName` required ≤200 chars, `UnitPrice` as `decimal(18,2)`, `Quantity` required.

**[OrderRepository : IOrderRepository](../../Services/Order/Order.Infrastructure/Persistence/Repositories/OrderRepository.cs)** — every read (`GetByIdAsync`, `GetByCustomerIdAsync`, `GetAllAsync`) eager-loads via `.Include(o => o.OrderItems)`, so a caller never has to remember to ask for line items separately. `AddAsync`/`UpdateAsync` each do one `Add`/`Update` + `SaveChangesAsync` — one unit-of-work per call, same pattern as `ProductRepository`.

Database: `OrderDb` (SQL Server), schema created via `Database.EnsureCreated()` at Development startup — no EF Core migrations anywhere in the repo yet.

### Events

**[OrderEventPublisher : IOrderEventPublisher](../../Services/Order/Order.Infrastructure/Events/OrderEventPublisher.cs)** — no longer the *only* class referencing `ShopFlow.Shared.Events`, now that `StockAvailabilityChecker` (below) does too. `PublishOrderPlacedAsync` maps `order.OrderItems` to `OrderItemDto` records, then publishes `OrderPlacedEvent(order.Id, order.CustomerId, order.CustomerEmail, items, order.TotalAmount, DateTime.UtcNow)` — the timestamp is the publish time, not `order.CreatedAt` or `order.UpdatedAt`.

**[StockAvailabilityChecker : IStockAvailabilityChecker](../../Services/Order/Order.Infrastructure/Events/StockAvailabilityChecker.cs)** — Order's first class to use MassTransit **request/response** rather than fire-and-forget publish:

```csharp
public class StockAvailabilityChecker : IStockAvailabilityChecker
{
    private readonly IRequestClient<CheckStockRequest> _requestClient;
    public StockAvailabilityChecker(IRequestClient<CheckStockRequest> requestClient) => _requestClient = requestClient;

    public async Task<StockAvailabilityResult> CheckAsync(IReadOnlyList<OrderItemEntity> items, CancellationToken ct)
    {
        var request = new CheckStockRequest(items
            .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity))
            .ToList());

        var response = await _requestClient.GetResponse<CheckStockResponse>(request, ct);

        return new StockAvailabilityResult(response.Message.IsAvailable, response.Message.InsufficientProductIds);
    }
}
```

`IRequestClient<CheckStockRequest>` is a MassTransit-managed request/response client, not a raw publish — it sends `CheckStockRequest` to Product's `CheckStockConsumer` (see [Product-Service.md §3](../Architecture/Product-Service.md#3-productinfrastructure--persistence-caching-messaging-jwt-settings)) and awaits the matching `CheckStockResponse` on an auto-managed reply queue, with a 10-second timeout configured where the client is registered (below). If Product never answers within that window, `GetResponse` throws a `RequestTimeoutException`, which is not one of `ExceptionHandlingMiddleware`'s known types — it falls through to the generic `500` branch, the same as any other unhandled infrastructure failure. There is no retry, circuit breaker, or fallback-to-"assume available" behavior here; a broker or Product outage makes every order confirmation fail loudly rather than silently skip the check.

**Dependency-version note (shared with Cart)**: `MassTransit.RabbitMQ` is pinned to **`8.5.10`**, not the `9.2.0`+ that introduced a mandatory commercial license. Per [Phase4.md](../Phases/Phase4.md)/[Phase5.md](../Phases/Phase5.md), every MassTransit-referencing project in the repo (Cart, Order, Notification) must stay on this pin unless a license is acquired.

### Settings

**[JwtSettings](../../Services/Order/Order.Infrastructure/Settings/JwtSettings.cs)** — `Secret`, `Issuer`, `Audience` only, identical shape to Cart's and Product's. No `ExpiryMinutes` — Order never mints a token.

---

## 4. Order.Api — Controllers, Middleware, Composition Root

**[Order.API.csproj](../../Services/Order/Order.Api/Order.API.csproj)** (`Sdk="Microsoft.NET.Sdk.Web"`) references Application + Infrastructure, plus `Serilog.AspNetCore`, `Swashbuckle.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `AspNetCore.HealthChecks.SqlServer`, **`AspNetCore.HealthChecks.Rabbitmq`** (new to the repo this phase), `FluentValidation.DependencyInjectionExtensions`, `MediatR`, `MassTransit.RabbitMQ`, `Microsoft.AspNetCore.Authentication.JwtBearer`.

### Endpoints

```text
POST   /api/orders                    [Authorize(Policy = "RequireVerifiedEmail")]  → 201 Created
GET    /api/orders                    [Authorize]                                    → 200 OK  (caller's own orders only)
GET    /api/orders/{id:guid}          [Authorize]                                    → 200 OK / 404
PUT    /api/orders/{id:guid}/confirm  [Authorize]                                    → 200 OK / 404 / 400 (already confirmed)
GET    /api/admin/orders              [Authorize(Policy = "RequireAdmin")]           → 200 OK  (every order, every customer)
GET    /health                                                                       → 200 OK — health status
```

**[OrdersController](../../Services/Order/Order.Api/Controllers/OrdersController.cs)** — `CustomerId`, `CustomerEmail`, and `IsAdmin` are all derived from JWT claims (`userId`, `ClaimTypes.Email`, `User.IsInRole("Admin")` respectively) — **never** from the request body, the same discipline `ProductsController.VendorId` and `CartController.UserId` already established. `PlaceOrder` accepts a local `PlaceOrderRequest(List<OrderItemRequestDto> Items)` record — no `CustomerId`/`CustomerEmail` field, confirming both always come from claims.

**[AdminOrdersController](../../Services/Order/Order.Api/Controllers/AdminOrdersController.cs)** — a second, class-level-gated controller (`[Authorize(Policy = "RequireAdmin")]` on the whole class, not per-action) exposing exactly one action, `GetAll`. This split-by-audience shape mirrors Product's `ProductsController`/`VendorsController` precedent: one controller for "my own resource" operations, a separate one for admin-wide operations, rather than mixing authorization levels inside a single controller.

### Middleware

**[ExceptionHandlingMiddleware](../../Services/Order/Order.Api/Middleware/ExceptionHandlingMiddleware.cs)** — byte-for-byte the same exception-to-status mapping as Cart/Product/Identity:

| Exception caught | Status | Body |
| --- | --- | --- |
| `FluentValidation.ValidationException` | 400 | `{ errors: [{ propertyName, errorMessage }] }` |
| `NotFoundException` | 404 | `{ message }` |
| `DomainException` (base, catch-all) | 400 | `{ message }` — this is how "already confirmed" (`order.Confirm()`'s guard) surfaces as a 400 |
| Any other `Exception` | 500 | Generic message; full exception logged |

### Composition root — Program.cs

**[Program.cs](../../Services/Order/Order.Api/Program.cs)**:

- Binds `JwtSettings` from configuration.
- Registers `AppDbContext` against SQL Server (`ConnectionStrings:Default`).
- Registers `IOrderRepository`, `IOrderEventPublisher`, and now `IStockAvailabilityChecker` — all **Scoped**.
- Registers MediatR scanning `Order.Application`; FluentValidation scanning the same assembly; `ValidationBehavior<,>` then `LoggingBehavior<,>` as open-generic `IPipelineBehavior<,>`.
- **`AddMassTransit`**: `AddRequestClient<CheckStockRequest>(TimeSpan.FromSeconds(10))`, then `UsingRabbitMq(...)` — **still no `AddConsumer`, no `ReceiveEndpoint`**, since Order still never processes an inbound event the classic way. The 10-second timeout is the ceiling `StockAvailabilityChecker`'s `GetResponse` call will wait before throwing. This is no longer the shortest MassTransit block of the messaging-aware services — that distinction now more accurately belongs to whichever service's block has the fewest total registrations, since Order's now carries a request client Cart's and Notification's blocks don't have.
- Configures JWT Bearer auth the same lazy way as every other service (`AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)`, so `WebApplicationFactory` config overrides take effect in tests); `ClockSkew = TimeSpan.Zero`.
- Registers **two** authorization policies, copied verbatim from Identity's `Program.cs`: `RequireVerifiedEmail` (`RequireClaim("emailVerified", "true")`) and `RequireAdmin` (`RequireRole("Admin")`). **No `RequireVendor`** — Order has no vendor concept.
- Registers `/health` against **both SQL Server and RabbitMQ** — the `AddRabbitMQ` health check factory takes `Func<IServiceProvider, Task<IConnection>>`; its actual signature was confirmed by reflecting the installed `AspNetCore.HealthChecks.Rabbitmq` assembly rather than guessed, after an initial guess failed to compile.
- **Dev-only startup block**: `db.Database.EnsureCreated()` only — no seed data, same as Product's dev block (contrast with Identity's, which also seeds an admin account).
- `public partial class Program` at the bottom, for `WebApplicationFactory<Program>`.

---

## 5. Test Projects

| Test project | Targets | Style | Notable packages |
| --- | --- | --- | --- |
| **Order.Domain.Tests** (14 tests) | `Order.Domain` | Pure unit, no mocks — `OrderEntityTests`, `OrderItemEntityTests` exercise `Create` + every guard clause on both entities, plus `Confirm`'s state guard | xunit, FluentAssertions |
| **Order.Application.Tests** (26 tests) | `Order.Application` | Handlers/validators/behaviors tested against **NSubstitute** mocks of `IOrderRepository`/`IOrderEventPublisher`/**`IStockAvailabilityChecker`** — no DB, no message bus, no HTTP. Covers `PlaceOrderCommandHandlerTests`, `ConfirmOrderCommandHandlerTests` (not-found, ownership-mismatch, already-confirmed, **and the new insufficient-stock case** — asserts the `DomainException`, and that neither `UpdateAsync` nor `PublishOrderPlacedAsync` are ever called), `GetOrderByIdQueryHandlerTests` (including admin-can-view-others), `GetMyOrdersQueryHandlerTests`, `GetAllOrdersQueryHandlerTests`, `PlaceOrderCommandValidatorTests`, `ValidationBehaviorTests`, `LoggingBehaviorTests`. The mock `IStockAvailabilityChecker` defaults to `StockAvailabilityResult(true, [])` in every other test's setup, so only the one new test needs to override it | + NSubstitute, FluentValidation, Microsoft.Extensions.Logging.Abstractions |
| **Order.Infrastructure.Tests** (8 tests) | `Order.Infrastructure` | `OrderRepositoryTests` (5, real SQL Server via `Testcontainers.MsSql`) — add/get-with-items roundtrip (the first proof in the repo that an EF Core aggregate with an owned collection actually persists and reloads correctly), unknown id, get-by-customer filters correctly, get-all, update persists a status change. `OrderEventPublisherTests` (1, real in-process MassTransit bus via `AddMassTransitTestHarness`) — confirms a published `OrderPlacedEvent` carries the correct `OrderId`/`CustomerEmail`/`Total`. **New** `StockAvailabilityCheckerTests` (2) — register an ad-hoc `cfg.AddHandler<CheckStockRequest>(...)` plus `cfg.AddRequestClient<CheckStockRequest>()` against the same in-process harness (no real Product service involved), resolve `IRequestClient<CheckStockRequest>` from a **DI scope** (a plain root-provider `GetRequiredService` throws — MassTransit registers the request client as Scoped), and assert `StockAvailabilityChecker` maps an available/unavailable `CheckStockResponse` into the matching `StockAvailabilityResult` | + NSubstitute, **Testcontainers.MsSql**, MassTransit(.Testing) |
| **Order.Api.Tests** (17 tests, project file named `Order.API.Tests.csproj`) | Full stack via `Order.Api` | End-to-end HTTP tests through `WebApplicationFactory` | + Microsoft.AspNetCore.Mvc.Testing |

**Order.Api.Tests fixtures** ([Fixtures/](../../Services/Order/Order.Api.Tests/Fixtures/)):
- `OrderApiFactory` — swaps `AppDbContext` for EF Core **InMemory** (largely vestigial, exactly as in Product, since the repository below never touches it) and `IOrderRepository` → `FakeOrderRepository` (Singleton, exposed as a public property with a `Seed` helper). Performs the same MassTransit-namespace-descriptor removal + `AddMassTransitTestHarness()` swap that `CartApiFactory` pioneered — **`IOrderEventPublisher` and `IStockAvailabilityChecker` both deliberately stay wired to their real implementations**, resolving `IPublishEndpoint`/`IRequestClient<CheckStockRequest>` from the test harness instead of a real broker connection, so tests can assert against `harness.Published.Any<OrderPlacedEvent>()` for real rather than mocking the publisher away entirely. Since there's no real Product service in this test host, the harness config also registers `cfg.AddHandler<CheckStockRequest>(async ctx => await ctx.RespondAsync(new CheckStockResponse(true, [])))` plus `cfg.AddRequestClient<CheckStockRequest>()` — every confirm-flow API test gets an automatic "always available" answer, so `OrdersControllerTests` doesn't need to fake a whole second service just to exercise the confirm endpoint. Insufficient-stock behavior is covered instead at the Application layer, in `ConfirmOrderCommandHandlerTests` (above).
- `FakeOrderRepository` — a `Dictionary<Guid, OrderEntity>`, `AddAsync`/`UpdateAsync` both just upsert into the same dictionary (no separate insert-vs-update distinction, since `OrderEntity` has no concurrency token to violate).
- `JwtTokenHelper` — mints JWTs with **four** claims (`userId`, `ClaimTypes.Email`, `ClaimTypes.Role`, and `emailVerified`, defaulting to `false`) — the one difference from Cart's/Product's copy of this helper, needed because `RequireVerifiedEmail` is a real, exercised policy here.

- `OrdersControllerTests` (14) — place order (401 no auth, 403 unverified email, 201 happy path + total calculation, 400 empty items), get mine (401, filters to caller's own orders only), get by id (401, 404 unknown, 200 as owner, 404 as non-owner), confirm (401, 200 + asserts `OrderPlacedEvent` was actually published via the harness, 400 already-confirmed, 404 non-owner).
- `AdminOrdersControllerTests` (3) — 401 no auth, 403 non-admin, 200 admin sees orders across every customer.

This mirrors Identity's/Product's/Cart's test strategy exactly: cheap and deterministic near Domain/Application, real infrastructure (SQL Server *and* an in-process message bus, the first service to need both together) at the edges.

---

## 5.5 Project Dependency Wiring

```text
┌─────────────────────────────────────────────────────────────────────┐
│                          Order Service                               │
│                                                                       │
│   Production Code                      Test Projects                │
│   ───────────────                      ─────────────                │
│                                                                       │
│   ┌──────────────┐                    ┌───────────────────────┐     │
│   │  Order.API   │                    │   Order.Api.Tests     │     │
│   └──────┬───┬───┘                    └───────────┬───────────┘     │
│          │   │ refs                               │ refs            │
│          │   └────────────────┐                   ▼                 │
│          │ refs                │      ┌──────────────────────┐      │
│          ▼                     │      │ Order.Infra.Tests    │      │
│   ┌──────────────────┐         │      └──────────┬───────────┘      │
│   │  Order.Infra     │◄────────┘                 │ refs             │
│   └───┬─────────┬────┘                           ▼                  │
│       │ refs    │ refs         ┌──────────────────────────────┐     │
│       │         │              │   Order.Application.Tests    │     │
│       │         │              └──────────┬───────────────────┘     │
│       │         │                         │ refs                    │
│       │         ▼                         ▼                         │
│       │  ┌──────────────────┐  ┌──────────────────────────────┐     │
│       │  │  Order.Application│◄─│    Order.Domain.Tests        │     │
│       │  └────────┬─────────┘  └──────────────────────────────┘     │
│       │ refs      │ refs                                            │
│       │           ▼                                                 │
│       └──►┌──────────────────┐                                      │
│           │   Order.Domain   │                                      │
│           └──────────────────┘                                      │
│                 (no deps)                                            │
│                                                                       │
│   Order.Infra also refs ──► Shared/ShopFlow.Shared (event contracts) │
└─────────────────────────────────────────────────────────────────────┘
```

| Project | References |
| --- | --- |
| `Order.Domain` | — |
| `Order.Application` | `Order.Domain` |
| `Order.Infrastructure` | `Order.Domain` + `Order.Application` + `ShopFlow.Shared` |
| `Order.API` | `Order.Application` + `Order.Infrastructure` |
| `Order.Domain.Tests` | `Order.Domain` |
| `Order.Application.Tests` | `Order.Application` |
| `Order.Infrastructure.Tests` | `Order.Infrastructure` |
| `Order.Api.Tests` | `Order.API` |

Structurally identical to Product's wiring diagram, with `ShopFlow.Shared` slotted in exactly where it sits for Cart — attached only to the Infrastructure layer, never Domain or Application.

---

## 6. Request Flow — End to End Example

`PUT /api/orders/{id}/confirm` (as the order's own authenticated customer):

1. **Api**: `OrdersController.Confirm` reads `CustomerId` from the `userId` JWT claim, builds `ConfirmOrderCommand(id, CustomerId)`, calls `IMediator.Send`.
2. **Application (pipeline)**: no validator is registered for `ConfirmOrderCommand`, so `ValidationBehavior` short-circuits straight to `next()`. `LoggingBehavior` logs "Handling `ConfirmOrderCommand`" around the call.
3. **Application (handler)**: `ConfirmOrderCommandHandler`:
   - `IOrderRepository.GetByIdAsync` → **Infrastructure**'s `OrderRepository` runs `SingleOrDefaultAsync` with `.Include(OrderItems)` against `AppDbContext`; `null` → `NotFoundException` → **Api**'s `ExceptionHandlingMiddleware` → HTTP 404.
   - Ownership check: `order.CustomerId != command.CustomerId` → the same `NotFoundException` → HTTP 404 (never a 403 — see [§1](#1-orderdomain--entities-enum-exceptions)).
   - `IStockAvailabilityChecker.CheckAsync(order.OrderItems, ct)` → **Infrastructure**'s `StockAvailabilityChecker` sends `CheckStockRequest` to Product Service via `IRequestClient<CheckStockRequest>` and awaits `CheckStockResponse` (10-second timeout). If any item is short, `DomainException` naming every insufficient `ProductId` → HTTP 400 — **the order is never touched**, `order.Confirm()` is never even called, and no event is published.
   - `order.Confirm()` (**Domain** method) — guards `Status == Pending`; a second confirm attempt throws `DomainException` → HTTP 400.
   - `IOrderRepository.UpdateAsync` → **Infrastructure** persists the status change via EF Core's change tracker + `SaveChangesAsync`.
   - `IOrderEventPublisher.PublishOrderPlacedAsync(order, ct)` → **Infrastructure**'s `OrderEventPublisher` maps the order's items to `OrderItemDto` and publishes `OrderPlacedEvent` onto RabbitMQ via `IPublishEndpoint`.
   - Returns `order.ToDto()` (**Application** mapping extension, `Status` serialized as `"Confirmed"`).
4. **Api**: controller returns HTTP 200 with the `OrderDto` body.
5. **Downstream, asynchronously**: Cart's `OrderPlacedConsumer` (on `order-placed-queue`) clears the customer's cart; Notification's `OrderPlacedConsumer` (on the distinct `notification-order-placed-queue`) sends an order-confirmation email — both are independent subscribers to the same `OrderPlacedEvent`, not a single competing-consumer queue, verified live via RabbitMQ's management API showing exactly one consumer on each queue. **Note what's absent here**: Product does *not* consume `OrderPlacedEvent` at all — stock was already adjusted when the items entered the cart, well before this step (see [Cart-Service.md §6](../Architecture/Cart-Service.md#6-request-flow--end-to-end-example)); step 3's stock check above is the only place this request touches Product, and it's a synchronous request/response read, not a fire-and-forget event.

Every arrow that crosses a layer boundary within Order itself crosses through an interface owned by the *inner* layer (`IOrderRepository`, `IOrderEventPublisher`, now also `IStockAvailabilityChecker`) — exactly as in Identity's, Product's, and Cart's request-flow traces. Step 5 is the one place the trace legitimately leaves this service's four layers entirely, the same way Cart's `OrderPlacedConsumer` flow does in reverse. Step 3's stock check is a different kind of cross-service reach than step 5 — a synchronous-style call this same request *waits on*, not a fire-and-forget notification to whoever happens to be listening.

---

## 7. Configuration & Running

Connection strings, RabbitMQ address, and JWT settings all live in `appsettings.Development.json` — the base `appsettings.json` has none of these, the same "Development-only for now" pattern as Cart/Product. Full run instructions are in [Documentations/RUNNING.md](../RUNNING.md); summary:

```bash
docker compose up -d sqlserver rabbitmq
dotnet run --project Services/Order/Order.Api
```

- API: `http://localhost:5020` locally (per [launchSettings.json](../../Services/Order/Order.Api/Properties/launchSettings.json)), `http://localhost:5003` in Docker; Swagger: `/swagger`; health: `/health` (SQL Server + RabbitMQ, both checked — a RabbitMQ outage now also silently breaks order confirmation, not just event publishing, since `StockAvailabilityChecker` needs the same broker)
- No dev seed data — an order is created the moment an authenticated, **email-verified** customer places one; there is nothing to pre-populate
- Placing an order requires `emailVerified: true` on the caller's JWT, which in turn requires having called `POST /api/auth/verify-email` on Identity first and logged in again — a real login prior to Phase 5 could never satisfy this policy at all
- **New runtime dependency**: confirming an order now requires **Product Service's `product-check-stock-queue` consumer** to be up and responsive, not just RabbitMQ itself — if Product is down or its consumer isn't running, `StockAvailabilityChecker.CheckAsync` times out after 10 seconds and confirmation fails with a `500`, even though the order data and RabbitMQ broker are both perfectly healthy. This is a new cross-service coupling that didn't exist before this feature — placing an order still has no dependency on Product, only *confirming* one does
- `dotnet test ShopFlow.sln` — note `Order.Infrastructure.Tests` needs Docker running (Testcontainers spins up a single SQL Server container; no Redis, no RabbitMQ container — `OrderEventPublisherTests` and the new `StockAvailabilityCheckerTests` both use an in-process test harness instead of a real broker)

---

## Summary — what each layer answers

| Layer | Answers |
| --- | --- |
| `Order.Domain` | What is a valid order/line-item state, what totals does an order compute for itself, and which status transitions are actually legal today? |
| `Order.Application` | What does the system do for each order use case — precisely when (confirmation, not placement) does an event go out, and what has to be true about stock elsewhere in the system before that's allowed to happen? |
| `Order.Infrastructure` | How is that fulfilled — SQL Server with a real owned aggregate, a RabbitMQ publisher, a request/response client into Product, JWT validation settings? |
| `Order.Api` | How is it exposed over HTTP across two audiences (customer vs. admin), how do failures become status codes, and how is everything — including the message bus — wired together at startup? |
