# Cart Service — Full Architecture Documentation

## Abstract

The Cart Service is made up of eight .NET projects — four production projects and four matching test projects — that together implement the shopping cart: add/update/remove items, view the cart, and clear it automatically when an order is placed. It follows the same Clean Architecture shape as Identity and Product (see [Identity-Service.md](./Identity-Service.md), [Product-Service.md](./Product-Service.md)) and trusts Identity's JWTs exactly as Product does — no `/auth` endpoints, no custom authorization policies at all. Its defining architectural difference from both: **there is no database and no domain entity**. The cart's state lives entirely in a Redis Hash; Redis *is* the persistence, so there's nothing for a `CartEntity` to wrap.

**What each project is, and why it's relevant:**

| Project | What it is | Why it exists |
| --- | --- | --- |
| `Cart.Domain` | Only exceptions — `DomainException`, `NotFoundException`. **No `Entities/` folder, no enums** | A cart item is a plain data record with no invariants of its own; quantity/price/name validation lives in FluentValidation at the Application boundary instead. |
| `Cart.Application` | The use cases — add/update/remove a cart item, clear the cart, get the cart — each as a MediatR command/query + handler, plus validators and the `ICartRepository` interface | Where the cart workflow lives, including the cumulative-add-to-cart rule and the update-vs-upsert distinction. |
| `Cart.Infrastructure` | The concrete technology: a Redis Hash per user via StackExchange.Redis, a MassTransit/RabbitMQ consumer that reacts to `OrderPlacedEvent`, a **publisher** that announces `CartStockAdjustedEvent` for Product to react to, JWT *validation* settings | Makes the use cases work against real storage and a real message bus, behind the interfaces Application declared. Like Product.Infrastructure, this layer signs nothing — it only validates tokens Identity already signed. Cart is now a two-way MassTransit participant, not just a consumer. |
| `Cart.Api` | The ASP.NET Core host — controller, exception-to-HTTP-status middleware, and the `Program.cs` composition root | The only project any client or other service talks to. |

**How they're related, and why:**

Same directed dependency chain as Identity and Product, and for the same reason:

```text
Cart.Domain            exceptions only — zero dependencies
       ↑
Cart.Application       use cases (CQRS) — depends only on Domain
       ↑
Cart.Infrastructure    Redis, MassTransit consumer, JWT settings — depends on Domain + Application + ShopFlow.Shared
       ↑
Cart.Api               controller, middleware, DI composition root — depends on Application + Infrastructure
```

`Cart.Application` declares `ICartRepository` without implementing it; `Cart.Infrastructure` implements it as `RedisCartRepository`; `Cart.Api` wires the choice together in `Program.cs`. That inversion is what lets `Cart.Application.Tests` mock the repository with NSubstitute, and what let `Cart.Api.Tests` swap Redis for an in-memory dictionary fake without touching a single handler.

