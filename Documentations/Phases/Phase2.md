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

**Queries + Handlers:**

| Query | Handler responsibility | Status |
| --- | --- | --- |
| `GetCurrentUserQuery` | Resolves `UserProfileDto` from `IUserRepository.GetByIdAsync` using `userId` claim; throws `NotFoundException` if user no longer exists | ✅ Done |

**DTOs:** ✅ implemented

- `AuthResponse` — `AccessToken`, `RefreshToken`, `Email`, `DisplayName`, `Role`
- `UserProfileDto` — `Id`, `Email`, `DisplayName`, `Role`, `IsEmailVerified`

**Interfaces:** ✅ implemented

- `IUserRepository` — `ExistsByEmail`, `Create`, `FindByEmail`, `GetById`, `CheckPassword`, `Update`
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

- `AppDbContext : DbContext` — minimal `DbContext` with `RefreshTokens` DbSet; fluent config enforces unique index on `Token` (max 500 chars); ASP.NET Core Identity tables deferred until `UserRepository` is fully wired
- `RefreshTokenRepository : IRefreshTokenRepository` ✅ — `GetByToken` (SingleOrDefault), `Save` (Add + SaveChanges), `Revoke` (Remove + SaveChanges, no-op on unknown token)
- `UserRepository : IUserRepository` ⬜ — stub; every method throws `NotImplementedException` pending full `UserManager<ApplicationUser>` wiring
- EF Core migrations → `IdentityDb` ⬜ Pending

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
POST   /api/auth/logout           [Authorize]     → 204 No Content
GET    /api/users/me              [Authorize]     → 200 OK
POST   /api/admin/users/{id}/assign-role  [RequireAdmin]  → 200 OK
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
| Any unhandled `Exception` | 500 — generic message, full exception logged via Serilog |

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
