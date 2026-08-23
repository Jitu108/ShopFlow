# Phase 2 — Identity Service

## Project Structure

```text
Services/Identity/
├── Identity.Domain/
├── Identity.Application/
├── Identity.Infrastructure/
├── Identity.API/
├── Identity.Domain.Tests/
├── Identity.Application.Tests/
├── Identity.Infrastructure.Tests/
└── Identity.API.Tests/
```

---

## Domain Layer

**Entities:**

- `ApplicationUser : IdentityUser` — extends ASP.NET Identity with `DisplayName`, `Role`, `IsEmailVerified`
- `RefreshToken` — `Token`, `ExpiresAt`, `UserId (FK)`

**Enums:**

- `UserRole` — `Customer | Vendor | Admin`

**Exceptions:**

- `DomainException`
- `NotFoundException`
- `InvalidCredentialsException`
- `DuplicateEmailException` — thrown by `RegisterUserCommandHandler` when email already exists; mapped to HTTP 409

---

## Application Layer

**Commands + Handlers:** ✅ implemented

| Command | Handler responsibility | Status |
| --- | --- | --- |
| `RegisterUserCommand` | Checks for duplicate email, creates user via `IUserRepository`, issues JWT + refresh token | ✅ Done |
| `LoginCommand` | Validates credentials via `IUserRepository`, throws `InvalidCredentialsException` on any failure, issues tokens | ✅ Done |
| `RefreshTokenCommand` | Validates + revokes old token via `IRefreshTokenRepository`, issues new JWT + refresh token pair | ✅ Done |
| `LogoutCommand` | Revokes the supplied refresh token via `IRefreshTokenRepository` | ✅ Done |
| `AssignRoleCommand` | Looks up user, parses `UserRole` enum, calls `user.AssignRole()`, persists; throws `NotFoundException` / `DomainException` on invalid input | ✅ Done |
| `ResetPasswordCommand` | Admin-only: looks up the target user by `{id}`, re-hashes and persists a new password via `IUserRepository.ResetPasswordAsync`; throws `NotFoundException` if the user doesn't exist | ✅ Done |

**Queries + Handlers:**

| Query | Handler responsibility | Status |
| --- | --- | --- |
| `GetCurrentUserQuery` | Resolves `UserProfileDto` from `IUserRepository.GetByIdAsync` using `userId` claim; throws `NotFoundException` if user no longer exists | ✅ Done |
| `SearchUsersByNameQuery` | Admin-only: delegates to `IUserRepository.SearchByNameAsync`, projects to `UserProfileDto` list | ✅ Done |

**DTOs:** ✅ implemented

- `AuthResponse` — `AccessToken`, `RefreshToken`, `Email`, `DisplayName`, `Role`
- `UserProfileDto` — `Id`, `Email`, `DisplayName`, `Role`, `IsEmailVerified`

**Interfaces:** ✅ implemented

- `IUserRepository` — `ExistsByEmail`, `Create`, `FindByEmail`, `GetById`, `CheckPassword`, `Update`, `SearchByName`, `ResetPassword`
- `ITokenService` — `GenerateJwtToken`, `GenerateRefreshTokenAsync`
- `IRefreshTokenRepository` — `GetByToken`, `Save`, `Revoke`

**Validators (FluentValidation):** ✅ implemented

- `RegisterUserCommandValidator` — email format, strong password (≥8 chars, upper, lower, digit, special), display name required and ≤100 chars
- `LoginCommandValidator` — email format, password required

**Pipeline Behaviors:**

- `ValidationBehavior<TRequest, TResponse>` — runs all `IValidator<TRequest>` instances; throws `ValidationException` before the handler if any failures exist ✅ Done
- `LoggingBehavior<TRequest, TResponse>` ⬜ Pending

---

## Infrastructure Layer

**Persistence:** ✅ implemented

