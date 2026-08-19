# Identity Service — Full Architecture Documentation

## Abstract

The Identity Service is made up of eight .NET projects — four production projects and four matching test projects — that together implement one thing: account registration, login, and role-based access control for ShopFlow. No single project is useful on its own; each exists because it isolates one concern, and the four production projects are meant to be read as one pipeline rather than four separate services.

**What each project is, and why it's relevant:**

| Project | What it is | Why it exists |
| --- | --- | --- |
| `Identity.Domain` | The vocabulary of the service — `ApplicationUser`, `RefreshToken`, `UserRole`, and the exceptions that name what can go wrong (`DuplicateEmailException`, `InvalidCredentialsException`, ...) | Every other project talks about users and tokens in these exact terms. Without it, "what is a user" would be redefined ad hoc in every layer. |
| `Identity.Application` | The use cases — register, login, refresh, logout, assign-role, get-current-user, search-users — each as a MediatR command/query + handler, plus the validators and interfaces they need | This is where the business workflow actually lives: *what steps happen, in what order, under what rules*. It's the reason the other three projects exist — Domain gives it a vocabulary, Infrastructure and Api give it a way to run. |
| `Identity.Infrastructure` | The concrete technology: EF Core against SQL Server, ASP.NET Core's password hasher, JWT signing | Makes the use cases actually work against real storage and real cryptography, behind the interfaces Application declared. |
| `Identity.Api` | The ASP.NET Core host — controllers, exception-to-HTTP-status middleware, and the `Program.cs` composition root that wires all of the above together and exposes it on the network | The only project any other ShopFlow service or client actually talks to. Everything below it is invisible from outside the process. |

**How they're related, and why:**

They form a single directed dependency chain — `Domain ← Application ← Infrastructure ← Api` — and that direction is the entire point. Each arrow means "depends on," and it only ever points toward Domain:

```text
Identity.Domain            entities, enums, exceptions — zero dependencies
       ↑
Identity.Application       use cases (CQRS) — depends only on Domain
       ↑
Identity.Infrastructure    EF Core, ASP.NET Identity hashing, JWT — depends on Domain + Application
       ↑
Identity.Api               controllers, middleware, DI composition root — depends on Application + Infrastructure
```

They're related this way — rather than, say, Application depending on Infrastructure directly — because `Identity.Application` declares interfaces (`IUserRepository`, `ITokenService`, `IRefreshTokenRepository`) that it needs but does not implement. `Identity.Infrastructure` is the project that implements them; `Identity.Api` is the project that tells the DI container which implementation to plug in. That inversion is what lets business logic (Application) be tested and reasoned about without a database or a web server, and what would let SQL Server or the JWT library be replaced without touching a single use case.

