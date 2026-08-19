# Phase 3 — Product Service

## Project Structure

```text
Services/Product/
├── Product.Domain/
├── Product.Application/
├── Product.Infrastructure/
├── Product.Api/
├── Product.Domain.Tests/
├── Product.Application.Tests/
├── Product.Infrastructure.Tests/
└── Product.Api.Tests/
```

Note: the catalog entity is named `ProductEntity` rather than `Product` — naming it `Product` collides with the root namespace segment shared by every project in this service (`Product.Domain`, `Product.Application`, ...), which fails to compile as soon as another project references the type (`CS0118: 'Product' is a namespace but is used like a type`).

---

## Domain Layer

**Entities:** ✅ implemented

- `ProductEntity` — `VendorId`, `Name`, `Description`, `Price`, `StockQuantity`, `IsActive`, `CreatedAt`/`UpdatedAt`, `CategoryId` (FK) + `Category` navigation
- `Category` — `Name`, `Products[]` navigation

**Exceptions:**

- `DomainException`
- `NotFoundException`

**Invariants enforced in `ProductEntity.Create` / `.Update`:** name required, price ≥ 0, stock quantity ≥ 0. `Deactivate()` implements the DELETE endpoint as a soft delete (`IsActive = false`) rather than a hard delete, so historical references (e.g. from future Order/Cart services) stay valid.

---

## Application Layer

**Commands + Handlers:** ✅ implemented

| Command | Handler responsibility | Status |
| --- | --- | --- |
| `CreateProductCommand` | Creates `ProductEntity`, persists via `IProductRepository`, invalidates `product:catalog` cache | ✅ Done |
| `UpdateProductCommand` | Loads product, verifies the calling vendor owns it (else `NotFoundException`), updates, invalidates `product:{id}` + `product:catalog` | ✅ Done |
| `DeleteProductCommand` | Loads product, verifies ownership, calls `Deactivate()`, invalidates both cache keys | ✅ Done |

**Queries + Handlers:**

| Query | Handler responsibility | Status |
| --- | --- | --- |
| `GetProductByIdQuery` | Cache-aside: `product:{id}` → SQL fallback → populate cache (10 min) | ✅ Done |
| `GetProductListQuery` | Cache-aside: `product:catalog` → SQL fallback (active products only) → populate cache (5 min) | ✅ Done |
| `GetVendorProductsQuery` | Direct repository read, no caching (vendor-scoped, low traffic) | ✅ Done |

**DTOs:** ✅ implemented — `ProductDto`

**Interfaces:** ✅ implemented

- `IProductRepository` — `GetByIdAsync`, `GetAllActiveAsync`, `GetByVendorIdAsync`, `AddAsync`, `UpdateAsync`
- `ICacheService` — `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`

**Validators (FluentValidation):** ✅ implemented

- `CreateProductCommandValidator` / `UpdateProductCommandValidator` — name required (≤200 chars), price/stock ≥ 0, category required

**Pipeline Behaviors:**

