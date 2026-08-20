# Phase 4 — Cart Service: Implementation Plan

## Context

ShopFlow follows a 7-phase build order (documented in `Documentations/ShopFlow-Approach.md` and tracked live in `Documentations/STATUS.md`). Phases 1-3 (Infrastructure, Identity Service, Product Service) are complete — both services have full Clean Architecture layers, passing test suites, and were verified end-to-end via Docker. Phase 4 is the **Cart Service**, currently pending: `Services/Cart/` is an empty directory, and the current branch (`dev/TJKG-006-cart-service`) is where this work belongs.

Cart is the simplest of the 7 services by design — no SQL, pure Redis, and it doesn't get called by Order Service directly; it clears itself by consuming an `OrderPlacedEvent` off RabbitMQ. This is also the first service in the repo to touch MassTransit/RabbitMQ, so part of this phase is standing up that plumbing correctly for Order and Notification services to reuse in Phase 5.

**Spec** (`Documentations/ShopFlow-ProjectSpec.md` lines 429-456, FR-20–26, NFR-21):
- Endpoints (all `[Authorize]`): `GET /api/cart`, `POST /api/cart/items`, `PUT /api/cart/items/{productId}`, `DELETE /api/cart/items/{productId}`, `DELETE /api/cart`
- Redis Hash: key `cart:{userId}`, TTL 7 days sliding, reset on every interaction
- Subscribes to `OrderPlacedEvent` via RabbitMQ/MassTransit → clears the cart
- Dependencies: Identity Service (done), RabbitMQ (running in docker-compose, no consumer code anywhere yet)

**Two design decisions confirmed with the user:**
1. Store the full `CartItemDto` (productId, name, price, qty) as JSON in each Redis hash field — not quantity alone — since the API must return name/price and a synchronous call to Product Service on every cart read would break Cart's "fast, self-contained" design goal.
2. Create `Shared/ShopFlow.Shared` now, containing `OrderPlacedEvent`/`OrderShippedEvent`/`OrderItemDto`, rather than a local placeholder — avoids duplication NFR-21 explicitly forbids and avoids a MassTransit type-name mismatch when Order Service starts publishing in Phase 5 (MassTransit's default RabbitMQ topology binds exchanges by the message type's fully-qualified name).

## Patterns to reuse (from Product/Identity services — do not reinvent)

