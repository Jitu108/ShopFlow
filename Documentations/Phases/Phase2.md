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

**Commands + Handlers:**
| Command | Handler responsibility |
|---|---|
| `RegisterUserCommand` | Create user via `UserManager`, assign default `Customer` role |
| `LoginCommand` | Validate credentials, issue JWT + refresh token |
| `RefreshTokenCommand` | Validate refresh token, rotate it, issue new JWT |
| `LogoutCommand` | Invalidate refresh token in DB |
| `AssignRoleCommand` | Admin-only role assignment |

**Queries + Handlers:**
| Query | Handler responsibility |
|---|---|
| `GetCurrentUserQuery` | Return profile from claims |

**DTOs:**
- `RegisterRequest`, `LoginRequest`, `AuthResponse` (JWT + refresh token), `UserProfileDto`

**Interfaces:**
- `ITokenService` — JWT generation + validation
- `IRefreshTokenRepository`

**Validators (FluentValidation):**
- `RegisterUserCommandValidator` — email format, password strength, required fields
- `LoginCommandValidator`

**Pipeline Behaviors:**
- `ValidationBehavior<TRequest, TResponse>`
- `LoggingBehavior<TRequest, TResponse>`

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

**Identity.Domain.Tests** — pure unit, no mocks:
- `ApplicationUser` property defaults
- `RefreshToken` expiry logic

**Identity.Application.Tests** — mocked interfaces:
- `RegisterUserCommandHandler` — duplicate email returns error
- `LoginCommandHandler` — wrong password throws, correct password returns `AuthResponse`
- `RefreshTokenCommandHandler` — expired token throws, valid token rotates
- `ValidationBehavior` — invalid `RegisterRequest` throws `ValidationException`

**Identity.Infrastructure.Tests** — Testcontainers (real SQL Server):
- `RefreshTokenRepository` — add, get by token, revoke
- `TokenService` — JWT contains correct claims, refresh token is unique per call

**Identity.API.Tests** — `WebApplicationFactory`:
- `POST /api/auth/register` → 201 on valid input, 400 on duplicate email
- `POST /api/auth/login` → 200 with tokens, 401 on bad credentials
- `GET /api/users/me` → 401 without token, 200 with valid JWT
- `POST /api/admin/users/{id}/assign-role` → 403 for non-admin, 200 for admin

---

## NuGet Packages Needed

| Package | Purpose |
|---|---|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | ASP.NET Identity + EF Core |
| `Microsoft.EntityFrameworkCore.SqlServer` | SQL Server provider |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT middleware |
| `System.IdentityModel.Tokens.Jwt` | JWT generation |
| `MediatR` | CQRS pipeline |
| `FluentValidation.AspNetCore` | Request validation |
| `Serilog.AspNetCore` | Structured logging |
| `AspNetCore.Diagnostics.HealthChecks` + `.SqlServer` | Health check |
| `xUnit` + `FluentAssertions` + `NSubstitute` | Unit tests |
| `Testcontainers.MsSql` | Integration tests |
| `Microsoft.AspNetCore.Mvc.Testing` | API tests |

---

## TDD Order for Phase 2

```
1. Domain entity tests        → ApplicationUser, RefreshToken
2. Validator tests            → RegisterUserCommandValidator
3. ValidationBehavior test    → pipeline rejects bad input
4. RegisterUserHandler test   → happy path + duplicate email
5. LoginHandler test          → valid creds + wrong creds
6. RefreshTokenHandler test   → rotation + expiry
7. TokenService test          → JWT claims, refresh uniqueness (Testcontainers)
8. Repository test            → RefreshTokenRepository (Testcontainers)
9. API endpoint tests         → WebApplicationFactory
```
