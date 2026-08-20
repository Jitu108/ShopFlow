# Phase 4 — Cart Service

## Project Structure

```text
Shared/
└── ShopFlow.Shared/            (new — event contracts shared across services)

Services/Cart/
├── Cart.Domain/
├── Cart.Application/
├── Cart.Infrastructure/
├── Cart.Api/
├── Cart.Domain.Tests/
├── Cart.Application.Tests/
├── Cart.Infrastructure.Tests/
└── Cart.Api.Tests/
```

`ShopFlow.Shared` was created in this phase rather than deferred to Phase 5, per NFR-21 ("shared event contracts live in a separate class library, never duplicated"). It holds `OrderPlacedEvent`, `OrderItemDto`, and `OrderShippedEvent` — Cart only consumes the first; the other two are defined now so Phase 5 (Order/Notification) only ever *adds* to this library, never edits an existing contract Cart already depends on. It's referenced only by `Cart.Infrastructure` (the event contract is a wire concern, not a Cart domain concept).

---

## Domain Layer

**Entities:** none — Cart has no persisted aggregate. The Redis Hash *is* the persistence; there's nothing for an entity to wrap.

**Exceptions:** ✅ implemented (copied from Product, namespace adjusted)

- `DomainException`
- `NotFoundException`

Quantity/price/name validation lives in FluentValidation at the Application boundary rather than in a domain entity — appropriate since the spec models `CartItem` as a plain data record, not an entity with invariants of its own.

---

## Application Layer

**Commands + Handlers:** ✅ implemented

| Command | Handler responsibility | Status |
| --- | --- | --- |
| `AddCartItemCommand` | Reads current cart; if the product is already present, adds the new quantity to the existing one (cumulative add-to-cart); upserts via `ICartRepository` | ✅ Done |
| `UpdateCartItemCommand` | Loads the cart, throws `NotFoundException` if the product isn't present (an update is not an upsert), replaces the quantity | ✅ Done |
| `RemoveCartItemCommand` | Removes the item; idempotent — removing an already-absent product is a no-op, not an error | ✅ Done |
| `ClearCartCommand` | Deletes the whole `cart:{userId}` key | ✅ Done |

**Queries + Handlers:**

| Query | Handler responsibility | Status |
| --- | --- | --- |
| `GetCartQuery` | Returns all items in the caller's cart as a flat list | ✅ Done |

**DTOs:** ✅ implemented — `CartItemDto` (`ProductId`, `ProductName`, `UnitPrice`, `Quantity`), matching the spec's `CartItem` record exactly.

**Interfaces:** ✅ implemented

- `ICartRepository` — `GetCartAsync`, `UpsertItemAsync`, `RemoveItemAsync`, `ClearCartAsync`

**Data-model decision:** the spec describes the Redis hash as `cart:{userId}` → field `productId` → value `quantity`. Since the API also needs to return `productName`/`unitPrice` on every read, and a synchronous call to Product Service on every cart read would conflict with Cart's "fast, self-contained" design goal, each hash field stores the full `CartItemDto` as JSON instead of a bare quantity. This is a deliberate, documented deviation from the literal spec text — confirmed with the user before implementation.

**Validators (FluentValidation):** ✅ implemented

- `AddCartItemCommandValidator` — `UserId`/`ProductId` not empty, `ProductName` not blank (≤200 chars), `UnitPrice` ≥ 0, `Quantity` ≥ 1
- `UpdateCartItemCommandValidator` — `UserId`/`ProductId` not empty, `Quantity` ≥ 1 (setting quantity to 0 goes through the DELETE endpoint instead, keeping each command single-responsibility)

**Pipeline Behaviors:** ✅ implemented (copied from Product, namespace adjusted)

- `ValidationBehavior<TRequest, TResponse>`
- `LoggingBehavior<TRequest, TResponse>`

---

## Infrastructure Layer

**Persistence:** ✅ implemented

- `CartKeys.ForUser(userId) => $"cart:{userId}"`
- `RedisCartRepository : ICartRepository` over `StackExchange.Redis` — `HashSetAsync`/`HashGetAllAsync`/`HashDeleteAsync`/`KeyDeleteAsync` against `IConnectionMultiplexer.GetDatabase()`. TTL is `KeyExpireAsync(key, TimeSpan.FromDays(7))`, reset on every write **and** on any read that returns a non-empty cart (sliding TTL per FR-26).

**Events:** ✅ implemented

- `OrderPlacedConsumer : IConsumer<OrderPlacedEvent>` — clears the caller's cart by `CustomerId` on every `OrderPlacedEvent`. Deliberately minimal: no side effects beyond `ICartRepository.ClearCartAsync`.