- **Scaffold**: 8 projects per service (`{Svc}.Domain[.Tests]`, `.Application[.Tests]`, `.Infrastructure[.Tests]`, `.Api[.Tests]`, API csproj named `{Svc}.API.csproj`). Copy csproj shape from `Services/Product/*`.
- **Redis DI**: `IConnectionMultiplexer` singleton from `ConnectionStrings:Redis`, exactly as in [Program.cs](Services/Product/Product.Api/Program.cs#L43-L44).
- **JWT**: Copy the `AddAuthentication`/`AddJwtBearer`/`AddOptions<JwtBearerOptions>` block from [Program.cs](Services/Product/Product.Api/Program.cs#L65-L87) verbatim, plus a local `JwtSettings` class (no shared settings lib exists). Cart only needs plain `[Authorize]` — no custom policy.
- **MediatR/FluentValidation pipeline**: Copy `ValidationBehavior.cs` and `LoggingBehavior.cs` from `Services/Product/Product.Application/Behaviors/` into `Cart.Application/Behaviors/` (namespace `Cart.Application.Behaviors`).
- **Tests**: xUnit + FluentAssertions + NSubstitute. API tests via `WebApplicationFactory<Program>` — copy `Services/Product/Product.Api.Tests/Fixtures/ProductApiFactory.cs` and `JwtTokenHelper.cs` patterns. Infra tests via `Testcontainers.Redis`, modeled on `Services/Product/Product.Infrastructure.Tests/Caching/RedisCacheServiceTests.cs`.
- **Solution file**: Use `dotnet sln add --solution-folder <name> <csproj>` rather than hand-editing `ShopFlow.sln` (Phase 2's own issue log shows hand-edited `.sln` GUIDs caused a bug).

## Step-by-step plan

### 1. Create `Shared/ShopFlow.Shared`
Plain `net10.0` class library, no packages. Files:
- `Shared/ShopFlow.Shared/Events/OrderItemDto.cs`
- `Shared/ShopFlow.Shared/Events/OrderPlacedEvent.cs`
- `Shared/ShopFlow.Shared/Events/OrderShippedEvent.cs`

Referenced only by `Cart.Infrastructure` (event contracts are a wire concern, not a Cart domain concept).

### 2. Scaffold the 8 Cart projects
`Services/Cart/Cart.Domain[.Tests]`, `Cart.Application[.Tests]`, `Cart.Infrastructure[.Tests]`, `Cart.Api[.Tests]` (csproj `Cart.API.csproj`). Package deltas vs. Product: drop `EntityFrameworkCore.SqlServer`/`InMemory`/`HealthChecks.SqlServer` entirely (no SQL anywhere in Cart); add `MassTransit.RabbitMQ` to `Cart.Infrastructure` and `Cart.Api` (new to the whole repo — pick current stable 8.x compatible with net10.0). Reference `ShopFlow.Shared` from `Cart.Infrastructure`.

### 3. Domain layer (`Cart.Domain`) — thin by design
Cart has no persisted aggregate; keep this layer to `Exceptions/DomainException.cs` and `Exceptions/NotFoundException.cs` (copied from Product, namespace `Cart.Domain.Exceptions`). Quantity validation lives in FluentValidation at the Application boundary, not a domain entity — appropriate since the spec models `CartItem` as a plain record, not an entity with invariants.

### 4. Application layer
- `DTOs/CartItemDto.cs` — record matching spec (`ProductId`, `ProductName`, `UnitPrice`, `Quantity`)
- `Interfaces/ICartRepository.cs` — `GetCartAsync`, `UpsertItemAsync`, `RemoveItemAsync`, `ClearCartAsync`
- Commands: `AddCartItemCommand`, `UpdateCartItemCommand`, `RemoveCartItemCommand`, `ClearCartCommand` (+ handlers), `Queries/GetCartQuery` (+ handler) — same file-pairing convention as `Product.Application/Commands`
- Validators: `AddCartItemCommandValidator` (ProductId not empty, ProductName not blank, UnitPrice ≥ 0, Quantity ≥ 1), `UpdateCartItemCommandValidator` (Quantity ≥ 1 — updating to 0 goes through `RemoveCartItemCommand`/DELETE instead)
- `Behaviors/ValidationBehavior.cs`, `Behaviors/LoggingBehavior.cs` — copied from Product
- `UserId` always comes from the JWT `userId` claim in the controller, never from the request body — same pattern as Product's `VendorId` handling
- TDD: write handler/validator/behavior tests first (NSubstitute-mocked `ICartRepository`), matching `Product.Application.Tests` structure

### 5. Infrastructure layer — `RedisCartRepository`
- `Persistence/CartKeys.cs` — `CartKeys.ForUser(userId) => $"cart:{userId}"`
- `Persistence/RedisCartRepository.cs` — `HashSetAsync`/`HashGetAllAsync`/`HashDeleteAsync`/`KeyDeleteAsync` against `IConnectionMultiplexer.GetDatabase()`; each hash field value is JSON-serialized `CartItemDto`; `KeyExpireAsync(key, TimeSpan.FromDays(7))` called on every write **and** on reads that return non-empty results (sliding TTL per FR-26)
- TDD: `Cart.Infrastructure.Tests/Persistence/RedisCartRepositoryTests.cs` against real Redis via `Testcontainers.Redis` first — cover add/get roundtrip, upsert-in-place (no duplicate fields), remove leaves siblings intact, clear removes the whole key, TTL is set/reset on writes

### 6. MassTransit consumer — `OrderPlacedConsumer`
- `Events/OrderPlacedConsumer.cs` implementing `IConsumer<OrderPlacedEvent>` (from `ShopFlow.Shared.Events`), calling `ICartRepository.ClearCartAsync(context.Message.CustomerId, ...)`
- TDD: unit test first with NSubstitute over `ICartRepository`, no broker needed for the logic test
- Optionally add one `Testcontainers.RabbitMq`-based integration test that boots a real broker, publishes `OrderPlacedEvent`, and asserts the Redis key clears — this is the first RabbitMQ integration test in the repo and de-risks Phase 5's assumption that the wiring works

### 7. API layer
- `Controllers/CartController.cs` — thin, MediatR-only, `[Authorize]` at controller level (no custom policy), maps the 5 endpoints to the 5 commands/query above
- `Middleware/ExceptionHandlingMiddleware.cs` — copied from Product (`ValidationException`→400, `NotFoundException`→404, `DomainException`→400, unhandled→500)
- `Infrastructure/Settings/JwtSettings.cs` — local copy, same shape as Product's
- `Program.cs` — Redis DI + `ICartRepository` registration, JWT bearer setup (copied), MediatR/FluentValidation pipeline registration, `AddMassTransit` with `AddConsumer<OrderPlacedConsumer>()` and `ReceiveEndpoint("order-placed-queue", ...)` + exponential retry (3 retries, 1s/10s/2s), Redis-only health check (no SQL Server check)
- Test fixtures: `Fixtures/CartApiFactory.cs` (fake `ICartRepository` via `RemoveAll`/`AddSingleton`, no EF InMemory needed), `Fixtures/JwtTokenHelper.cs` (copied from Product), `Fixtures/FakeCartRepository.cs`
- **Watch item**: `WebApplicationFactory` must not let MassTransit's hosted service try to connect to a real broker during API tests — either skip `AddMassTransit` in the test host via `ConfigureTestServices`, or use MassTransit's in-memory test transport. Verify this early; otherwise every API test can hang.
- `CartControllerTests.cs` cases: no-auth→401 on all endpoints, empty cart→200+[], add item→200/201+body, invalid body (qty 0)→400, update existing→200, update unknown productId→404, delete item→204 (idempotent on unknown id, matching Identity's `RefreshTokenRepository.Revoke` no-op precedent), delete whole cart→204

### 8. docker-compose.yml
Uncomment the `cart-service` block (currently [docker-compose.yml:154-176](docker-compose.yml#L154-L176)) and fix two things vs. the existing stub:
- `Redis__ConnectionString` → `ConnectionStrings__Redis` (matches Product's convention and `Program.cs`'s `GetConnectionString("Redis")` call — the stub currently uses a different key name than Product does)
- Add `ASPNETCORE_ENVIRONMENT=Development`, `JwtSettings__Issuer=ShopFlow`, `JwtSettings__Audience=ShopFlow` (present on identity-service/product-service blocks, missing from the cart-service stub — needed for JWT validation to succeed)

Also create `Services/Cart/Dockerfile` (copy Product's, adjust project names) and `Cart.Api/appsettings.json` + `appsettings.Development.json` (Redis + RabbitMQ + JwtSettings sections, no SQL connection string).

### 9. Solution file
```
dotnet sln ShopFlow.sln add --solution-folder Shared Shared/ShopFlow.Shared/ShopFlow.Shared.csproj
dotnet sln ShopFlow.sln add --solution-folder Cart Services/Cart/Cart.Domain/Cart.Domain.csproj
... (remaining 7 Cart.* projects, same --solution-folder Cart)
```

### 10. Verification
- **Unit**: `Cart.Domain.Tests`, `Cart.Application.Tests` (handlers/validators/behaviors), `Cart.Infrastructure.Tests/Events/OrderPlacedConsumerTests` (NSubstitute)
- **Integration**: `Cart.Infrastructure.Tests/Persistence/RedisCartRepositoryTests` (Testcontainers.Redis, real Redis); optional Testcontainers.RabbitMq consumer test
- **API**: `Cart.Api.Tests/Controllers/CartControllerTests` (WebApplicationFactory + JwtTokenHelper + FakeCartRepository)
- **Build/test gate**: `dotnet build ShopFlow.sln && dotnet test ShopFlow.sln`
- **Manual end-to-end smoke test** (matching Phase 3's documented approach): `docker compose up -d` for `redis`, `rabbitmq`, `identity-service`, `cart-service`; get a real JWT via Identity; exercise all 5 Cart endpoints; verify via `redis-cli HGETALL cart:{userId}` and `redis-cli TTL cart:{userId}` that data and TTL behave correctly. Since Order Service doesn't exist until Phase 5, manually publish a test `OrderPlacedEvent` via the RabbitMQ management UI (`http://localhost:15672`) to confirm the consumer clears the cart — document this as an explicit, temporary stand-in for full end-to-end in `Documentations/Phases/Phase4.md`.
- Finish by writing `Documentations/Phases/Phase4.md` (following the Phase2.md/Phase3.md template) and updating `Documentations/STATUS.md`'s phase table (Phase 4 → Complete, `Shared/` row → Complete) and test-count totals.

## Critical files
- `Documentations/ShopFlow-ProjectSpec.md` (Cart spec, FR-20–26, NFR-21)
- `Services/Product/Product.Api/Program.cs` (DI/JWT/MediatR/Redis wiring template)
- `Services/Product/Product.Infrastructure/Caching/RedisCacheService.cs` + `Product.Infrastructure.Tests/Caching/RedisCacheServiceTests.cs` (Redis + Testcontainers pattern)
- `Services/Product/Product.Api.Tests/Fixtures/ProductApiFactory.cs` + `JwtTokenHelper.cs` (test fixture pattern)
- `docker-compose.yml` (cart-service block to uncomment/fix)
- `ShopFlow.sln` (needs Shared + Cart entries)
- `Documentations/Phases/Phase3.md` (doc template/TDD-order convention to follow)