- `AppDbContext : DbContext` — `Users` (`ApplicationUser`) and `RefreshTokens` DbSets; fluent config enforces a unique index on `Email` and on `Token` (max 500 chars). **Not** ASP.NET Core Identity's own tables — a plain EF Core mapping of `ApplicationUser`/`RefreshToken` instead
- `RefreshTokenRepository : IRefreshTokenRepository` ✅ — `GetByToken` (SingleOrDefault), `Save` (Add + SaveChanges), `Revoke` (Remove + SaveChanges, no-op on unknown token)
- `UserRepository : IUserRepository` ✅ implemented — **not** wired to `UserManager<ApplicationUser>` as originally planned; instead a custom implementation directly over `AppDbContext` + `IPasswordHasher<ApplicationUser>` (`ExistsByEmailAsync`, `CreateAsync`, `FindByEmailAsync`, `GetByIdAsync`, `CheckPasswordAsync`, `UpdateAsync`, `SearchByNameAsync`, `ResetPasswordAsync`)
- EF Core migrations → `IdentityDb` — still not added; `IdentityDb` is created via `Database.EnsureCreated()` at Development startup (same as Product Service), confirmed created and in use, just not via migrations

**JWT:** ✅ implemented

- `JwtSettings` POCO — binds from `"JwtSettings"` config section (`Secret`, `Issuer`, `Audience`, `ExpiryMinutes`)
- `TokenService : ITokenService` — issues JWT with claims `userId`, `email` (`ClaimTypes.Email`), `role` (`ClaimTypes.Role`), `emailVerified`; 7-day refresh tokens created via `RefreshToken.Create()` and persisted via `IRefreshTokenRepository.SaveAsync`

**Authorization Policies** (registered in `Program.cs`): ✅ implemented

```csharp
options.AddPolicy("RequireVendor",        p => p.RequireRole("Vendor"));
options.AddPolicy("RequireAdmin",         p => p.RequireRole("Admin"));
options.AddPolicy("RequireVerifiedEmail", p => p.RequireClaim("emailVerified", "true"));
```

---

## API Layer

**Endpoints:** ✅ implemented

```text
POST   /api/auth/register                         → 201 Created
POST   /api/auth/login                            → 200 OK
POST   /api/auth/refresh                          → 200 OK
POST   /api/auth/logout                     [Authorize]     → 204 No Content
GET    /api/users/me                        [Authorize]     → 200 OK
GET    /api/admin/users?name=               [RequireAdmin]  → 200 OK
POST   /api/admin/users/{id}/assign-role    [RequireAdmin]  → 200 OK
POST   /api/admin/users/{id}/reset-password [RequireAdmin]  → 200 OK
```

Note: `PUT /api/users/me` is deferred — no update profile command implemented yet.

**Middleware:** ✅ implemented

`ExceptionHandlingMiddleware` — exception-to-status mapping:

| Exception | HTTP Status |
| --- | --- |
| `ValidationException` | 400 — with `{ errors: [{ propertyName, errorMessage }] }` |
| `InvalidCredentialsException` | 401 |
| `NotFoundException` | 404 |
| `DuplicateEmailException` | 409 |
| `DomainException` | 400 |
| Any unhandled `Exception` | 500 — generic message, full exception logged via plain `ILogger<ExceptionHandlingMiddleware>` (Serilog is referenced in the `.csproj` but never wired up via `UseSerilog(...)` in `Program.cs`, unlike every later service) |

**Program.cs wiring:** ✅ implemented

- `JwtSettings` bound from config; `JwtBearerOptions` configured lazily via `IOptions<JwtSettings>` so `WebApplicationFactory` config overrides take effect
- `AppDbContext` registered for SQL Server
- `IRefreshTokenRepository → RefreshTokenRepository`, `IUserRepository → UserRepository`, `ITokenService → TokenService` (all Scoped)
- MediatR scanning `Identity.Application` assembly; `ValidationBehavior` registered as open-generic `IPipelineBehavior<,>`; validators scanned from Application assembly
- `public partial class Program` — exposes entry point for `WebApplicationFactory`

