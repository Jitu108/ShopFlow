# Product Service — Full Architecture Documentation

## Abstract

The Product Service is made up of eight .NET projects — four production projects and four matching test projects — that together implement the product catalog: categories, vendor-owned product listings, and public browsing. It follows the same Clean Architecture shape as the Identity Service (see [Identity-Service.md](./Identity-Service.md)) and trusts Identity's JWTs rather than issuing any of its own — Product.Api has no `/auth` endpoints at all.

**What each project is, and why it's relevant:**

| Project | What it is | Why it exists |
| --- | --- | --- |
| `Product.Domain` | The vocabulary of the service — `ProductEntity`, `Category`, and the exceptions that name what can go wrong (`DomainException`, `NotFoundException`) | Every other project talks about products and categories in these exact terms. |
| `Product.Application` | The use cases — create/update/delete a product, create a category, list/get products, list categories — each as a MediatR command/query + handler, plus validators, cache-key policy, and the interfaces they need | Where the catalog workflow actually lives, including *when* the cache gets invalidated and *who* is allowed to touch a product. |
| `Product.Infrastructure` | The concrete technology: EF Core against SQL Server, Redis via StackExchange.Redis, JWT *validation* settings | Makes the use cases work against real storage and a real cache, behind the interfaces Application declared. Unlike Identity.Infrastructure, this layer signs nothing — it only holds the settings needed to validate a token Identity already signed. |
| `Product.Api` | The ASP.NET Core host — controllers, exception-to-HTTP-status middleware, and the `Program.cs` composition root | The only project any client or other service talks to. |

**How they're related, and why:**

Same directed dependency chain as Identity, and for the same reason:

```text
Product.Domain            entities, exceptions — zero dependencies
       ↑
Product.Application       use cases (CQRS) — depends only on Domain
       ↑
Product.Infrastructure    EF Core, Redis, JWT settings — depends on Domain + Application
       ↑
Product.Api               controllers, middleware, DI composition root — depends on Application + Infrastructure
```

`Product.Application` declares `IProductRepository`, `ICategoryRepository`, `ICacheService` without implementing them; `Product.Infrastructure` implements them; `Product.Api` wires the choice together in `Program.cs`. That inversion is what lets `Product.Application.Tests` mock the cache and repositories with NSubstitute, and what let `Product.Api.Tests` swap Redis and SQL Server for in-memory fakes without touching a single handler.