- `ValidationBehavior<TRequest, TResponse>` ✅ Done
- `LoggingBehavior<TRequest, TResponse>` ✅ Done (left pending in Identity's Phase 2 — implemented here since the pattern was needed)

---

## Infrastructure Layer

**Persistence:** ✅ implemented

- `AppDbContext : DbContext` — `Products`, `Categories` DbSets; `Product.CategoryId` → `Category` FK (`DeleteBehavior.Restrict`); `VendorId` indexed
- `ProductRepository : IProductRepository` ✅
- EF Core migrations → `ProductDb` ⬜ Pending (currently `EnsureCreated()` at startup in Development, matching Identity's approach)

**Caching:** ✅ implemented

- `RedisCacheService : ICacheService` over `StackExchange.Redis` — JSON-serialized values, sliding-window-style expiry passed per call (10 min for single product, 5 min for catalog list)

---

## API Layer

**Endpoints:** ✅ implemented

```text
GET    /api/products                              → 200 (public, cached)
GET    /api/products/{id}                         → 200 (public, cached) / 404
POST   /api/products                  [RequireVendor]  → 201
PUT    /api/products/{id}             [RequireVendor, owner-only]  → 200 / 404
DELETE /api/products/{id}             [RequireVendor, owner-only]  → 204 / 404
GET    /api/vendors/{id}/products     [RequireVendor]  → 200
```

`VendorId` for `POST`/`PUT`/`DELETE` is read from the `userId` JWT claim, not the request body — the same claim shape Identity issues (`userId`, `email`, `role`, `emailVerified`).

**Middleware:** ✅ implemented

`ExceptionHandlingMiddleware` — same exception-to-status mapping pattern as Identity, minus the auth-specific exceptions Identity has (`InvalidCredentialsException`, `DuplicateEmailException`) since they don't apply here:

| Exception | HTTP Status |
| --- | --- |
| `ValidationException` | 400 |
| `NotFoundException` | 404 |
| `DomainException` | 400 |
| Any unhandled `Exception` | 500 |

**Program.cs wiring:** ✅ implemented

- `JwtSettings` bound from config, validated against the **same secret/issuer/audience as Identity** — a JWT issued by Identity's `/api/auth/login` works here unchanged
- `AppDbContext` registered for SQL Server; `IConnectionMultiplexer` registered as a singleton for Redis
- `IProductRepository → ProductRepository`, `ICacheService → RedisCacheService` (Scoped)
- MediatR scanning `Product.Application`; `ValidationBehavior` + `LoggingBehavior` registered as open-generic `IPipelineBehavior<,>`
- `RequireVendor` authorization policy (role check only — no separate `RequireAdmin`/`RequireVerifiedEmail`, those live in Identity)
- `public partial class Program` for `WebApplicationFactory`

**Health check:** ✅ implemented

- `/health` — SQL Server (`AspNetCore.HealthChecks.SqlServer`) + Redis (`AspNetCore.HealthChecks.Redis`)

---

## Test Projects

**Product.Domain.Tests** (10 tests) — pure unit, no mocks: ✅ implemented

- `ProductTests` — create with valid data, blank name / negative price / negative stock throw, update changes fields + bumps `UpdatedAt`, deactivate sets `IsActive = false`
- `CategoryTests` — create with valid/blank name

**Product.Application.Tests** (34 tests) — mocked interfaces (NSubstitute + FluentAssertions): ✅ implemented

- `CreateProductCommandHandlerTests`, `UpdateProductCommandHandlerTests` (incl. ownership mismatch → `NotFoundException`), `DeleteProductCommandHandlerTests` (incl. ownership mismatch)
- `GetProductByIdQueryHandlerTests` — cache-hit skips repository, cache-miss populates cache, unknown id throws
- `GetProductListQueryHandlerTests`, `GetVendorProductsQueryHandlerTests`
- `CreateProductCommandValidatorTests`, `UpdateProductCommandValidatorTests`
- `ValidationBehaviorTests`, `LoggingBehaviorTests`

**Product.Infrastructure.Tests** (8 tests) — Testcontainers, real containers: ✅ implemented

- `ProductRepositoryTests` (5, real SQL Server) — add + get roundtrip, unknown id returns null, active-only filtering, vendor-scoped filtering, update persists
- `RedisCacheServiceTests` (3, real Redis) — set + get roundtrip, missing key returns default, remove then get returns default

**Product.Api.Tests** (13 tests) — `WebApplicationFactory`: ✅ implemented

Fixtures (mirroring Identity's `IdentityApiFactory` pattern):

- `ProductApiFactory` — swaps `AppDbContext` for EF Core InMemory, replaces `IProductRepository` and `ICacheService` with in-memory fakes, injects deterministic JWT settings matching `JwtTokenHelper`
- `FakeProductRepository`, `FakeCacheService`, `JwtTokenHelper`

Tests:

- `ProductsControllerTests` (11) — public GET 200, unknown id 404, create as vendor 201, create as customer 403, create without auth 401, invalid body 400, update/delete as owning vendor succeeds, update as non-owning vendor 404, delete without auth 401
- `VendorsControllerTests` (2) — without auth 401, as vendor 200

**Total: 65 tests, all passing.**

---

## NuGet Packages

| Package | Project | Status |
| --- | --- | --- |
| `MediatR 12.5.0` | `Product.Application`, `Product.Api` | ✅ Added |
| `FluentValidation 11.11.0` | `Product.Application`, `Product.Application.Tests` | ✅ Added |
| `Microsoft.Extensions.Logging.Abstractions 10.0.0` | `Product.Application`, `Product.Application.Tests` | ✅ Added |
| `FluentAssertions 6.12.2` | all `.Tests` projects | ✅ Added |
| `NSubstitute 5.3.0` | `Product.Application.Tests`, `Product.Infrastructure.Tests` | ✅ Added |
| `Microsoft.EntityFrameworkCore.SqlServer 10.0.0` | `Product.Infrastructure`, `Product.Infrastructure.Tests` | ✅ Added |
| `StackExchange.Redis 2.8.24` | `Product.Infrastructure` | ✅ Added |
| `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0` | `Product.Api` | ✅ Added |
| `AspNetCore.HealthChecks.SqlServer 9.0.0`, `AspNetCore.HealthChecks.Redis 9.0.0` | `Product.Api` | ✅ Added |
| `FluentValidation.DependencyInjectionExtensions 11.11.0` | `Product.Api` | ✅ Added |
| `Serilog.AspNetCore 9.0.0` | `Product.Api` | ✅ Added |
| `Testcontainers.MsSql 4.4.0`, `Testcontainers.Redis 4.4.0` | `Product.Infrastructure.Tests` | ✅ Added |
| `Microsoft.AspNetCore.Mvc.Testing 10.0.0`, `Microsoft.EntityFrameworkCore.InMemory 10.0.0` | `Product.Api.Tests` | ✅ Added |

---

## How to Run

```bash
# 1. Start SQL Server and Redis
docker compose up -d sqlserver redis

# 2. Run the Product Service
dotnet run --project Services/Product/Product.Api
```

On first startup in Development, `ProductDb` is auto-created via `EnsureCreated()` (no seed data yet — categories must be created directly in the database until a seeding step is added).

**URLs:**

| URL | Purpose |
| --- | ------------ |
| `http://localhost:5091` (or the port `dotnet run` selects) | API base |
| `.../swagger` | Swagger UI |
| `.../health` | Health check |

Via Docker Compose, the service is reachable at `http://localhost:5002`.

**Run tests:**

```bash
dotnet test ShopFlow.sln
```

> `Product.Infrastructure.Tests` uses Testcontainers (SQL Server + Redis) — Docker must be running.

---

## TDD Order for Phase 3

```text
1. ✅ Domain entity tests          → ProductEntity, Category
2. ✅ Validator tests              → CreateProductCommandValidator, UpdateProductCommandValidator
3. ✅ Command handler tests        → Create/Update/Delete, incl. ownership checks
4. ✅ Query handler tests          → cache-hit vs cache-miss for GetProductById / GetProductList
5. ✅ Pipeline behavior tests      → ValidationBehavior, LoggingBehavior
6. ✅ Repository test              → ProductRepository (Testcontainers, real SQL Server)
7. ✅ Cache service test           → RedisCacheService (Testcontainers, real Redis)
8. ✅ API endpoint tests           → WebApplicationFactory
```