**Important deviation from the plan — MassTransit version:** the plan called for MassTransit(.RabbitMQ) latest stable, which at planning time was `9.2.0`. During the Docker smoke test, `9.2.0` failed at startup with `MassTransit.ConfigurationException: License must be specified with SetLicense/SetLicenseLocation...` — MassTransit introduced a mandatory commercial license starting at `9.0.0`. Since this project has no such license, **all MassTransit packages are pinned to `8.5.10`**, the last fully open-source (Apache 2.0) release before the licensing change. This applies to `Cart.Infrastructure` and `Cart.Api`'s `MassTransit.RabbitMQ` references — keep future services (Order, Notification in Phase 5) on `8.5.10` too unless the project acquires a MassTransit license.

---

## API Layer

**Endpoints:** ✅ implemented

```text
GET    /api/cart                      [Authorize]  → 200
POST   /api/cart/items                [Authorize]  → 201
PUT    /api/cart/items/{productId}    [Authorize]  → 200 / 404 (unknown productId)
DELETE /api/cart/items/{productId}    [Authorize]  → 204 (idempotent — unknown productId also 204)
DELETE /api/cart                      [Authorize]  → 204
```

`UserId` is read from the `userId` JWT claim (the same claim shape Identity issues), never from the request body or route — same pattern as Product's `VendorId` handling. No custom authorization policy is needed; Cart's endpoints only require a valid JWT.

**Middleware:** ✅ implemented — `ExceptionHandlingMiddleware`, same exception-to-status mapping as Product/Identity:

| Exception | HTTP Status |
| --- | --- |
| `ValidationException` | 400 |
| `NotFoundException` | 404 |
| `DomainException` | 400 |
| Any unhandled `Exception` | 500 |

**Program.cs wiring:** ✅ implemented

- `JwtSettings` bound from config, validated against the same secret/issuer/audience as Identity/Product
- `IConnectionMultiplexer` registered as a singleton for Redis (`ConnectionStrings:Redis`); `ICartRepository → RedisCartRepository` (Scoped)
- MediatR scanning `Cart.Application`; `ValidationBehavior` + `LoggingBehavior` registered as open-generic `IPipelineBehavior<,>`
- `AddMassTransit` with `AddConsumer<OrderPlacedConsumer>()`, `UsingRabbitMq` bound to `RabbitMQ:Host`/`User`/`Pass` config, `ReceiveEndpoint("order-placed-queue", ...)` with exponential retry (3 attempts, 1s/10s/2s)
- No custom authorization policy — plain `[Authorize]`
- `public partial class Program` for `WebApplicationFactory`

**Health check:** ✅ implemented — `/health` (Redis only; no SQL Server check, since Cart has no SQL dependency)

---

## Test Projects

**Cart.Domain.Tests** (1 test): ✅ implemented

- `NotFoundExceptionTests` — message formatting

**Cart.Application.Tests** (23 tests) — mocked `ICartRepository` (NSubstitute + FluentAssertions): ✅ implemented

- `AddCartItemCommandHandlerTests` (new product upserts at requested quantity; existing product adds to current quantity)
- `UpdateCartItemCommandHandlerTests` (existing product updates in place; unknown product throws `NotFoundException`)
- `RemoveCartItemCommandHandlerTests`, `ClearCartCommandHandlerTests`
- `GetCartQueryHandlerTests` (empty cart, populated cart)
- `AddCartItemCommandValidatorTests`, `UpdateCartItemCommandValidatorTests`
- `ValidationBehaviorTests`, `LoggingBehaviorTests`

**Cart.Infrastructure.Tests** (6 tests) — Testcontainers, real containers: ✅ implemented

- `RedisCartRepositoryTests` (5, real Redis via `Testcontainers.Redis`) — add/get roundtrip, upsert-in-place doesn't duplicate hash fields, remove leaves sibling items intact, clear removes the whole key, TTL is set to ~7 days on write
- `OrderPlacedConsumerTests` (1, real in-process MassTransit bus via `AddMassTransitTestHarness`) — publishing `OrderPlacedEvent` triggers `ICartRepository.ClearCartAsync` for the event's `CustomerId`

**Cart.Api.Tests** (10 tests) — `WebApplicationFactory`: ✅ implemented

