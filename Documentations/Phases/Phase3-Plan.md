# Phase 3 — Product Service: Plan

> **Note on this document:** Phase 3 was already built and shipped before this plan was written. No pre-implementation plan file existed for it at the time — this is a retroactive reconstruction, written by working backward from [Phase3.md](Phase3.md) (the completion log), [Architecture/Product-Service.md](../Architecture/Product-Service.md), [ShopFlow-Approach.md](../ShopFlow-Approach.md), and [ShopFlow-ProjectSpec.md](../ShopFlow-ProjectSpec.md). It documents what the plan effectively was, not a historical artifact captured before the work happened.

## Context

Per `ShopFlow-Approach.md`, Product is built second — "cart and order reference product IDs," so the catalog needs to exist before those services have anything to point at. Unlike Identity, Product is the **first service to reuse an existing pattern** rather than establish one: same Clean Architecture shape (`Domain ← Application ← Infrastructure ← Api`), same CQRS/MediatR + FluentValidation pipeline, same lazy JWT-Bearer configuration trick, same `WebApplicationFactory` fixture style. The core decision for this phase is therefore less "how do we structure a service" (Identity already answered that) and more "what does Product need on top of that shape" — Redis cache-aside reads, no token issuance (only validation), and a naming collision to avoid (`ProductEntity`, not `Product`).

**Spec** (`ShopFlow-ProjectSpec.md` FR-11–FR-19):
- Anonymous browsing (list + detail), vendor-owned CRUD (create/update/delete restricted to the owning vendor), categories seeded/admin-managed, stock quantity + active/inactive status, Redis cache-aside on reads with write-invalidation

## Patterns to reuse from Identity (Phase 2) — do not reinvent

- **8-project scaffold** — same csproj shape, same `Domain ← Application ← Infrastructure ← Api` dependency direction, same matching `.Tests` project per layer
- **JWT validation** — copy the lazy `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)` registration verbatim (needed for `WebApplicationFactory` config overrides to take effect), validating against the **same shared secret/issuer/audience** Identity signs with — a token from `/api/auth/login` should work here unchanged, since Product never issues its own tokens
- **ValidationBehavior** — same open-generic `IPipelineBehavior<,>` shape as Identity's
- **WebApplicationFactory test fixture style** — `{Service}ApiFactory` swapping real infra for fakes/InMemory, `JwtTokenHelper` minting test JWTs with the same three claims Identity issues, since Product has no `/login` endpoint of its own to obtain a real one from
- **ExceptionHandlingMiddleware** — same catch-order discipline (subtype before base `DomainException`), registered before Swagger/auth

## What's new / different for Product

- **Redis cache-aside** — Identity has no caching concern at all; this is the first service with one. `ICacheService` (`GetAsync<T>`/`SetAsync<T>`/`RemoveAsync`) as the inversion point, `RedisCacheService` over `StackExchange.Redis` as the implementation, `CacheKeys` as a small static policy class (`product:{id}`, `product:catalog`) so cache key strings aren't scattered/duplicated across handlers
- **No token issuance** — `Product.Infrastructure` needs a `JwtSettings` POCO to validate tokens, but no `TokenService`, no signing package usage — Product only ever checks a signature Identity already produced
- **`LoggingBehavior` actually implemented** — Identity's is a planned stub; Product is where the pattern gets a real implementation (`ILogger<LoggingBehavior<,>>`, logs before/after `next()`), registered alongside `ValidationBehavior`
- **Naming collision to avoid**: the catalog entity must be named `ProductEntity`, not `Product` — `Product` collides with the root namespace segment every project in this service shares (`Product.Domain`, `Product.Application`, ...), which fails to compile (`CS0118`) the moment another project references the type. Worth deciding before writing the entity, not after hitting the compiler error.
- **Ownership-as-404, not 403** — `Update`/`Delete` handlers treat "product exists but belongs to a different vendor" the same as "product doesn't exist" (`NotFoundException`), the same enumeration-hiding instinct Identity applies to login failures. `VendorsController.GetVendorProducts` is the one endpoint that deliberately breaks this pattern and returns 403 instead, since the route parameter is the vendor's *own* id, not a product id — nothing to hide there.

## Step-by-step plan

### 1. Scaffold the 8 projects
Copy Identity's project shape into `Services/Product/`, dropping ASP.NET Identity/token-signing packages, adding `StackExchange.Redis` + `AspNetCore.HealthChecks.Redis`.