The four test projects mirror this chain one-to-one, exactly as in Identity and Product — see [§5](#5-test-projects).

The sections below walk each of the eight projects in full, then trace one request (`POST /api/cart/items`) end-to-end through all four production layers in [§6](#6-request-flow--end-to-end-example).

---

## Overview

The Cart Service owns the shopping cart: adding, updating, removing, and clearing line items for the authenticated user, plus reacting to order placement by clearing the cart automatically. It never authenticates anyone itself — it validates JWTs issued by the Identity Service against the **same shared secret/issuer/audience** as Identity and Product. Per [Phase4.md](../Phases/Phase4.md), it's the third service built.

It follows **Clean Architecture** with **CQRS via MediatR**, the same shape as Identity/Product but with no persistence project (SQL Server) in the traditional sense — Redis takes that role:

```text
Shared/
└── ShopFlow.Shared/              Event contracts shared across services

Services/Cart/
├── Cart.Domain/                   Cart.Domain.Tests/
├── Cart.Application/              Cart.Application.Tests/
├── Cart.Infrastructure/           Cart.Infrastructure.Tests/
└── Cart.Api/                      Cart.Api.Tests/
```

**`ShopFlow.Shared`** was created in this phase (rather than deferred to Phase 5) specifically so shared event contracts live in one library, never duplicated. It holds `OrderPlacedEvent`, `OrderItemDto`, and `OrderShippedEvent`; Cart consumes only `OrderPlacedEvent`, but all three were defined now so Phase 5 (Order/Notification) only ever *adds* to the library, never edits a contract Cart already depends on. It's referenced only by `Cart.Infrastructure` — the event contract is a wire concern, not something the Domain or Application layers need to know about.

> **Data-model note:** the cart spec describes a Redis hash `cart:{userId}` mapping `productId → quantity`. Cart instead stores the *entire* `CartItemDto` (including `ProductName`/`UnitPrice`) as JSON in each hash field. This is a deliberate, documented deviation — a synchronous call to Product Service on every cart read would conflict with Cart's "fast, self-contained" design goal, and the API needs to return name/price on every read regardless.

---

## 1. Cart.Domain — Exceptions Only

**[Cart.Domain.csproj](../../Services/Cart/Cart.Domain/Cart.Domain.csproj)** — plain class library, **no NuGet packages, no project references**, same isolation guarantee as `Identity.Domain`/`Product.Domain`.

### Entities

**None.** This is the one structural departure from Identity/Product's Domain layer: there is no aggregate to model. A cart is just "whatever is in the Redis hash for this user right now" — the hash itself is the persisted state, so there's no separate in-memory entity to load, mutate, and save.

### Enums

None.

### Exceptions

Copied from Product, namespace adjusted — only two types, identical shape to Product's:

| Exception | Thrown when | Mapped to |
| --- | --- | --- |
| `DomainException` (base) | Not thrown directly anywhere in Cart — kept only as the common base class and as the middleware's catch-all before `Exception` | 400 |
| `NotFoundException(entityName, key)` | `UpdateCartItemCommandHandler` when the target product isn't already in the cart | 404 |

**Notable asymmetry vs. Product**: Cart's `NotFoundException` is raised with `nameof(CartItemDto)` as the entity name (an Application-layer DTO, not a Domain entity) — there's no domain type to name it after, since none exists.

---

## 2. Cart.Application — Use Cases (CQRS)

**[Cart.Application.csproj](../../Services/Cart/Cart.Application/Cart.Application.csproj)** references only `Cart.Domain`, plus `MediatR`, `FluentValidation`, and `Microsoft.Extensions.Logging.Abstractions` (needed because `LoggingBehavior` is fully implemented here, exactly as in Product).

### DTOs

**[CartItemDto](../../Services/Cart/Cart.Application/DTOs/CartItemDto.cs)** — the entire data model for a cart line, and the closest thing Cart has to an entity:

```csharp
public record CartItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
```

A plain record, not a class with private setters/factory methods — there are no invariants to protect at this level (see [§1](#1-cartdomain--exceptions-only)).

### Commands + Handlers

**[Commands/](../../Services/Cart/Cart.Application/Commands/)**

| Command | Returns | Handler responsibility |
| --- | --- | --- |
| `AddCartItemCommand(UserId, ProductId, ProductName, UnitPrice, Quantity)` | `CartItemDto` | Loads the current cart; **if the product is already present, adds the new quantity to the existing quantity** (cumulative add-to-cart, not overwrite); builds a fresh `CartItemDto` with the combined quantity; `UpsertItemAsync`; then `ICartEventPublisher.PublishStockAdjustedAsync(productId, command.Quantity, ct)` — **the delta published is always the newly-requested quantity, never the combined total**, since that's the amount actually leaving availability this call. `ProductName`/`UnitPrice` from the *new* request always win — an existing item's stale name/price gets silently refreshed on every add |
| `UpdateCartItemCommand(UserId, ProductId, Quantity)` | `CartItemDto` | Loads the cart; throws `NotFoundException` if the product **isn't already present** — update is deliberately not an upsert; otherwise `existing with { Quantity = command.Quantity }` (a full overwrite of quantity, not an add); `UpsertItemAsync`; then publishes `command.Quantity - existing.Quantity` as the delta — **positive** if the caller raised the quantity (more stock reserved), **negative** if they lowered it (stock released back), and **skipped entirely** if the quantity didn't actually change |
| `RemoveCartItemCommand(UserId, ProductId)` | *(none — `IRequest`)* | Loads the cart first (a new read that didn't exist before stock tracking), then forwards to `ICartRepository.RemoveItemAsync` — still **idempotent**, removing an already-absent product is a no-op, not an error; if the product *was* present, publishes `-existing.Quantity` (the full reserved amount comes back) — if it wasn't present, no event is published at all, since nothing was ever reserved to release |
| `ClearCartCommand(UserId)` | *(none — `IRequest`)* | Forwards straight to `ICartRepository.ClearCartAsync` — deletes the whole `cart:{userId}` key. **Does not publish any stock-adjustment event** — a real, deliberate gap: clearing the cart does not currently release the stock every item in it had reserved (see [§3](#3-cartinfrastructure--redis-persistence-event-consumer-jwt-settings)) |

`ClearCartCommandHandler` is still a one-line pass-through with no branching. `RemoveCartItemCommandHandler` no longer is, now that it has to read the cart before it can know how much to release.

### Queries + Handlers

**[Queries/](../../Services/Cart/Cart.Application/Queries/)**

| Query | Returns | Handler responsibility |
| --- | --- | --- |
| `GetCartQuery(UserId)` | `IReadOnlyList<CartItemDto>` | `ICartRepository.GetCartAsync` → `cart.Values.ToList()`. No caching layer (unlike Product) — Redis already *is* the store, so there's nothing to cache in front of |

### Validators (FluentValidation)

**[Validators/](../../Services/Cart/Cart.Application/Validators/)**

- `AddCartItemCommandValidator` — `UserId` not empty; `ProductId` not empty; `ProductName` not empty, ≤200 chars; `UnitPrice` ≥ 0; `Quantity` ≥ 1
- `UpdateCartItemCommandValidator` — `UserId` not empty; `ProductId` not empty; `Quantity` ≥ 1, with a validation message that explicitly points elsewhere: *"Quantity must be at least 1. To remove an item, use the delete endpoint instead."* — encoding the same single-responsibility split Product enforces between soft-delete and update, but here the split is between two literal HTTP endpoints
- No validator exists for `RemoveCartItemCommand`, `ClearCartCommand`, or `GetCartQuery` — `ValidationBehavior` short-circuits to `next()` when no validators are registered for a request type

### Pipeline Behaviors

**[Behaviors/](../../Services/Cart/Cart.Application/Behaviors/)** — copied from Product, namespace adjusted, byte-for-byte identical in shape:

- `ValidationBehavior<TRequest,TResponse>` — runs every matching `IValidator<TRequest>`, aggregates failures, throws `FluentValidation.ValidationException` if any exist.
- `LoggingBehavior<TRequest,TResponse>` — logs `"Handling {RequestName}"` before `next()` and `"Handled {RequestName}"` after.

Both registered as open-generic `IPipelineBehavior<,>` in `Program.cs`, `ValidationBehavior` first, then `LoggingBehavior` — same order as Product.

### Interfaces (the inversion point)

**[ICartRepository](../../Services/Cart/Cart.Application/Interfaces/ICartRepository.cs)**:

```csharp
public interface ICartRepository
{
    Task<IReadOnlyDictionary<Guid, CartItemDto>> GetCartAsync(Guid userId, CancellationToken ct);
    Task UpsertItemAsync(Guid userId, CartItemDto item, CancellationToken ct);
    Task RemoveItemAsync(Guid userId, Guid productId, CancellationToken ct);
    Task ClearCartAsync(Guid userId, CancellationToken ct);
}
```

Four methods total — smaller than either `IProductRepository` or `ICategoryRepository`, since there's no separate add/update split at the repository level: both `AddCartItemCommandHandler` and `UpdateCartItemCommandHandler` funnel through the same `UpsertItemAsync`. The keyed-by-`Guid` dictionary return type (rather than a flat list) is deliberate — it's exactly the shape both `AddCartItemCommandHandler` and `UpdateCartItemCommandHandler` need for an O(1) `TryGetValue` lookup by `ProductId`.

**[ICartEventPublisher](../../Services/Cart/Cart.Application/Interfaces/ICartEventPublisher.cs)** — the newest interface in this layer, and the one exception to "Cart.Application never references `ShopFlow.Shared`":

```csharp
public interface ICartEventPublisher
{
    Task PublishStockAdjustedAsync(Guid productId, int quantityDelta, CancellationToken ct);
}
```

Deliberately shaped like `IOrderEventPublisher` in Order — a plain `(Guid, int, CancellationToken)` signature, not the raw `CartStockAdjustedEvent` record itself, so `Cart.Application` still never needs to reference `ShopFlow.Shared` (that stays confined to `Cart.Infrastructure`'s implementation, [§3](#3-cartinfrastructure--redis-persistence-event-consumer-jwt-settings) below) — the same inversion Order's Abstract section already documents.

---

## 3. Cart.Infrastructure — Redis Persistence, Event Consumer, JWT Settings

**[Cart.Infrastructure.csproj](../../Services/Cart/Cart.Infrastructure/Cart.Infrastructure.csproj)** references Domain + Application + **`ShopFlow.Shared`**, plus `StackExchange.Redis`, `MassTransit.RabbitMQ`, `Microsoft.Extensions.Options`. Notably **no EF Core package at all** — this is the first service in ShopFlow with no SQL Server dependency anywhere in its stack. Also no JWT-signing package, same as Product — Cart only validates tokens.

### Persistence

**[CartKeys](../../Services/Cart/Cart.Infrastructure/Persistence/CartKeys.cs)** — `ForUser(userId) => $"cart:{userId}"`, the sole Redis key naming policy for the service (contrast with Product's `CacheKeys`, which has two key shapes for two different cached resources).

**[RedisCartRepository : ICartRepository](../../Services/Cart/Cart.Infrastructure/Persistence/RedisCartRepository.cs)** over `StackExchange.Redis`, backed by a single Redis **Hash** per user (field = `productId.ToString()`, value = the full `CartItemDto` as JSON):

- `GetCartAsync` — `HashGetAllAsync(key)`; deserializes every field back into a `CartItemDto`, keyed by `Guid.Parse(field name)`. **If the hash is non-empty, also resets the key's TTL** (`KeyExpireAsync`) — a sliding-TTL read, not just a passive lookup.
- `UpsertItemAsync` — `HashSetAsync(key, productId, json)` (overwrites in place if the field already exists — Redis hash-set semantics, so update-in-place is free), then unconditionally `KeyExpireAsync(key, Ttl)`.
- `RemoveItemAsync` — `HashDeleteAsync` (no-op if the field is already absent — this is *why* `RemoveCartItemCommand` is idempotent, the guarantee comes from Redis's own semantics, not from an application-level existence check); if the hash still has any fields left, resets the TTL again.
- `ClearCartAsync` — `KeyDeleteAsync` on the whole key. Simplest possible implementation — no iteration over fields.
- `Database` is computed fresh (`_connectionMultiplexer.GetDatabase()`) on every access, same pattern as Product's `RedisCacheService`.

**TTL policy**: a constant 7-day (`TimeSpan.FromDays(7)`) sliding expiry, reset on every write **and** on any read that returns a non-empty cart. An abandoned cart with zero further activity expires exactly a week after its last touch; an actively-browsed cart never expires as long as it's read or written at least once a week.

### Events

**[OrderPlacedConsumer : IConsumer\<OrderPlacedEvent\>](../../Services/Cart/Cart.Infrastructure/Events/OrderPlacedConsumer.cs)** — the entire handler is one line: `ICartRepository.ClearCartAsync(context.Message.CustomerId, ...)`. Deliberately minimal — no side effects beyond clearing the cart, no acknowledgement event published back, no filtering by order status (any `OrderPlacedEvent`, regardless of what it contains, clears that customer's cart in full).

**[CartEventPublisher : ICartEventPublisher](../../Services/Cart/Cart.Infrastructure/Events/CartEventPublisher.cs)** — the new counterpart, and the first time Cart *publishes* rather than only consumes:

```csharp
public class CartEventPublisher : ICartEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    public CartEventPublisher(IPublishEndpoint publishEndpoint) => _publishEndpoint = publishEndpoint;

    public async Task PublishStockAdjustedAsync(Guid productId, int quantityDelta, CancellationToken ct)
        => await _publishEndpoint.Publish(new CartStockAdjustedEvent(productId, quantityDelta), ct);
}
```

One line, same shape as `OrderEventPublisher` in Order — the entire method is the mapping from the Application-layer interface call to the wire-format record. `CartStockAdjustedEvent(ProductId, QuantityDelta)` is defined in `ShopFlow.Shared.Events` alongside `OrderPlacedEvent`; Product's `CartStockAdjustedConsumer` is the sole subscriber (see [Product-Service.md §3](../Architecture/Product-Service.md#3-productinfrastructure--persistence-caching-messaging-jwt-settings)).

**Known gap — `ClearCartCommand` doesn't release anything**: unlike `AddCartItemCommand`/`UpdateCartItemCommand`/`RemoveCartItemCommand`, clearing the whole cart via `DELETE /api/cart` publishes no `CartStockAdjustedEvent` at all for any of the items being cleared. Every unit those items had reserved in Product stays reserved — a real, currently-unclosed leak, distinct from (and not to be confused with) the natural 7-day TTL expiry described below, which also never publishes a release event when a cart quietly expires unread.

**Important dependency-version deviation**: `MassTransit.RabbitMQ` is pinned to **`8.5.10`** across both `Cart.Infrastructure` and `Cart.Api`, not the `9.2.0` that was the latest stable at planning time. `9.0.0` introduced a mandatory commercial license (`MassTransit.ConfigurationException: License must be specified...`) that this project doesn't hold, discovered during a Docker smoke test. Per [Phase4.md](../Phases/Phase4.md), **future services (Order, Notification) must stay on `8.5.10`** too unless a MassTransit license is acquired.

### Settings

**[JwtSettings](../../Services/Cart/Cart.Infrastructure/Settings/JwtSettings.cs)** — `Secret`, `Issuer`, `Audience` only, identical shape to Product's. No `ExpiryMinutes` — Cart never mints a token.

---

## 4. Cart.Api — Controller, Middleware, Composition Root

**[Cart.API.csproj](../../Services/Cart/Cart.Api/Cart.API.csproj)** (`Sdk="Microsoft.NET.Sdk.Web"`) references Application + Infrastructure, plus `Serilog.AspNetCore`, `Swashbuckle.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `AspNetCore.HealthChecks.Redis`, `FluentValidation.DependencyInjectionExtensions`, `MediatR`, `MassTransit.RabbitMQ`, `Microsoft.AspNetCore.Authentication.JwtBearer`. **No `AspNetCore.HealthChecks.SqlServer`** — there's no SQL Server anywhere in this service to check.

### Endpoints

```text
GET    /api/cart                      [Authorize]  → 200 OK
POST   /api/cart/items                [Authorize]  → 201 Created
PUT    /api/cart/items/{productId:guid}   [Authorize]  → 200 OK / 404 Not Found
DELETE /api/cart/items/{productId:guid}   [Authorize]  → 204 No Content (idempotent — unknown productId is also 204)
DELETE /api/cart                      [Authorize]  → 204 No Content
GET    /health                                     → 200 OK — health status (Redis only)
```

**[CartController](../../Services/Cart/Cart.Api/Controllers/CartController.cs)** — the whole controller is five one-line action methods plus a private `UserId => Guid.Parse(User.FindFirstValue("userId")!)` property, the same claim-derivation pattern as `ProductsController.VendorId`. **No custom authorization policy is registered or needed** — every action carries only the class-level `[Authorize]`, since Cart has no concept of roles or ownership beyond "this JWT's own cart" (there's nothing to distinguish a Vendor from a Customer here). `AddItem`/`UpdateItem` accept local records `AddCartItemRequest(ProductId, ProductName, UnitPrice, Quantity)` / `UpdateCartItemRequest(Quantity)` — neither carries a `UserId` field, confirming it always comes from the claim, not the body.

### Middleware

**[ExceptionHandlingMiddleware](../../Services/Cart/Cart.Api/Middleware/ExceptionHandlingMiddleware.cs)** — identical exception-to-status mapping to Product/Identity, registered before Swagger/auth:

| Exception caught | Status | Body |
| --- | --- | --- |
| `FluentValidation.ValidationException` | 400 | `{ errors: [{ propertyName, errorMessage }] }` |
| `NotFoundException` | 404 | `{ message }` |
| `DomainException` (base, catch-all) | 400 | `{ message }` |
| Any other `Exception` | 500 | Generic message; full exception logged |

Same catch-order discipline (subtype before base) as every other service. No 401/409 mapping — nothing in Cart's exception hierarchy maps to either.

### Composition root — Program.cs

**[Program.cs](../../Services/Cart/Cart.Api/Program.cs)**:

- Binds `JwtSettings` from configuration.
- Registers `IConnectionMultiplexer` as a **Singleton** (`ConnectionMultiplexer.Connect(...)`, falling back to `"localhost:6379"`) — same pattern as Product's Redis registration, but here it's the *only* store, not a cache in front of one.
- Registers `ICartRepository` as **Scoped** (`RedisCartRepository`), and now `ICartEventPublisher` as **Scoped** (`CartEventPublisher`) alongside it.
- Registers MediatR scanning `Cart.Application`; FluentValidation scanning the same assembly; `ValidationBehavior<,>` then `LoggingBehavior<,>` as open-generic `IPipelineBehavior<,>`.
- **`AddMassTransit`**: `AddConsumer<OrderPlacedConsumer>()`; `UsingRabbitMq` bound to `RabbitMQ:Host`/`User`/`Pass` config (falling back to `localhost`/`guest`/`guest`); `ReceiveEndpoint("order-placed-queue", ...)` with `UseMessageRetry(r => r.Exponential(3, 1s, 10s, 2s))` — three retries, exponential backoff between 1 and 10 seconds with a 2-second step. **`CartEventPublisher` needs no additional registration here** — publishing only needs `IPublishEndpoint`, which `AddMassTransit` already provides regardless of which consumers are configured, so the block's shape is otherwise unchanged even though Cart now publishes as well as consumes.
- Configures JWT Bearer auth the same lazy way as Identity/Product (`AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)`, so `WebApplicationFactory` config overrides take effect in tests); `ClockSkew = TimeSpan.Zero`.
- `AddAuthorization()` with **no named policies at all** — a first among the three services documented so far; Identity and Product both register at least `RequireVendor`/`RequireAdmin`.
- Registers `/health` against **Redis only** — no SQL Server check exists to add.
- **No dev-seed block** — there's nothing to seed; a cart is created implicitly the moment its owner adds a first item.
- `public partial class Program` at the bottom, for `WebApplicationFactory<Program>`.

---

## 5. Test Projects

| Test project | Targets | Style | Notable packages |
| --- | --- | --- | --- |
| **Cart.Domain.Tests** (1 test) | `Cart.Domain` | Pure unit, no mocks — `NotFoundExceptionTests` checks message formatting only. The smallest Domain.Tests project in ShopFlow so far, proportional to the smallest Domain layer | xunit, FluentAssertions |
| **Cart.Application.Tests** (29 tests) | `Cart.Application` | Handlers/validators/behaviors tested against **NSubstitute** mocks of `ICartRepository` **and now `ICartEventPublisher`** — no Redis, no HTTP. Covers both command handlers' branching (new-item vs. existing-item for Add; found vs. not-found for Update) plus `RemoveCartItemCommandHandlerTests`, `ClearCartCommandHandlerTests`, `GetCartQueryHandlerTests` (empty + populated), both validators, `ValidationBehaviorTests`, `LoggingBehaviorTests` — **plus 6 new tests asserting the exact delta published** by Add (always the newly-requested quantity), Update (positive/negative/skipped-when-unchanged), and Remove (negative of the removed quantity, or not published at all if the item wasn't in the cart) | + NSubstitute, FluentValidation, Microsoft.Extensions.Logging.Abstractions |
| **Cart.Infrastructure.Tests** (7 tests) | `Cart.Infrastructure` | `RedisCartRepositoryTests` (5) against a **real Redis** via `Testcontainers.Redis` — roundtrip, upsert-in-place doesn't duplicate hash fields, remove leaves sibling items intact, clear removes the whole key, TTL is ~7 days on write. `OrderPlacedConsumerTests` (1) and the new `CartEventPublisherTests` (1) both spin up a real **in-process MassTransit bus** via `AddMassTransitTestHarness` — the consumer test publishes an `OrderPlacedEvent` and asserts `ICartRepository.ClearCartAsync` (an NSubstitute mock here, not real Redis) was called for that event's `CustomerId`; the publisher test does the reverse, calling `CartEventPublisher.PublishStockAdjustedAsync` directly and asserting via `harness.Published.Any<CartStockAdjustedEvent>()` that the right product/delta went out. **No SQL Server container at all** — the one Infrastructure.Tests project in ShopFlow so far that needs Docker for only a single technology | + NSubstitute, **Testcontainers.Redis**, MassTransit(.Testing) |
| **Cart.Api.Tests** (10 tests) | Full stack via `Cart.Api` | End-to-end HTTP tests through `WebApplicationFactory`, `ICartRepository` swapped for an in-memory fake, MassTransit swapped for its test harness so no test ever dials a real broker | + Microsoft.AspNetCore.Mvc.Testing |

**Cart.Api.Tests fixtures** ([Fixtures/](../../Services/Cart/Cart.Api.Tests/Fixtures/)):
- `CartApiFactory` — the `WebApplicationFactory<Program>` subclass; overrides `JwtSettings` + `ConnectionStrings:Redis` config, swaps `ICartRepository` → `FakeCartRepository` (Singleton, exposed as a public property so tests can inspect it). Its most distinctive step, with no equivalent in Identity or Product: it walks the registered service collection and **removes every descriptor whose service/implementation type lives under the `MassTransit` namespace** — the real `AddMassTransit(...).UsingRabbitMq(...)` call in `Program.cs` would otherwise try to dial a real broker the moment the test host starts — then re-adds `AddMassTransitTestHarness` with the same `OrderPlacedConsumer` registered against the in-memory test transport. **`ICartEventPublisher` stays wired to the real `CartEventPublisher`**, exactly the same choice `OrderApiFactory` makes for `IOrderEventPublisher` — it resolves `IPublishEndpoint` from the test harness instead of a real connection, so a Cart API test could assert against `harness.Published.Any<CartStockAdjustedEvent>()` for real, the same way Order's API tests already do for `OrderPlacedEvent` (no `CartController` test currently exercises this, but the wiring supports it).
- `FakeCartRepository` — a `Dictionary<Guid, Dictionary<Guid, CartItemDto>>` (per-user cart, keyed by product) reproducing `ICartRepository`'s four operations in memory: `RemoveItemAsync`/`ClearCartAsync` are no-ops if the user/product isn't found, matching Redis's own idempotent semantics exactly.
- `JwtTokenHelper` — mints real signed JWTs with the three claims (`userId`, `ClaimTypes.Email`, `ClaimTypes.Role`) `CartController.UserId` expects, since Cart.Api has no login endpoint of its own.

This mirrors Identity's and Product's test strategy: cheap and deterministic near Domain/Application, real infrastructure at the edges — except here "real infrastructure" means a Redis container and an in-process message bus instead of SQL Server.

---

## 5.5 Project Dependency Wiring

```text
┌───────────────────────────────────────────────────────────────────┐
│                          Cart Service                              │
│                                                                     │
│   Production Code                      Test Projects              │
│   ───────────────                      ─────────────              │
│                                                                     │
│   ┌──────────────┐                    ┌───────────────────────┐   │
│   │  Cart.API    │                    │   Cart.Api.Tests      │   │
│   └──────┬───┬───┘                    └───────────┬───────────┘   │
│          │   │ refs                               │ refs          │
│          │   └────────────────┐                   ▼               │
│          │ refs                │      ┌──────────────────────┐    │
│          ▼                     │      │ Cart.Infra.Tests      │    │
│   ┌──────────────────┐         │      └──────────┬───────────┘    │
│   │  Cart.Infra      │◄────────┘                 │ refs           │
│   └───┬─────────┬────┘                           ▼                │
│       │ refs    │ refs         ┌──────────────────────────────┐   │
│       │         │              │   Cart.Application.Tests     │   │
│       │         │              └──────────┬───────────────────┘   │
│       │         │                         │ refs                  │
│       │         ▼                         ▼                       │
│       │  ┌──────────────────┐  ┌──────────────────────────────┐   │
│       │  │  Cart.Application│◄─│    Cart.Domain.Tests          │   │
│       │  └────────┬─────────┘  └──────────────────────────────┘   │
│       │ refs      │ refs                                          │
│       │           ▼                                               │
│       └──►┌──────────────────┐                                    │
│           │   Cart.Domain    │                                    │
│           └──────────────────┘                                    │
│                 (no deps)                                          │
│                                                                     │
│   Cart.Infra also refs ──► Shared/ShopFlow.Shared (event contracts)│
└───────────────────────────────────────────────────────────────────┘
```

| Project | References |
| --- | --- |
| `Cart.Domain` | — |
| `Cart.Application` | `Cart.Domain` |
| `Cart.Infrastructure` | `Cart.Domain` + `Cart.Application` + `ShopFlow.Shared` |
| `Cart.API` | `Cart.Application` + `Cart.Infrastructure` |
| `Cart.Domain.Tests` | `Cart.Domain` |
| `Cart.Application.Tests` | `Cart.Application` |
| `Cart.Infrastructure.Tests` | `Cart.Infrastructure` |
| `Cart.Api.Tests` | `Cart.API` |

`ShopFlow.Shared` is the one dependency with no equivalent in Identity or Product — a shared class library outside `Services/`, referenced only by `Cart.Infrastructure`, never by `Cart.Domain` or `Cart.Application` (the event contract is a wire/messaging concern, not part of the cart's own vocabulary).

---

## 6. Request Flow — End to End Example

`POST /api/cart/items` (as an authenticated user, adding a product already in their cart):

1. **Api**: `CartController.AddItem` reads `UserId` from the `userId` JWT claim, builds `AddCartItemCommand` from the request body + that `UserId`, calls `IMediator.Send`.
2. **Application (pipeline)**: `ValidationBehavior` runs `AddCartItemCommandValidator` — blank product name / negative price / quantity < 1 → `ValidationException` → **Api**'s `ExceptionHandlingMiddleware` → HTTP 400. `LoggingBehavior` logs "Handling `AddCartItemCommand`" around the call.
3. **Application (handler)**: `AddCartItemCommandHandler`:
   - `ICartRepository.GetCartAsync` → **Infrastructure**'s `RedisCartRepository` runs `HashGetAllAsync` against the user's `cart:{userId}` hash, resetting its TTL since it's non-empty.
   - Product already present → new quantity = request quantity + existing quantity (cumulative add).
   - Builds a fresh `CartItemDto` (new name/price win, combined quantity) — no **Domain** layer call at all, since there's no entity to construct through a factory.
   - `ICartRepository.UpsertItemAsync` → **Infrastructure**'s `RedisCartRepository` runs `HashSetAsync` (overwrites the field in place) then `KeyExpireAsync` (resets the 7-day TTL again).
   - `ICartEventPublisher.PublishStockAdjustedAsync(productId, command.Quantity, ct)` → **Infrastructure**'s `CartEventPublisher` publishes `CartStockAdjustedEvent` onto RabbitMQ, **after** the Redis write succeeds — if the publish itself fails, the cart item is already saved regardless (no compensating rollback exists for that ordering).
   - Returns the new `CartItemDto` directly — no separate mapping step, since the DTO *is* what's persisted.
4. **Api**: controller returns HTTP 201 with the `CartItemDto` body — the response is returned **before** anything downstream reacts to the published event; the caller has no way to know from this response alone whether Product has processed the stock adjustment yet.
5. **Downstream, asynchronously**: Product's `CartStockAdjustedConsumer` (on `product-cart-stock-adjusted-queue`) decrements `StockQuantity` by the published delta — see [Product-Service.md §3](../Architecture/Product-Service.md#3-productinfrastructure--persistence-caching-messaging-jwt-settings).

Compare with **order placement clearing the cart** — a flow with no HTTP request at all:

1. The Order Service (Phase 5) publishes `OrderPlacedEvent` to RabbitMQ once an order is placed.
2. **Infrastructure**: MassTransit's `order-placed-queue` receive endpoint delivers it to `OrderPlacedConsumer.Consume`.
3. `OrderPlacedConsumer` calls `ICartRepository.ClearCartAsync(event.CustomerId, ...)` directly — bypassing MediatR, the Application layer, and the API layer entirely, since there's no HTTP caller to respond to and no validation a domain event could fail.
4. `RedisCartRepository.ClearCartAsync` runs `KeyDeleteAsync` on `cart:{customerId}` — the next `GET /api/cart` for that user returns an empty list.

Every arrow that crosses a layer boundary in the HTTP flow crosses through an interface owned by the *inner* layer (`ICartRepository`, now also `ICartEventPublisher`), exactly as in Identity's and Product's request-flow traces — only the event-driven flow skips layers, and it does so deliberately, since a message consumer has no controller and no caller-facing validation contract to honor.

---

## 7. Configuration & Running

Connection strings, RabbitMQ address, and JWT settings all live in [appsettings.Development.json](../../Services/Cart/Cart.Api/appsettings.Development.json) — the base `appsettings.json` has none of these, the same "Development-only for now" pattern as Product. Full run instructions are in [Documentations/RUNNING.md](../RUNNING.md); summary:

```bash
docker compose up -d redis rabbitmq
dotnet run --project Services/Cart/Cart.Api
```

- API: `http://localhost:5019` (per [launchSettings.json](../../Services/Cart/Cart.Api/Properties/launchSettings.json)); Swagger: `/swagger`; health: `/health` (Redis only)
- Through the Gateway (Ocelot), `GET`/`DELETE /api/cart` and `POST`/`PUT`/`DELETE /api/cart/{everything}` both route to `cart-service:80`, each requiring the `Bearer` authentication provider key — see [ocelot.json](../../Gateway/Gateway.Api/ocelot.json)
- No dev seed data — a cart is created implicitly by the first `POST /api/cart/items` for a given user; there is nothing to pre-populate
- `dotnet test ShopFlow.sln` — note `Cart.Infrastructure.Tests` needs Docker running (Testcontainers spins up a single Redis container; no SQL Server container, unlike Identity/Product)

---

## Summary — what each layer answers

| Layer | Answers |
| --- | --- |
| `Cart.Domain` | What can go wrong, and how does it map to an exception? (There's no "what is a valid cart state" question here — see [§1](#1-cartdomain--exceptions-only).) |
| `Cart.Application` | What does the system do for each cart use case — cumulative add vs. strict update vs. idempotent remove — and what does "the cart" mean as data? |
| `Cart.Infrastructure` | How is that fulfilled — a Redis Hash with a sliding TTL, a RabbitMQ consumer, JWT validation settings? |
| `Cart.Api` | How is it exposed over HTTP, how do failures become status codes, and how is everything (including the message bus) wired together at startup? |
