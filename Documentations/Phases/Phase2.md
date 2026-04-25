# Phase 2 — Identity Service

## Project Structure

```
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

---

## Application Layer

**Commands + Handlers:** ✅ implemented

| Command | Handler responsibility | Status |
| --- | --- | --- |
| `RegisterUserCommand` | Checks for duplicate email, creates user via `IUserRepository`, issues JWT + refresh token | ✅ Done |
| `LoginCommand` | Validates credentials via `IUserRepository`, throws `InvalidCredentialsException` on any failure, issues tokens | ✅ Done |
| `RefreshTokenCommand` | Validates + revokes old token via `IRefreshTokenRepository`, issues new JWT + refresh token pair | ✅ Done |
| `LogoutCommand` | Invalidate refresh token in DB | ⬜ Pending |
| `AssignRoleCommand` | Admin-only role assignment | ⬜ Pending |

**Queries + Handlers:**

| Query | Handler responsibility | Status |
| --- | --- | --- |
| `GetCurrentUserQuery` | Return profile from claims | ⬜ Pending |

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

**Pipeline Behaviors:** ✅ partially implemented

- `ValidationBehavior<TRequest, TResponse>` — runs all `IValidator<TRequest>` instances; throws `ValidationException` before the handler if any failures exist ✅ Done
- `LoggingBehavior<TRequest, TResponse>` ⬜ Pending

---

## Infrastructure Layer

**Persistence:**
- `IdentityDbContext : IdentityDbContext<ApplicationUser>` — includes `RefreshTokens` DbSet
- `RefreshTokenRepository : IRefreshTokenRepository`
- EF Core migrations → `IdentityDb`

**JWT:**
- `TokenService : ITokenService`
  - Issues JWT with claims: `userId`, `email`, `role`, `emailVerified`
  - Generates opaque refresh token (random bytes, stored in DB with expiry)
  - Refresh token rotation on each use

**Authorization Policies** (registered in `Program.cs`):
```csharp
options.AddPolicy("RequireVendor", p => p.RequireRole("Vendor"));
options.AddPolicy("RequireAdmin",  p => p.RequireRole("Admin"));
options.AddPolicy("RequireVerifiedEmail", p => p.RequireClaim("emailVerified", "true"));
```

---

## API Layer

**Endpoints:**
```
POST   /api/auth/register
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout
GET    /api/users/me              [Authorize]
PUT    /api/users/me              [Authorize]
POST   /api/admin/users/{id}/assign-role   [RequireAdmin]
```

**Middleware:**
- `ExceptionHandlingMiddleware` — maps domain exceptions to HTTP status codes

**Health check:**
- `/health` — SQL Server connectivity

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

**Identity.Infrastructure.Tests** — Testcontainers (real SQL Server): ⬜ pending

- `RefreshTokenRepository` — add, get by token, revoke
- `TokenService` — JWT contains correct claims, refresh token is unique per call

**Identity.API.Tests** — `WebApplicationFactory`: ⬜ pending

- `POST /api/auth/register` → 201 on valid input, 400 on duplicate email
- `POST /api/auth/login` → 200 with tokens, 401 on bad credentials
- `GET /api/users/me` → 401 without token, 200 with valid JWT
- `POST /api/admin/users/{id}/assign-role` → 403 for non-admin, 200 for admin

---

## NuGet Packages

| Package | Project | Status |
| --- | --- | --- |
| `MediatR 12.5.0` | `Identity.Application` | ✅ Added |
| `FluentValidation 11.11.0` | `Identity.Application`, `Identity.Application.Tests` | ✅ Added |
| `FluentAssertions 6.12.2` | `Identity.Application.Tests` | ✅ Added |
| `NSubstitute 5.3.0` | `Identity.Application.Tests` | ✅ Added |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | `Identity.Infrastructure` | ⬜ Pending |
| `Microsoft.EntityFrameworkCore.SqlServer` | `Identity.Infrastructure` | ⬜ Pending |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | `Identity.API` | ⬜ Pending |
| `System.IdentityModel.Tokens.Jwt` | `Identity.Infrastructure` | ⬜ Pending |
| `FluentValidation.AspNetCore` | `Identity.API` | ⬜ Pending |
| `Serilog.AspNetCore` | `Identity.API` | ⬜ Pending |
| `AspNetCore.Diagnostics.HealthChecks` + `.SqlServer` | `Identity.API` | ⬜ Pending |
| `Testcontainers.MsSql` | `Identity.Infrastructure.Tests` | ⬜ Pending |
| `Microsoft.AspNetCore.Mvc.Testing` | `Identity.API.Tests` | ⬜ Pending |

---

## TDD Order for Phase 2

```
1. ✅ Domain entity tests        → ApplicationUser, RefreshToken
2. ✅ Validator tests            → RegisterUserCommandValidator, LoginCommandValidator
3. ✅ ValidationBehavior test    → pipeline rejects bad input, does not call next
4. ✅ RegisterUserHandler test   → happy path + duplicate email
5. ✅ LoginHandler test          → valid creds + wrong creds
6. ✅ RefreshTokenHandler test   → rotation + expiry + unknown token
7.    TokenService test          → JWT claims, refresh uniqueness (Testcontainers)
8.    Repository test            → RefreshTokenRepository (Testcontainers)
9.    API endpoint tests         → WebApplicationFactory
```