Fixtures (mirroring Product's `ProductApiFactory` pattern):

- `CartApiFactory` — swaps `ICartRepository` for `FakeCartRepository`; removes every MassTransit-namespaced service descriptor that `Program.cs`'s `AddMassTransit(...).UsingRabbitMq(...)` registered and replaces them with `AddMassTransitTestHarness`, so API tests never attempt a real broker connection at test-host startup
- `FakeCartRepository`, `JwtTokenHelper`

Tests:

- `CartControllerTests` (10) — no-auth 401 (GET/POST), empty cart 200 + `[]`, add item 201 + body, invalid body (quantity 0) 400, update existing item 200, update unknown product 404, remove existing item 204, remove unknown item 204 (idempotent), clear cart 204

**Total: 40 tests, all passing.**

---

## Live End-to-End Verification

Beyond the 40 automated tests, the service was built and run as a real Docker container (`redis`, `rabbitmq`, `identity-service`, `cart-service`) for a full manual round trip: registered a customer through Identity, used the real JWT against Cart to exercise all 5 endpoints — added an item (confirmed via `redis-cli HGETALL`/`TTL` that the hash and ~7-day TTL were exactly as designed), updated its quantity, confirmed a 404 on an unknown product, removed the item, added another, then cleared the whole cart and confirmed the Redis key was gone (`EXISTS` → 0).

The `OrderPlacedEvent` auto-clear path (FR-25) was also verified live, not just via the automated consumer test: since Order Service doesn't exist until Phase 5, a real `OrderPlacedEvent` was published directly onto the `ShopFlow.Shared.Events:OrderPlacedEvent` exchange via RabbitMQ's management HTTP API (matching MassTransit's interoperable JSON envelope), and the target user's cart cleared within seconds — confirmed both by `redis-cli EXISTS` flipping to 0 and by RabbitMQ's exchange/queue topology (`ShopFlow.Shared.Events:OrderPlacedEvent` fanout exchange, correctly bound to `order-placed-queue`, which had exactly 1 active running consumer). This is the practical stand-in for full end-to-end until Order Service exists in Phase 5 — but the actual exchange name, binding, and consumption were all exercised for real, not assumed.

**Gap found during this phase (see [STATUS.md](../STATUS.md#gaps-closed)):** MassTransit 9.x requires a commercial license; all MassTransit packages were pinned to `8.5.10` (last Apache-2.0 release) after this was discovered failing at container startup.

---

## NuGet Packages

| Package | Project | Status |
| --- | --- | --- |
| `MediatR 12.5.0` | `Cart.Application`, `Cart.Api` | ✅ Added |
| `FluentValidation 11.11.0` | `Cart.Application`, `Cart.Application.Tests` | ✅ Added |
| `Microsoft.Extensions.Logging.Abstractions 10.0.0` | `Cart.Application`, `Cart.Application.Tests` | ✅ Added |
| `FluentAssertions 6.12.2` | all `.Tests` projects | ✅ Added |
| `NSubstitute 5.3.0` | `Cart.Application.Tests`, `Cart.Infrastructure.Tests` | ✅ Added |
| `StackExchange.Redis 2.8.24` | `Cart.Infrastructure` | ✅ Added |
| `MassTransit.RabbitMQ 8.5.10` | `Cart.Infrastructure`, `Cart.Api` | ✅ Added (pinned below latest — see Infrastructure Layer note) |
| `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0` | `Cart.Api` | ✅ Added |
| `AspNetCore.HealthChecks.Redis 9.0.0` | `Cart.Api` | ✅ Added |
| `FluentValidation.DependencyInjectionExtensions 11.11.0` | `Cart.Api` | ✅ Added |
| `Serilog.AspNetCore 9.0.0` | `Cart.Api` | ✅ Added |
| `Testcontainers.Redis 4.4.0` | `Cart.Infrastructure.Tests` | ✅ Added |
| `Microsoft.Extensions.DependencyInjection 10.0.0` | `Cart.Infrastructure.Tests` | ✅ Added (for `AddMassTransitTestHarness`) |
| `Microsoft.AspNetCore.Mvc.Testing 10.0.0` | `Cart.Api.Tests` | ✅ Added |

---

## How to Run

```bash
# 1. Start Redis and RabbitMQ
docker compose up -d redis rabbitmq

# 2. Run the Cart Service
dotnet run --project Services/Cart/Cart.Api
```

**URLs:**

| URL | Purpose |
| --- | ------------ |
| `http://localhost:5019` (or the port `dotnet run` selects) | API base |
| `.../swagger` | Swagger UI |
| `.../health` | Health check |

Via Docker Compose, the service is reachable at `http://localhost:5004`.

**Run tests:**

```bash
dotnet test ShopFlow.sln
```

> `Cart.Infrastructure.Tests` uses Testcontainers (Redis) — Docker must be running.

---

## TDD Order for Phase 4

```text
1. ✅ Domain exception test        → NotFoundException
2. ✅ Validator tests              → AddCartItemCommandValidator, UpdateCartItemCommandValidator
3. ✅ Command handler tests        → Add/Update/Remove/Clear, incl. cumulative-add and not-found behavior
4. ✅ Query handler test           → GetCartQuery
5. ✅ Pipeline behavior tests      → ValidationBehavior, LoggingBehavior
6. ✅ Repository test              → RedisCartRepository (Testcontainers, real Redis)
7. ✅ Event consumer test          → OrderPlacedConsumer (real in-process MassTransit bus)
8. ✅ API endpoint tests           → WebApplicationFactory
```