### 2. Domain layer (`Product.Domain`) — zero dependencies
- `ProductEntity` (not `Product` — see naming note) — private setters, `Create`/`Update`/`Deactivate` methods, shared private `Validate` (blank name, negative price, negative stock all throw `DomainException`). No `Activate()` — deactivation is one-directional.
- `Category` — `Create(name)` only; no `Update`/`Rename` — categories are immutable once created.
- No enums (a deliberate contrast with Identity's `UserRole`).
- Exceptions: `DomainException`, `NotFoundException(entityName, key)` only — no `DuplicateCategoryException` subtype; a duplicate category name collapses into the base `DomainException` → 400, unlike Identity's dedicated `DuplicateEmailException` → 409.

### 3. Application layer (`Product.Application`) — depends only on Domain
- `CacheKeys` static class — single source of truth for cache key strings
- Commands: `CreateProductCommand`, `UpdateProductCommand` (ownership check → `NotFoundException` on mismatch, not 403), `DeleteProductCommand` (soft delete via `Deactivate()` — no `DeleteAsync` anywhere), `CreateCategoryCommand` (duplicate-name check via `ICategoryRepository.ExistsByNameAsync`)
- Queries: `GetProductByIdQuery` (cache-aside, 10 min TTL), `GetProductListQuery` (cache-aside, 5 min TTL, active-only), `GetVendorProductsQuery` (no caching — vendor-scoped, includes inactive), `GetCategoryListQuery` (no caching)
- Interfaces: `IProductRepository`, `ICategoryRepository`, `ICacheService`
- Validators: `CreateProductCommandValidator`, `UpdateProductCommandValidator` (does **not** validate `VendorId` — ownership is a handler concern, not a validator concern), `CreateCategoryCommandValidator`
- Behaviors: `ValidationBehavior` (copied from Identity), `LoggingBehavior` (new, fully implemented here)

### 4. Infrastructure layer (`Product.Infrastructure`) — depends on Domain + Application
- `AppDbContext` — `Products`/`Categories` DbSets; FK `OnDelete(DeleteBehavior.Restrict)` (not Cascade); note categories have no DB-level unique index on `Name` — uniqueness is only enforced by the Application-layer existence check, a check-then-insert race the schema doesn't close
- `ProductRepository`, `CategoryRepository`
- `RedisCacheService : ICacheService` — `IConnectionMultiplexer` computed fresh per call (thread-safe by design, no field caching), JSON via `System.Text.Json`
- `JwtSettings` — `Secret`/`Issuer`/`Audience` only, no `ExpiryMinutes` (Product never mints a token)

### 5. API layer (`Product.Api`)
- Endpoints: `GET /api/products` + `/{id}` (anonymous), `POST`/`PUT`/`DELETE /api/products` (`[RequireVendor]`, owner-only via `NotFoundException`), `GET`/`POST /api/categories` (`GET` anonymous, `POST` `[RequireAdmin]`), `GET /api/vendors/{id}/products` (`[RequireVendor]` + explicit id-vs-claim check → 403 on mismatch)
- `VendorId` always read from the `userId` JWT claim in the controller, never the request body
- `ExceptionHandlingMiddleware` — same shape as Identity's, minus the 401/409 mappings Product has no exception types for
- `Program.cs` — `IConnectionMultiplexer` as a Singleton; `IProductRepository`/`ICategoryRepository`/`ICacheService` as Scoped; MediatR + both pipeline behaviors; only two authorization policies (`RequireVendor`, `RequireAdmin` — no `RequireVerifiedEmail`, Product has no email-verification concept); `/health` against **both** SQL Server and Redis; dev-startup block does `EnsureCreated()` **and now** seeds `CategorySeed` from config (closed gap — see Phase3.md) so `POST /api/products` doesn't need a category inserted by hand first

### 6. Tests, per layer (TDD)
- `Product.Domain.Tests` — `ProductTests`, `CategoryTests` against `Create`/`Update`/`Deactivate`/`Validate` directly
- `Product.Application.Tests` — NSubstitute-mocked `IProductRepository`/`ICategoryRepository`/`ICacheService`; handler cache-hit vs cache-miss coverage for the two cached queries; validator + behavior tests
- `Product.Infrastructure.Tests` — `ProductRepositoryTests` (Testcontainers, real SQL Server), `RedisCacheServiceTests` (Testcontainers, real Redis) — the first service to spin up two containers for its infra tests
- `Product.Api.Tests` — `ProductApiFactory` (mirrors `IdentityApiFactory`), `FakeProductRepository`/`FakeCategoryRepository`/`FakeCacheService` (dictionary-backed, `FakeCacheService` deliberately ignores TTL), `JwtTokenHelper` (same three claims as Identity, since Product has no login endpoint of its own)

## Verification

- `dotnet test ShopFlow.sln` — all Product test projects green
- Manual smoke test via Docker (`sqlserver`, `redis`, `identity-service`, `product-service`): register a vendor through Identity, promote via admin endpoint, re-issue a JWT with the `Vendor` role, create a product, confirm the public list/get-by-id endpoints return it, confirm Redis actually holds the `product:{id}` JSON cache entry, update as the owning vendor and confirm cache invalidation, list via the vendor-scoped endpoint, soft-delete and confirm it disappears from the public catalog
- Confirm role/ownership enforcement on real HTTP responses: 401 without a token, 403 for a non-vendor role or vendor-id mismatch, 404 for a non-owning vendor's product

## Critical files

- `Documentations/ShopFlow-ProjectSpec.md` (FR-11–FR-19)
- `Documentations/Architecture/Product-Service.md` (full as-built architecture, including every deliberate deviation from Identity's pattern)
- `Services/Identity/Identity.Api/Program.cs` (the template this phase's `Program.cs` is built from)
- `Services/Product/Product.Infrastructure/Caching/RedisCacheService.cs` (the cache-aside pattern Cart's Phase 4 `RedisCartRepository` later adapts)