**Health check:** ✅ implemented

- `/health` — SQL Server connectivity via `AspNetCore.HealthChecks.SqlServer`

---

## Test Projects

**Identity.Domain.Tests** — pure unit, no mocks: ✅ implemented

- `ApplicationUser` property defaults
- `RefreshToken` expiry logic

**Identity.Application.Tests** — mocked interfaces (NSubstitute + FluentAssertions): ✅ implemented

- `RegisterUserCommandHandlerTests` — happy path, duplicate email guard, `CreateAsync` call count
- `LoginCommandHandlerTests` — valid credentials, unknown email, wrong password
- `RefreshTokenCommandHandlerTests` — happy path + old token revocation, expired token, unknown token
- `ValidationBehaviorTests` — valid request calls next, invalid request throws `ValidationException` and does not call next
- `RegisterUserCommandValidatorTests` — all password complexity rules, blank/null email, malformed email, blank/oversized display name
- `LoginCommandValidatorTests` — blank/null email, malformed email, blank/null password

**Identity.Infrastructure.Tests** — unit + Testcontainers: ✅ implemented

- `TokenServiceTests` (9 tests) — `userId`, `email`, `role`, `emailVerified` claims, three-part JWT structure, `SaveAsync` called once, non-empty token value, uniqueness across two calls, correct `UserId` on returned `RefreshToken`
- `RefreshTokenRepositoryTests` (6 Testcontainers tests, real SQL Server) — save+get roundtrip, persisted `UserId` and `ExpiresAt`, unknown token returns null, revoke+get returns null, revoke of non-existent token does not throw

**Identity.API.Tests** — `WebApplicationFactory`: ✅ implemented

Fixtures:

- `IdentityApiFactory` — swaps `AppDbContext` for EF Core InMemory, replaces `IUserRepository` and `IRefreshTokenRepository` with in-memory fakes, injects deterministic JWT settings
- `FakeUserRepository` — `Dictionary<Guid, (User, Password)>` with `Seed()` helper
- `FakeRefreshTokenRepository` — `Dictionary<string, RefreshToken>`
- `JwtTokenHelper` — static helper generating signed test JWTs matching production claim structure

Tests:

- `AuthControllerTests` (9 tests) — register 201, tokens in body, duplicate 409, invalid input 400, login 200 + tokens, wrong password 401, unknown email 401, refresh valid 200, refresh invalid 401, logout 204
- `UsersControllerTests` (5 tests) — getMe without token 401, with valid JWT 200, profile body correct, assignRole as Customer 403, as Admin 200

---

## Issues Found and Fixed

| # | Project | Problem | Fix |
| --- | --- | --- | --- |
| 1 | `Identity.Application.csproj` | Had a reference to its own test project `Identity.Application.Tests` — circular and wrong | Removed bad reference; added correct `→ Identity.Domain` |
| 2 | `Identity.API.Tests.csproj` | Referenced `..\Identity.Api\Identity.Api.csproj` but actual filename is `Identity.API.csproj` (capitalisation mismatch) — build would fail | Fixed path to `..\Identity.Api\Identity.API.csproj` |
| 3 | `Identity.Infrastructure.csproj` | No project references — `Infrastructure` had no knowledge of `Domain` or `Application` | Added references to `Identity.Domain` + `Identity.Application` |
| 4 | `Identity.API.csproj` | No project references — `API` had no knowledge of `Application` or `Infrastructure` | Added references to `Identity.Application` + `Identity.Infrastructure` |
| 5 | `Identity.Domain.Tests/` | csproj filename was `Idetity.Domain.Tests.csproj` (missing 'n') — typo in filename and solution entry | Renamed file to `Identity.Domain.Tests.csproj`; updated path in `ShopFlow.sln` |