The four test projects mirror this chain one-to-one, exactly as in Identity — see [§5](#5-test-projects).

The sections below walk each of the eight projects in full, then trace one request (`POST /api/products`) end-to-end through all four production layers in [§6](#6-request-flow--end-to-end-example).

---

## Overview

The Product Service owns the catalog: categories (admin-managed) and products (vendor-owned, publicly browsable). It never authenticates anyone itself — it validates JWTs issued by the Identity Service against the **same shared secret/issuer/audience**, so a token from Identity's `/api/auth/login` works here unchanged. Per [ShopFlow-Approach.md](../ShopFlow-Approach.md), it's the second service built, layered on top of the trust Identity establishes.

It follows **Clean Architecture** with **CQRS via MediatR**, identical in shape to Identity:

```text
Services/Product/
├── Product.Domain/               Product.Domain.Tests/
├── Product.Application/          Product.Application.Tests/
├── Product.Infrastructure/        Product.Infrastructure.Tests/
└── Product.Api/                  Product.Api.Tests/
```

> **Naming note** (from [Phase3.md](../Phases/Phase3.md)): the catalog entity is named `ProductEntity`, not `Product` — `Product` collides with the root namespace segment shared by every project in this service (`Product.Domain`, `Product.Application`, ...), which fails to compile (`CS0118`) the moment another project references the type.

---

## 1. Product.Domain — Entities, Exceptions

**[Product.Domain.csproj](../../Services/Product/Product.Domain/Product.Domain.csproj)** — plain class library, **no NuGet packages, no project references**, same isolation guarantee as `Identity.Domain`.

Folders: `Entities/`, `Exceptions/` only. **Unlike Identity, there is no `Enums/` folder at all** — Product.Domain defines no enums; a category is a plain named entity, not a status/type enum.

### Entities

**[ProductEntity](../../Services/Product/Product.Domain/Entities/ProductEntity.cs)** — private setters, private parameterless constructor, mutation only through named methods:

| Member | Purpose |
| --- | --- |
| `Id, VendorId, Name, Description, Price, StockQuantity, IsActive, CreatedAt, UpdatedAt, CategoryId` | State, all privately settable |
| `Category` | Navigation to the owning `Category` |
| `Create(vendorId, name, description, price, stockQuantity, categoryId)` (static factory) | Validates via `Validate(...)`, then sets `Id = Guid.NewGuid()`, `IsActive = true`, `CreatedAt = UpdatedAt = DateTime.UtcNow`. Does **not** validate `categoryId != Guid.Empty` — that check lives in the Application-layer validator, not the entity |
| `Update(name, description, price, stockQuantity, categoryId)` | Re-runs `Validate`, overwrites the same fields, bumps `UpdatedAt`. Deliberately cannot touch `VendorId`, `Id`, `IsActive`, or `CreatedAt` — ownership and activation state aren't mutable through this path |
| `Deactivate()` | Soft delete: `IsActive = false`, bumps `UpdatedAt`. **There is no `Activate()`** — deactivation is one-directional in the domain model |
| `Validate(name, price, stockQuantity)` (private, shared by `Create`/`Update`) | Throws `DomainException` on blank name, negative price, or negative stock quantity |

**[Category](../../Services/Product/Product.Domain/Entities/Category.cs)** — `Id`, `Name`, `Products` (inverse navigation collection). `Create(name)` throws `DomainException("Category name is required.")` on a blank name; otherwise sets `Id = Guid.NewGuid()`, `Name = name` **without trimming**. There is no `Update`/`Rename` method anywhere in the codebase — categories are immutable once created.

### Enums

None — a deliberate contrast with Identity's `UserRole`.

### Exceptions

Only two types total (versus Identity's four):

| Exception | Thrown when | Mapped to |
| --- | --- | --- |
| `DomainException` (base, also thrown directly) | Invalid product name/price/stock (`ProductEntity.Validate`); blank category name (`Category.Create`); duplicate category name (`CreateCategoryCommandHandler`, Application layer) | 400 |
| `NotFoundException(entityName, key)` | Product not found by ID; **also thrown when a product exists but belongs to a different vendor** — `Update`/`Delete` handlers deliberately reuse "not found" rather than a distinct "forbidden" error, hiding the existence of other vendors' products the same way Identity's login handler collapses "unknown email" and "wrong password" into one `InvalidCredentialsException` | 404 |

**Notable asymmetry vs. Identity**: there is no `DuplicateCategoryException` subtype — the duplicate-category case throws the base `DomainException` directly, so it collapses into the same 400 response as any other invalid input, whereas Identity gives duplicate email its own type and its own 409.

---

## 2. Product.Application — Use Cases (CQRS)

**[Product.Application.csproj](../../Services/Product/Product.Application/Product.Application.csproj)** references only `Product.Domain`, plus `MediatR`, `FluentValidation`, and — unlike Identity.Application — `Microsoft.Extensions.Logging.Abstractions`, needed because this layer implements a fully working `LoggingBehavior` (Identity's is still a planned stub).

### Cache key policy

**[CacheKeys.cs](../../Services/Product/Product.Application/CacheKeys.cs)** — the one piece of this layer with no Identity equivalent, since Identity's Application layer has no caching concern:

```csharp
public const string Catalog = "product:catalog";
public static string Product(Guid id) => $"product:{id}";
```

### Commands + Handlers

**[Commands/](../../Services/Product/Product.Application/Commands/)**

| Command | Returns | Handler responsibility |
| --- | --- | --- |
| `CreateProductCommand(VendorId, Name, Description, Price, StockQuantity, CategoryId)` | `ProductDto` | `ProductEntity.Create(...)` → `IProductRepository.AddAsync` → invalidates `CacheKeys.Catalog` only (no per-product key yet, since it never existed in cache) |
| `UpdateProductCommand(Id, VendorId, Name, Description, Price, StockQuantity, CategoryId)` | `ProductDto` | Loads by `Id` (`NotFoundException` if missing); **ownership check** — `product.VendorId != command.VendorId` also throws `NotFoundException`, not a 403; calls `product.Update(...)`; persists; invalidates **both** `CacheKeys.Product(id)` and `CacheKeys.Catalog` |
| `DeleteProductCommand(Id, VendorId)` | *(none — `IRequest`)* | Same lookup + ownership check as Update; calls `product.Deactivate()` then `UpdateAsync` — **there is no `DeleteAsync`**, deletion is always the soft-delete path; invalidates the same two cache keys |
| `CreateCategoryCommand(Name)` | `CategoryDto` | Checks `ICategoryRepository.ExistsByNameAsync`; throws base `DomainException` (no subtype) if a duplicate; otherwise `Category.Create` → `AddAsync`. **No cache interaction at all** — categories aren't cached |

### Queries + Handlers

**[Queries/](../../Services/Product/Product.Application/Queries/)**

| Query | Returns | Handler responsibility |
| --- | --- | --- |
| `GetProductByIdQuery(Id)` | `ProductDto` | Cache-aside: `product:{id}` hit → return; miss → `GetByIdAsync` (`NotFoundException` if null) → map → cache for **10 minutes**. Note `GetByIdAsync` doesn't filter `IsActive`, so a deactivated product is still directly fetchable by ID |
| `GetProductListQuery()` (no params) | `IReadOnlyList<ProductDto>` | Cache-aside on `product:catalog`, **5-minute** TTL; miss → `GetAllActiveAsync` (active only) |
| `GetVendorProductsQuery(VendorId)` | `IReadOnlyList<ProductDto>` | **No caching** — direct `GetByVendorIdAsync`; the only product handler with no `ICacheService` dependency. Returns *all* of a vendor's products including deactivated ones — a deliberate difference from the public catalog query |
| `GetCategoryListQuery()` (no params) | `IReadOnlyList<CategoryDto>` | No caching. `GetAllAsync` (repo orders alphabetically) → map |

### Validators (FluentValidation)

**[Validators/](../../Services/Product/Product.Application/Validators/)**

- `CreateProductCommandValidator` — `VendorId` not empty; `Name` not empty, ≤200 chars; `Price` ≥ 0; `StockQuantity` ≥ 0; `CategoryId` not empty
- `UpdateProductCommandValidator` — same shape but on `Id` instead of `VendorId`; **does not validate `VendorId`** on update — ownership is enforced in the handler, not the validator
- `CreateCategoryCommandValidator` — `Name` not empty, ≤100 chars
- No validator exists for `DeleteProductCommand` or any query — `ValidationBehavior` short-circuits to `next()` when no validators are registered for a request type

### Pipeline Behaviors

**[Behaviors/](../../Services/Product/Product.Application/Behaviors/)**

- `ValidationBehavior<TRequest,TResponse>` — same shape as Identity's: runs every matching `IValidator<TRequest>`, aggregates failures, throws `FluentValidation.ValidationException` if any exist.
- `LoggingBehavior<TRequest,TResponse>` — **fully implemented here**, where Identity's equivalent is still only planned. Logs `"Handling {RequestName}"` before `next()` and `"Handled {RequestName}"` after, via `ILogger<LoggingBehavior<TRequest,TResponse>>`. Purely observational.

Both are registered as open-generic `IPipelineBehavior<,>` in `Program.cs`, `ValidationBehavior` first, then `LoggingBehavior`.

### DTOs

**[DTOs/](../../Services/Product/Product.Application/DTOs/)** — `ProductDto(Id, VendorId, Name, Description, Price, StockQuantity, IsActive, CategoryId, CreatedAt, UpdatedAt)` and `CategoryDto(Id, Name)` (deliberately omits the `Products` navigation collection). Mapping is via plain extension methods (`ProductMappingExtensions.ToDto()`, `CategoryMappingExtensions.ToDto()`) — no mapping library.

### Interfaces (the inversion point)

**[Interfaces/](../../Services/Product/Product.Application/Interfaces/)**

```csharp
public interface IProductRepository
{
    Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProductEntity>> GetAllActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductEntity>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct);
    Task AddAsync(ProductEntity product, CancellationToken ct);
    Task UpdateAsync(ProductEntity product, CancellationToken ct);
}
```

No `DeleteAsync` (soft delete only) and no `ExistsByIdAsync` (existence is always checked via a null `GetByIdAsync`).

```csharp
public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);
    Task AddAsync(Category category, CancellationToken ct);
}
```

```csharp
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}
```

Generic and technology-agnostic — the inversion point that lets `RedisCacheService` become `FakeCacheService` in `Product.Api.Tests` without touching a handler.

---

## 3. Product.Infrastructure — Persistence, Caching, JWT Settings

**[Product.Infrastructure.csproj](../../Services/Product/Product.Infrastructure/Product.Infrastructure.csproj)** references Domain + Application, plus `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Extensions.Options`, `StackExchange.Redis`. Notably **no JWT-signing package** — Product never issues tokens, only validates them, so there's no `TokenService` here at all, just a settings POCO (below).

### Persistence

**[AppDbContext](../../Services/Product/Product.Infrastructure/Persistence/AppDbContext.cs)** — `DbSet<ProductEntity> Products`, `DbSet<Category> Categories`. `OnModelCreating`:

- `Category`: app-generated `Id` (`ValueGeneratedNever()`), `Name` required, ≤100 chars. **No unique index on `Name`** — uniqueness is enforced only in the Application handler's `ExistsByNameAsync` check before insert, which is a check-then-insert race the schema itself doesn't close (contrast with Identity's DB-level unique index on `Email`).
- `ProductEntity`: table `Products`, app-generated `Id`, `VendorId` required with a non-unique `HasIndex` (supports the vendor-listing query), `Name` required ≤200 chars, `Description` ≤2000 chars but **not required** (can be empty), `Price` as `decimal(18,2)`, `StockQuantity`/`IsActive`/`CreatedAt`/`UpdatedAt` all required. FK to `Category` is `OnDelete(DeleteBehavior.Restrict)` — **Restrict, not Cascade** (Identity cascades `RefreshTokens`) — though moot in practice since no category-delete use case exists.

**[ProductRepository : IProductRepository](../../Services/Product/Product.Infrastructure/Persistence/Repositories/ProductRepository.cs)**:
- `GetByIdAsync` — `SingleOrDefaultAsync(x => x.Id == id)`.
- `GetAllActiveAsync` — `Where(x => x.IsActive)`, no ordering applied.
- `GetByVendorIdAsync` — `Where(x => x.VendorId == vendorId)`, **does not filter `IsActive`**, no ordering.
- `AddAsync` / `UpdateAsync` — `Add`/`Update` + `SaveChangesAsync` each, one unit-of-work per call.

**[CategoryRepository : ICategoryRepository](../../Services/Product/Product.Infrastructure/Persistence/Repositories/CategoryRepository.cs)**:
- `GetAllAsync` — `OrderBy(x => x.Name)`.
- `ExistsByNameAsync` — compares against `name.Trim()`, but `Category.Create` never trims the stored name, so a category created with incidental whitespace can dodge a later trimmed comparison. Comparison case-sensitivity also depends on DB collation (SQL Server's default collation is typically case-insensitive; EF Core InMemory is case-sensitive) — a real test-vs-prod divergence, which is why `FakeCategoryRepository` in `Product.Api.Tests` explicitly forces `StringComparison.OrdinalIgnoreCase` to compensate.
- `AddAsync` — `Add` + `SaveChangesAsync`.

### Caching

**[RedisCacheService : ICacheService](../../Services/Product/Product.Infrastructure/Caching/RedisCacheService.cs)** over `StackExchange.Redis`:
- `Database` is computed fresh (`_connectionMultiplexer.GetDatabase()`) on every access rather than cached as a field — cheap and thread-safe by StackExchange.Redis design.
- `GetAsync<T>` — `StringGetAsync`; `IsNullOrEmpty` → `default`; otherwise `JsonSerializer.Deserialize<T>` with default `System.Text.Json` options.
- `SetAsync<T>` — `JsonSerializer.Serialize` → `StringSetAsync(key, value, expiry)`, passing the caller's `TimeSpan` straight through to Redis's native `EXPIRE`.
- `RemoveAsync` — `KeyDeleteAsync`, no-op if the key is already absent.
- No key prefixing/namespacing inside the service itself — callers own the full key via `CacheKeys`.

**[JwtSettings](../../Services/Product/Product.Infrastructure/Settings/JwtSettings.cs)** — `Secret`, `Issuer`, `Audience` only. **No `ExpiryMinutes`** — Product only validates tokens, it never mints one, so there's no expiry to configure here.

---

## 4. Product.Api — Controllers, Middleware, Composition Root

**[Product.API.csproj](../../Services/Product/Product.Api/Product.API.csproj)** (`Sdk="Microsoft.NET.Sdk.Web"`) references Application + Infrastructure, plus `Serilog.AspNetCore`, `Swashbuckle.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `AspNetCore.HealthChecks.SqlServer`, `AspNetCore.HealthChecks.Redis`, `FluentValidation.DependencyInjectionExtensions`, `MediatR`, `Microsoft.AspNetCore.Authentication.JwtBearer`.

`Program.cs` calls `UseSerilog(...)` (console sink, reading the `Serilog` section of `appsettings.json`) plus `UseSerilogRequestLogging()`, so the referenced `Serilog.AspNetCore` package is actually wired up rather than sitting unused.

### Endpoints

```text
GET    /api/products                                    → 200 OK   (anonymous)
GET    /api/products/{id:guid}                           → 200 OK / 404
POST   /api/products                    [RequireVendor]  → 201 Created
PUT    /api/products/{id:guid}          [RequireVendor]  → 200 OK / 404
DELETE /api/products/{id:guid}          [RequireVendor]  → 204 No Content / 404
GET    /api/categories                                   → 200 OK   (anonymous)
POST   /api/categories                  [RequireAdmin]   → 201 Created
GET    /api/vendors/{id:guid}/products  [RequireVendor]  → 200 OK
GET    /health                                            → 200 OK — health status
```

**[ProductsController](../../Services/Product/Product.Api/Controllers/ProductsController.cs)** — every write action derives the caller's vendor identity from a private `VendorId => Guid.Parse(User.FindFirstValue("userId")!)`, **never** from the request body, so a vendor can never create or touch a product "as" another vendor. `Create`/`Update` build `CreateProductRequest`/`UpdateProductRequest` local records (`Name, Description, Price, StockQuantity, CategoryId` — no `VendorId`/`Id` field, confirming those always come from claims/route) and forward to MediatR; `Update` returns `200 Ok`, not a distinct "updated" status; `Delete` returns `204 NoContent`.

**[CategoriesController](../../Services/Product/Product.Api/Controllers/CategoriesController.cs)** — `GetAll` anonymous; `Create` gated by `RequireAdmin`.

**[VendorsController](../../Services/Product/Product.Api/Controllers/VendorsController.cs)** — `GetVendorProducts(id)` is gated by `RequireVendor` **and** now checks that `id` matches the caller's own `userId` claim, returning `403 Forbidden` on mismatch (`Forbid()`) before the query is even sent — the same owner-only pattern `ProductsController.Update`/`Delete` follow. Fixes a prior gap where any authenticated Vendor could list *any other* vendor's products by ID.

### Middleware

**[ExceptionHandlingMiddleware](../../Services/Product/Product.Api/Middleware/ExceptionHandlingMiddleware.cs)**, registered before Swagger/auth so it wraps the whole downstream pipeline:

| Exception caught | Status | Body |
| --- | --- | --- |
| `FluentValidation.ValidationException` | 400 | `{ errors: [{ propertyName, errorMessage }] }` |
| `NotFoundException` | 404 | `{ message }` |
| `DomainException` (base, catch-all) | 400 | `{ message }` |
| Any other `Exception` | 500 | Generic message; full exception logged |

Same catch-order discipline as Identity (subtype before base). Unlike Identity, there's **no 401/409 mapping** here at all — Product.Domain has no exception type that maps to either status, since duplicate-category and invalid-input errors both collapse into the generic `DomainException → 400` branch.

### Composition root — Program.cs

**[Program.cs](../../Services/Product/Product.Api/Program.cs)**:

- Binds `JwtSettings` from configuration.
- Registers `AppDbContext` against SQL Server (`ConnectionStrings:Default`).
- Registers `IConnectionMultiplexer` as a **Singleton** (`ConnectionMultiplexer.Connect(...)`, falling back to `"localhost:6379"` if config is missing) — a single shared Redis connection for the app's lifetime.
- Registers `IProductRepository`, `ICategoryRepository`, `ICacheService` as **Scoped** — including `RedisCacheService`, even though it just wraps the Singleton multiplexer.
- Registers MediatR scanning `Product.Application`.
- Registers FluentValidation scanning the same assembly; adds `ValidationBehavior<,>` then `LoggingBehavior<,>` as open-generic `IPipelineBehavior<,>`.
- Configures JWT Bearer auth the same lazy way as Identity (`AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)`) specifically so `WebApplicationFactory` config overrides in tests take effect; `ClockSkew = TimeSpan.Zero` (no grace period on token expiry).
- Registers exactly two authorization policies — `RequireVendor`, `RequireAdmin` (role checks only). **No `RequireVerifiedEmail`** — Product has no email-verification concept.
- Registers `/health` against **both** SQL Server and Redis (Identity checks SQL Server only, since it has no Redis dependency).
- **Dev-only startup block**: `db.Database.EnsureCreated()`, **then seeds categories** from the `CategorySeed` array in configuration (`appsettings.Development.json`: `Electronics`, `Clothing`, `Home & Kitchen`, `Books`, `Toys & Games`) — each name added via `Category.Create(name)` only if a category with that exact name doesn't already exist, then one `SaveChanges()`. Comparable in spirit to Identity's dev-seed block (which seeds an admin account instead), just seeding catalog data rather than a user.
- `public partial class Program` at the bottom, for `WebApplicationFactory<Program>`.

---

## 5. Test Projects

| Test project | Targets | Style | Notable packages |
| --- | --- | --- | --- |
| **Product.Domain.Tests** | `Product.Domain` | Pure unit, no mocks — `ProductTests`, `CategoryTests` exercise `Create`/`Update`/`Deactivate`/`Validate` directly | xunit, FluentAssertions |
| **Product.Application.Tests** | `Product.Application` | Handlers/validators/behaviors tested against **NSubstitute** mocks of `IProductRepository`/`ICategoryRepository`/`ICacheService` — no DB, no HTTP | + NSubstitute, FluentValidation, Microsoft.Extensions.Logging.Abstractions |
| **Product.Infrastructure.Tests** | `Product.Infrastructure` | `ProductRepositoryTests` against a **real SQL Server** via Testcontainers *and* `RedisCacheServiceTests` against a **real Redis** via Testcontainers — a step beyond Identity.Infrastructure.Tests, which only spins up SQL Server (Identity has no cache to test) | + NSubstitute, **Testcontainers.MsSql**, **Testcontainers.Redis** |
| **Product.Api.Tests** | Full stack via `Product.Api` | End-to-end HTTP tests through `WebApplicationFactory`, with `AppDbContext` swapped for **EF Core InMemory** and `IProductRepository`/`ICategoryRepository`/`ICacheService` swapped for in-memory fakes | + Microsoft.AspNetCore.Mvc.Testing, EF Core InMemory |

**Product.Api.Tests fixtures** ([Fixtures/](../../Services/Product/Product.Api.Tests/Fixtures/)):
- `ProductApiFactory` — the `WebApplicationFactory<Program>` subclass; overrides `JwtSettings` config to match `JwtTokenHelper`, swaps `AppDbContext` for InMemory (largely vestigial, since the repositories below never touch it), and swaps `IProductRepository` → `FakeProductRepository`, `ICategoryRepository` → `FakeCategoryRepository`, `ICacheService` → `FakeCacheService`, each exposed as a public property so tests can seed them before issuing requests.
- `FakeProductRepository` / `FakeCategoryRepository` — dictionary-backed fakes reproducing the real repositories' filtering (`IsActive`, `VendorId`) and ordering (categories by name) in memory, plus a `Seed()` helper.
- `FakeCacheService` — dictionary-backed, type-checked cast on read, **ignores TTL entirely** (no expiry simulation).
- `JwtTokenHelper` — mints real signed JWTs with the three claims (`userId`, `ClaimTypes.Email`, `ClaimTypes.Role`) `ProductsController.VendorId` and the `RequireVendor`/`RequireAdmin` policies expect, since Product.Api has no login endpoint of its own to obtain one from.

This mirrors Identity's test strategy exactly: cheap and deterministic near Domain, real infrastructure (now including a real cache, not just a real database) at the edges.

---

## 5.5 Project Dependency Wiring

```text
┌─────────────────────────────────────────────────────────────────┐
│                        Product Service                           │
│                                                                  │
│   Production Code                    Test Projects              │
│   ───────────────                    ─────────────              │
│                                                                  │
│   ┌──────────────┐                  ┌───────────────────────┐   │
│   │ Product.API  │                  │  Product.API.Tests    │   │
│   └──────┬───┬───┘                  └───────────┬───────────┘   │
│          │   │ refs                             │ refs          │
│          │   └──────────────────┐              ▼               │
│          │ refs                 │    ┌──────────────────────┐   │
│          ▼                      │    │ Product.Infra.Tests   │   │
│   ┌──────────────────┐          │    └──────────┬───────────┘   │
│   │  Product.Infra   │◄─────────┘              │ refs          │
│   └────┬─────────┬───┘                         ▼               │
│        │ refs    │ refs          ┌──────────────────────────┐   │
│        │         │               │  Product.App.Tests       │   │
│        │         │               └──────────┬───────────────┘   │
│        │         │                          │ refs              │
│        │         ▼                          ▼                   │
│        │  ┌──────────────────┐   ┌──────────────────────────┐   │
│        │  │Product.Applicat. │◄──│  Product.Domain.Tests    │   │
│        │  └────────┬─────────┘   └──────────────────────────┘   │
│        │ refs      │ refs                                        │
│        │           ▼                                            │
│        └──►┌──────────────────┐                                 │
│            │  Product.Domain  │                                 │
│            └──────────────────┘                                 │
│                  (no deps)                                       │
└─────────────────────────────────────────────────────────────────┘
```

| Project | References |
| --- | --- |
| `Product.Domain` | — |
| `Product.Application` | `Product.Domain` |
| `Product.Infrastructure` | `Product.Domain` + `Product.Application` |
| `Product.API` | `Product.Application` + `Product.Infrastructure` |
| `Product.Domain.Tests` | `Product.Domain` |
| `Product.Application.Tests` | `Product.Application` |
| `Product.Infrastructure.Tests` | `Product.Infrastructure` |
| `Product.API.Tests` | `Product.API` |

---

## 6. Request Flow — End to End Example

`POST /api/products` (as an authenticated Vendor):

1. **Api**: `ProductsController.Create` reads `VendorId` from the `userId` JWT claim (never from the body), builds `CreateProductCommand` from the request body + that `VendorId`, calls `IMediator.Send`.
2. **Application (pipeline)**: `ValidationBehavior` runs `CreateProductCommandValidator` — blank name / negative price or stock / missing category → `ValidationException` → **Api**'s `ExceptionHandlingMiddleware` → HTTP 400. `LoggingBehavior` logs "Handling `CreateProductCommand`" around the call.
3. **Application (handler)**: `CreateProductCommandHandler`:
   - `ProductEntity.Create(...)` (**Domain** factory — enforces entity invariants, can itself throw `DomainException` if something bypasses the validator).
   - `IProductRepository.AddAsync` → **Infrastructure**'s `ProductRepository` persists via `AppDbContext`.
   - `ICacheService.RemoveAsync(CacheKeys.Catalog)` → **Infrastructure**'s `RedisCacheService` deletes the stale catalog-list cache entry so the next `GET /api/products` repopulates it from SQL Server.
   - Returns `product.ToDto()` (**Application** mapping extension).
4. **Api**: controller returns HTTP 201 with the `ProductDto` body.

Every arrow that crosses a layer boundary crosses through an interface owned by the *inner* layer, exactly as in Identity's request-flow trace — the concrete cache/database technology is always supplied from further out.

---

## 7. Configuration & Running

Connection strings, Redis address, and JWT settings all live in [appsettings.Development.json](../../Services/Product/Product.Api/appsettings.Development.json) — note the base `appsettings.json` has no connection strings or JWT secret at all, so the service only really runs in `Development` today (no production config file exists yet). Full run instructions are in [Documentations/RUNNING.md](../RUNNING.md); summary:

```bash
docker compose up -d sqlserver redis
dotnet run --project Services/Product/Product.Api
```

- API: `http://localhost:5015` in Identity's case, `http://localhost:5016` here; Swagger: `/swagger`; health: `/health` (SQL Server + Redis, both checked)
- Dev environments seed five categories automatically (`Electronics`, `Clothing`, `Home & Kitchen`, `Books`, `Toys & Games`) — a product can reference one of these immediately, with no manual `POST /api/categories` call needed first
- `dotnet test ShopFlow.sln` — note `Product.Infrastructure.Tests` needs Docker running (Testcontainers spins up **both** SQL Server and Redis, one container each)

---

## Summary — what each layer answers

| Layer | Answers |
| --- | --- |
| `Product.Domain` | What is a valid product/category state, and what operations can change it? |
| `Product.Application` | What does the system do for each catalog use case — and when does the cache get invalidated or bypassed? |
| `Product.Infrastructure` | How is that fulfilled — SQL Server, Redis, JWT validation settings? |
| `Product.Api` | How is it exposed over HTTP, how do failures become status codes, and how is everything wired together at startup? |