The four test projects mirror this chain one-to-one (`Identity.Domain.Tests` → ... → `Identity.Api.Tests`), so the relevance of each is fixed by which production project it targets and how far from the "pure business rule" center that project sits — pure unit tests near Domain, real SQL Server/HTTP integration tests near Api. See [§5](#5-test-projects) for the detail.

The sections below walk each of these eight projects in full, then trace one request (`register`) end-to-end through all four production layers in [§6](#6-request-flow--end-to-end-example).

---

## Overview

The Identity Service owns authentication, authorization, and user-account management for the whole ShopFlow platform. It issues the JWTs every other service trusts, so — per [ShopFlow-Approach.md](../ShopFlow-Approach.md) — it's built first: everything downstream depends on it.

It follows **Clean Architecture** (four concentric layers, dependencies point inward only) with **CQRS via MediatR** inside the Application layer:

```text
Identity.Domain            entities, enums, exceptions — zero dependencies
       ↑
Identity.Application       use cases (CQRS) — depends only on Domain
       ↑
Identity.Infrastructure    EF Core, ASP.NET Identity hashing, JWT — depends on Domain + Application
       ↑
Identity.Api               controllers, middleware, DI composition root — depends on Application + Infrastructure
```

Each layer has a matching test project (`Identity.Domain.Tests`, `Identity.Application.Tests`, `Identity.Infrastructure.Tests`, `Identity.Api.Tests`), so the test strategy mirrors the architecture: pure unit tests at the center, integration tests at the edges.

```text
Services/Identity/
├── Identity.Domain/              Identity.Domain.Tests/
├── Identity.Application/         Identity.Application.Tests/
├── Identity.Infrastructure/       Identity.Infrastructure.Tests/
└── Identity.Api/                 Identity.Api.Tests/
```

---

## 1. Identity.Domain — Entities, Enums, Exceptions

**[Identity.Domain.csproj](../../Services/Identity/Identity.Domain/Identity.Domain.csproj)** — plain class library, **no NuGet packages, no project references**. This is intentional: the innermost layer must not know EF Core, ASP.NET Core, or any framework exists. It expresses only business rules.

### Entities

**[ApplicationUser](../../Services/Identity/Identity.Domain/Entities/ApplicationUser.cs)** — a plain class (not an EF Core / ASP.NET Identity base type), with private setters and a private constructor. All mutation happens through named methods that protect invariants:

| Member | Purpose |
| --- | --- |
| `Id, Email, DisplayName, Role, IsEmailVerified, PasswordHash` | State, all privately settable |
| `RefreshTokens` | Read-only view over an internal `List<RefreshToken>` |
| `Create(email, displayName)` (static factory) | Only way to construct a user; rejects blank email/display name, trims both, defaults `Role = Customer`, `IsEmailVerified = false` |
| `VerifyEmail()` | Flips `IsEmailVerified` to `true` |
| `UpdateProfile(displayName)` | Re-validates and re-trims display name |
| `AssignRole(role)` | Used by `AssignRoleCommandHandler` (Application layer) |
| `SetPasswordHash(hash)` | Used by `UserRepository` (Infrastructure layer) after hashing — the entity never hashes its own password, keeping hashing algorithm choice out of Domain |

**[RefreshToken](../../Services/Identity/Identity.Domain/Entities/RefreshToken.cs)** — `Id, Token, ExpiresAt, CreatedAt, UserId`, plus a computed `IsExpired` (`DateTime.UtcNow >= ExpiresAt`). `Create(userId, expiresAt)` generates the opaque token value from two concatenated base64-encoded GUIDs. A second public constructor exists explicitly for test mocks and EF Core materialization (commented in the source as "not for application code" — application code must go through `Create`).

### Enums

**[UserRole](../../Services/Identity/Identity.Domain/Enums/UserRole.cs)** — `Customer | Vendor | Admin`. Stored as `int` in the database (see `AppDbContext.OnModelCreating`), parsed from string at the API boundary (`AssignRoleCommandHandler` uses `Enum.TryParse`).

### Exceptions

All inherit a common `DomainException(string message) : Exception`, so the API layer can catch the base type as a fallback and each subtype for a specific HTTP status:

| Exception | Thrown when | Mapped to |
| --- | --- | --- |
| `DuplicateEmailException` | Registration with an already-registered email | 409 |
| `InvalidCredentialsException` | Wrong password *or* unknown email at login; expired/unknown/foreign refresh token | 401 |
| `NotFoundException` | User looked up by ID no longer exists | 404 |
| `DomainException` (base, thrown directly) | Invalid role string in `AssignRoleCommand` | 400 |

Note `LoginCommandHandler` deliberately throws the *same* `InvalidCredentialsException` whether the email doesn't exist or the password is wrong — this prevents user enumeration via the login endpoint.

---

## 2. Identity.Application — Use Cases (CQRS)

**[Identity.Application.csproj](../../Services/Identity/Identity.Application/Identity.Application.csproj)** references only `Identity.Domain`, plus `MediatR` and `FluentValidation`. This is the Dependency Inversion point of the whole service: business workflows are defined here, but nothing about *how* they're persisted or exposed.

### Commands + Handlers

**[Commands/](../../Services/Identity/Identity.Application/Commands/)**

| Command | Returns | Handler responsibility |
| --- | --- | --- |
| `RegisterUserCommand(Email, Password, DisplayName)` | `AuthResponse` | Rejects duplicate email, creates the user via `IUserRepository.CreateAsync`, issues JWT + refresh token |
| `LoginCommand(Email, Password)` | `AuthResponse` | Verifies credentials via `IUserRepository`; throws `InvalidCredentialsException` uniformly on any failure |
| `RefreshTokenCommand(Token)` | `AuthResponse` | Looks up + validates the refresh token, loads the owning user, **revokes the old token**, issues a new JWT + refresh token pair (rotation) |
| `LogoutCommand(Token)` | `Unit` | Revokes the supplied refresh token |
| `AssignRoleCommand(UserId, Role)` | `Unit` | Loads user, parses `Role` string to `UserRole` enum, calls `user.AssignRole()`, persists |

### Queries + Handlers

**[Queries/](../../Services/Identity/Identity.Application/Queries/)**

| Query | Returns | Handler responsibility |
| --- | --- | --- |
| `GetCurrentUserQuery(UserId)` | `UserProfileDto` | Resolves the caller's own profile from the `userId` JWT claim |
| `SearchUsersByNameQuery(Name)` | `IReadOnlyList<UserProfileDto>` | Delegates to `IUserRepository.SearchByNameAsync`, projects to DTOs |

Every command/query is an immutable `record` implementing MediatR's `IRequest`/`IRequest<TResponse>`; handlers never touch `DbContext`, `UserManager`, or `HttpContext` directly — every external effect goes through an interface this layer owns.

### Interfaces (the inversion point)

**[Interfaces/](../../Services/Identity/Identity.Application/Interfaces/)** — `IUserRepository`, `ITokenService`, `IRefreshTokenRepository`. Declared here, implemented in `Identity.Infrastructure`, wired together in `Identity.Api/Program.cs`. This is what makes handlers unit-testable with mocks and makes the storage/token technology swappable without touching business logic.

### Validators (FluentValidation)

**[Validators/](../../Services/Identity/Identity.Application/Validators/)**

- `RegisterUserCommandValidator` — email format; password ≥8 chars with upper/lower/digit/special-char rules; display name required, ≤100 chars
- `LoginCommandValidator` — email format + non-empty password only (no complexity check — login must not leak password policy)

### Pipeline Behavior

**[Behaviors/ValidationBehavior.cs](../../Services/Identity/Identity.Application/Behaviors/ValidationBehavior.cs)** — an open-generic `IPipelineBehavior<TRequest,TResponse>` registered once in `Program.cs`, so it wraps *every* command/query. It runs all matching `IValidator<TRequest>` instances before the handler executes and throws `FluentValidation.ValidationException` on failure — handlers never contain input-shape validation, only business rules. A `LoggingBehavior` is planned as a second behavior using the same mechanism.

### DTOs

**[DTOs/](../../Services/Identity/Identity.Application/DTOs/)** — `AuthResponse(AccessToken, RefreshToken, Email, DisplayName, Role)` and `UserProfileDto(Id, Email, DisplayName, Role, IsEmailVerified)`. Deliberately separate from `ApplicationUser` so the entity can evolve without changing the public contract, and so internal-only fields (like `PasswordHash`) are structurally impossible to leak.

---

## 3. Identity.Infrastructure — Persistence, Hashing, JWT

**[Identity.Infrastructure.csproj](../../Services/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj)** references Domain + Application, plus `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.Options`. This layer supplies the concrete technology behind every interface Application declared.

### Persistence

**[AppDbContext](../../Services/Identity/Identity.Infrastructure/Persistence/AppDbContext.cs)** — a minimal `DbContext` with two `DbSet`s: `Users` and `RefreshTokens`. `OnModelCreating` configures:
- `ApplicationUser`: `Id` as an app-generated (non-DB-generated) key, unique index on `Email`, `DisplayName` ≤100 chars, `PasswordHash` ≤500 chars, `Role` stored as `int`, and a cascade-delete one-to-many to `RefreshTokens`.
- `RefreshToken`: app-generated `Id`, unique index on `Token` (≤500 chars).

Note this is a hand-rolled `DbContext`, **not** `IdentityDbContext<ApplicationUser>` — despite referencing `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, the service does not use ASP.NET Core Identity's `UserManager`/`SignInManager` machinery; it uses only `IPasswordHasher<ApplicationUser>` for hashing and rolls its own repository queries.

**[UserRepository : IUserRepository](../../Services/Identity/Identity.Infrastructure/Persistence/Repositories/UserRepository.cs)** — implements all seven `IUserRepository` methods directly against `AppDbContext` + `IPasswordHasher<ApplicationUser>`:
- `CreateAsync` hashes the password via `IPasswordHasher.HashPassword` before saving.
- `CheckPasswordAsync` uses `IPasswordHasher.VerifyHashedPassword`, treating anything except `PasswordVerificationResult.Failed` as success (so it also accepts `SuccessRehashNeeded` without special handling).
- `SearchByNameAsync` does a case-sensitivity-dependent `Contains` filter on `DisplayName`, ordered alphabetically — backs the new `SearchUsersByNameQuery`.

**[RefreshTokenRepository : IRefreshTokenRepository](../../Services/Identity/Identity.Infrastructure/Persistence/Repositories/RefreshTokenRepository.cs)** — straightforward EF Core CRUD: `GetByTokenAsync` (`SingleOrDefaultAsync`), `SaveAsync` (`AddAsync` + `SaveChangesAsync`), `RevokeAsync` (finds and `Remove`s; no-op, no throw, if the token doesn't exist — logout/refresh-rotation calls stay idempotent).

### JWT

**[JwtSettings](../../Services/Identity/Identity.Infrastructure/Settings/JwtSettings.cs)** — POCO bound from the `"JwtSettings"` config section: `Secret`, `Issuer`, `Audience`, `ExpiryMinutes` (default 60).

**[TokenService : ITokenService](../../Services/Identity/Identity.Infrastructure/Jwt/TokenService.cs)**:
- `GenerateJwtToken(user)` — signs a JWT (`HmacSha256`, symmetric key from `JwtSettings.Secret`) with four claims: `userId`, `ClaimTypes.Email`, `ClaimTypes.Role`, and a custom `emailVerified` (lowercased string `"true"/"false"`). These four claims are the platform-wide contract every other ShopFlow service relies on to authorize requests.
- `GenerateRefreshTokenAsync(userId)` — creates a `RefreshToken` valid for 7 days via the Domain factory `RefreshToken.Create`, persists it through `IRefreshTokenRepository.SaveAsync`, and returns it.

---

## 4. Identity.Api — Controllers, Middleware, Composition Root

**[Identity.API.csproj](../../Services/Identity/Identity.Api/Identity.API.csproj)** (`Sdk="Microsoft.NET.Sdk.Web"`) references Application + Infrastructure, plus `Serilog.AspNetCore`, `Swashbuckle.AspNetCore`, `Microsoft.AspNetCore.OpenApi`, `AspNetCore.HealthChecks.SqlServer`, `FluentValidation.DependencyInjectionExtensions`, `MediatR`.

### Endpoints

```text
POST   /api/auth/register                             → 201 Created
POST   /api/auth/login                                → 200 OK
POST   /api/auth/refresh                              → 200 OK
POST   /api/auth/logout                  [Authorize]  → 204 No Content
GET    /api/users/me                     [Authorize]  → 200 OK
GET    /api/admin/users?name=            [RequireAdmin] → 200 OK
POST   /api/admin/users/{id}/assign-role [RequireAdmin] → 200 OK
```

**[AuthController](../../Services/Identity/Identity.Api/Controllers/AuthController.cs)** — every action is a one-liner: build/forward a MediatR request, return the mapped status code. No business logic lives here.

**[UsersController](../../Services/Identity/Identity.Api/Controllers/UsersController.cs)** — `GetMe` reads the `userId` claim off `User` (populated by JWT auth) rather than trusting a client-supplied ID; `SearchUsers` and `AssignRole` are gated by the `RequireAdmin` policy.

### Middleware

**[ExceptionHandlingMiddleware](../../Services/Identity/Identity.Api/Middleware/ExceptionHandlingMiddleware.cs)** — the single place exceptions become HTTP responses, registered first in the pipeline (before Swagger/auth) so it wraps everything downstream:

| Exception caught | Status | Body |
| --- | --- | --- |
| `FluentValidation.ValidationException` | 400 | `{ errors: [{ propertyName, errorMessage }] }` |
| `InvalidCredentialsException` | 401 | `{ message }` |
| `NotFoundException` | 404 | `{ message }` |
| `DuplicateEmailException` | 409 | `{ message }` |
| `DomainException` (base, catch-all for Domain errors) | 400 | `{ message }` |
| Any other `Exception` | 500 | Generic message; full exception logged via Serilog |

Catch order matters — more specific `DomainException` subtypes are caught before the base `DomainException` clause, and C# evaluates `catch` blocks top-to-bottom.

### Composition root — Program.cs

**[Program.cs](../../Services/Identity/Identity.Api/Program.cs)** is where every other layer gets wired together:

- Binds `JwtSettings` from configuration.
- Registers `AppDbContext` against SQL Server (`ConnectionStrings:Default`).
- Registers `IRefreshTokenRepository`, `IUserRepository`, `ITokenService`, and `IPasswordHasher<ApplicationUser>` as **Scoped**.
- Registers MediatR scanning the `Identity.Application` assembly (picks up every handler automatically).
- Registers FluentValidation scanning the same assembly, and adds `ValidationBehavior<,>` as an open-generic `IPipelineBehavior<,>`.
- Configures JWT Bearer authentication — notably, `JwtBearerOptions` are configured **lazily** via `IOptions<JwtSettings>` inside `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtSettings>>(...)`, specifically so that `WebApplicationFactory` config overrides in tests take effect (a naive `AddJwtBearer(opts => ...)` would capture config too early).
- Registers three authorization policies — `RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail` — that propagate platform-wide per the Phase 2 plan.
- Registers a `/health` check against SQL Server.
- **Dev-only startup block**: `db.Database.EnsureCreated()` plus seeding one admin account from the `AdminSeed` config section (see [appsettings.Development.json](../../Services/Identity/Identity.Api/appsettings.Development.json): `admin@shopflow.com` / `Admin@12345`), skipped if that email already exists.
- `public partial class Program` at the bottom — the hook `WebApplicationFactory<Program>` needs to boot the app in-process for integration tests.

---

## 5. Test Projects

| Test project | Targets | Style | Notable packages |
| --- | --- | --- | --- |
| **Identity.Domain.Tests** | `Identity.Domain` | Pure unit, no mocks | xunit, FluentAssertions |
| **Identity.Application.Tests** | `Identity.Application` | Handlers/validators tested against **NSubstitute** mocks of `IUserRepository`/`ITokenService`/`IRefreshTokenRepository` — no DB, no HTTP | + NSubstitute, FluentValidation |
| **Identity.Infrastructure.Tests** | `Identity.Infrastructure` | `TokenService` unit tests (claims, structure, uniqueness) + `RefreshTokenRepository` tests against a **real SQL Server via Testcontainers** | + NSubstitute, **Testcontainers.MsSql** |
| **Identity.Api.Tests** | Full stack via `Identity.Api` | End-to-end HTTP tests through `WebApplicationFactory`, with `AppDbContext` swapped for **EF Core InMemory** and `IUserRepository`/`IRefreshTokenRepository` swapped for in-memory fakes | + Microsoft.AspNetCore.Mvc.Testing, EF Core InMemory |

**Identity.Api.Tests fixtures** ([Fixtures/](../../Services/Identity/Identity.Api.Tests/Fixtures/)):
- `IdentityApiFactory` — the `WebApplicationFactory<Program>` subclass doing the swaps above and injecting deterministic JWT settings
- `FakeUserRepository` / `FakeRefreshTokenRepository` — in-memory dictionary-backed fakes with `Seed()` helpers
- `JwtTokenHelper` — generates signed test JWTs matching production claim structure, so tests can authenticate as arbitrary users/roles without going through `/login`

This mirrors the architecture deliberately: the closer a component is to the Domain center, the cheaper and more deterministic its tests are; the closer to the Api edge, the more the tests verify real infrastructure wiring (real SQL Server for the repository that owns the unique-token invariant, real HTTP pipeline for status-code mapping).

---

## 6. Request Flow — End to End Example

`POST /api/auth/register`:

1. **Api**: `AuthController.Register` builds `RegisterUserCommand` from the request body, calls `IMediator.Send`.
2. **Application (pipeline)**: `ValidationBehavior` runs `RegisterUserCommandValidator` — bad email/password/display-name → `ValidationException` → **Api**'s `ExceptionHandlingMiddleware` → HTTP 400.
3. **Application (handler)**: `RegisterUserCommandHandler`:
   - `IUserRepository.ExistsByEmailAsync` (→ **Infrastructure**'s `UserRepository`, an EF Core query) — if true, throws `DuplicateEmailException` (**Domain**) → HTTP 409.
   - `ApplicationUser.Create(...)` (**Domain** factory — enforces entity invariants).
   - `IUserRepository.CreateAsync` → **Infrastructure** hashes the password via `IPasswordHasher` and persists via `AppDbContext`.
   - `ITokenService.GenerateJwtToken` / `GenerateRefreshTokenAsync` → **Infrastructure**'s `TokenService` signs a JWT and persists a `RefreshToken` (**Domain** factory) via `IRefreshTokenRepository`.
   - Returns `AuthResponse` (**Application** DTO).
4. **Api**: controller returns HTTP 201 with the `AuthResponse` body.

Every arrow that crosses a layer boundary crosses through an interface owned by the *inner* layer — the concrete implementation is always supplied from further out, never referenced directly.

---

## 7. Configuration & Running

Connection string, JWT secret, and the dev admin seed all live in [appsettings.Development.json](../../Services/Identity/Identity.Api/appsettings.Development.json). Full run instructions are in [Documentations/RUNNING.md](../RUNNING.md); summary:

```bash
docker compose up -d sqlserver
dotnet run --project Services/Identity/Identity.Api
```

- API: `http://localhost:5015`, Swagger: `/swagger`, health: `/health`
- Dev seed account: `admin@shopflow.com` / `Admin@12345`
- `dotnet test ShopFlow.sln` — note `Identity.Infrastructure.Tests` needs Docker running (Testcontainers)

---

## Summary — what each layer answers

| Layer | Answers |
| --- | --- |
| `Identity.Domain` | What is a valid entity state, and what operations can change it? |
| `Identity.Application` | What does the system do, step by step, for each use case — and what does it require from the outside world? |
| `Identity.Infrastructure` | How is that requirement actually fulfilled — SQL Server, password hashing, JWT signing? |
| `Identity.Api` | How is it exposed over HTTP, how are failures translated to status codes, and how is everything wired together at startup? |