**Total: 92 tests, all passing** (21 Domain, 38 Application, 16 Infrastructure via Testcontainers, 17 API via `WebApplicationFactory`).

---

## NuGet Packages

| Package | Project | Status |
| --- | --- | --- |
| `MediatR 12.5.0` | `Identity.Application` | ✅ Added |
| `FluentValidation 11.11.0` | `Identity.Application`, `Identity.Application.Tests` | ✅ Added |
| `FluentAssertions 6.12.2` | `Identity.Application.Tests` | ✅ Added |
| `NSubstitute 5.3.0` | `Identity.Application.Tests` | ✅ Added |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.0` | `Identity.Infrastructure` | ✅ Added |
| `Microsoft.EntityFrameworkCore.SqlServer 10.0.0` | `Identity.Infrastructure`, `Identity.Infrastructure.Tests` | ✅ Added |
| `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0` | `Identity.Infrastructure` | ✅ Added |
| `Microsoft.Extensions.Options 10.0.0` | `Identity.Infrastructure`, `Identity.Infrastructure.Tests` | ✅ Added |
| `FluentValidation.DependencyInjectionExtensions 11.11.0` | `Identity.API` | ✅ Added |
| `MediatR 12.5.0` | `Identity.API` | ✅ Added |
| `Serilog.AspNetCore 9.0.0` | `Identity.API` | ✅ Added |
| `AspNetCore.HealthChecks.SqlServer 9.0.0` | `Identity.API` | ✅ Added |
| `Testcontainers.MsSql 4.4.0` | `Identity.Infrastructure.Tests` | ✅ Added |
| `NSubstitute 5.3.0` | `Identity.Infrastructure.Tests` | ✅ Added |
| `FluentAssertions 6.12.2` | `Identity.Infrastructure.Tests`, `Identity.API.Tests` | ✅ Added |
| `Microsoft.AspNetCore.Mvc.Testing 10.0.0` | `Identity.API.Tests` | ✅ Added |
| `Microsoft.EntityFrameworkCore.InMemory 10.0.0` | `Identity.API.Tests` | ✅ Added |
| `System.IdentityModel.Tokens.Jwt` | (bundled with `JwtBearer`) | ✅ Available |

---

## How to Run

> Full details in [Documentations/RUNNING.md](../RUNNING.md).

```bash
# 1. Start SQL Server (must be healthy before the API starts)
docker compose up -d sqlserver

# 2. Run the Identity Service
dotnet run --project Services/Identity/Identity.Api
```

On first startup in Development, the application auto-creates `IdentityDb` and seeds an admin account:

| Field    | Value                  |
| -------- | ---------------------- |
| Email    | `admin@shopflow.com`   |
| Password | `Admin@12345`          |

**URLs:**

| URL                                   | Purpose      |
| ------------------------------------- | ------------ |
| `http://localhost:5015`               | API base     |
| `http://localhost:5015/swagger`       | Swagger UI   |
| `http://localhost:5015/health`        | Health check |

**Run tests:**

```bash
dotnet test ShopFlow.sln
```

> `Identity.Infrastructure.Tests` uses Testcontainers — Docker must be running.

---

## TDD Order for Phase 2

```text
1. ✅ Domain entity tests        → ApplicationUser, RefreshToken
2. ✅ Validator tests            → RegisterUserCommandValidator, LoginCommandValidator
3. ✅ ValidationBehavior test    → pipeline rejects bad input, does not call next
4. ✅ RegisterUserHandler test   → happy path + duplicate email
5. ✅ LoginHandler test          → valid creds + wrong creds
6. ✅ RefreshTokenHandler test   → rotation + expiry + unknown token
7. ✅ TokenService test          → JWT claims, refresh uniqueness (NSubstitute + unit)
8. ✅ Repository test            → RefreshTokenRepository (Testcontainers)
9. ✅ API endpoint tests         → WebApplicationFactory
```
