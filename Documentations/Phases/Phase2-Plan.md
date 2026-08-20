# Phase 2 — Identity Service: Plan

> **Note on this document:** Phase 2 was already built and shipped before this plan was written. No pre-implementation plan file existed for it at the time — this is a retroactive reconstruction, written by working backward from [Phase2.md](Phase2.md) (the completion log), [Architecture/Identity-Service.md](../Architecture/Identity-Service.md), [ShopFlow-Approach.md](../ShopFlow-Approach.md), and [ShopFlow-ProjectSpec.md](../ShopFlow-ProjectSpec.md). It documents what the plan effectively was, not a historical artifact captured before the work happened.

## Context

Per `ShopFlow-Approach.md`, Identity is built immediately after infrastructure and before every other service: "Build this first — everything depends on JWT auth." Product, Cart, and Order all trust the same JWT and the same three claims-based authorization policies, so getting the claim shape and policy set right here is what lets every later service just plug into it rather than reinvent auth. This is also the **first** service built, so — unlike Phase 3/4 — there's no prior service's Clean Architecture scaffold to copy; Identity is what *establishes* the pattern (8-project layout, CQRS/MediatR, FluentValidation pipeline behavior, JWT claim shape) that Product and Cart later reuse verbatim.

**Spec** (`ShopFlow-ProjectSpec.md` FR-01–FR-10):
- Register (email/password/display name, default role `Customer`), login issuing JWT + refresh token, silent renewal via refresh-token rotation, logout, view/update own profile, admin role assignment
- JWT claims: `userId`, `email`, `role`, `emailVerified` — the contract every downstream service validates against
- Three authorization policies that "propagate platform-wide": `RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail`

## Step-by-step plan

### 1. Scaffold the 8 projects
`Services/Identity/Identity.Domain[.Tests]`, `Identity.Application[.Tests]`, `Identity.Infrastructure[.Tests]`, `Identity.Api[.Tests]` — Clean Architecture, dependencies pointing inward only (`Domain ← Application ← Infrastructure ← Api`). This project shape is what Phase 3 (Product) and Phase 4 (Cart) later copy csproj-for-csproj.

### 2. Domain layer (`Identity.Domain`) — zero dependencies
- `ApplicationUser` — plain class (not `IdentityUser`, despite referencing ASP.NET Identity packages later), private setters, mutation only through named methods (`Create`, `VerifyEmail`, `UpdateProfile`, `AssignRole`, `SetPasswordHash`) so invariants can't be bypassed
- `RefreshToken` — `Token`, `ExpiresAt`, `UserId`, computed `IsExpired`; `Create(userId, expiresAt)` factory
- `UserRole` enum — `Customer | Vendor | Admin`
- Exceptions, all inheriting `DomainException`: `NotFoundException`, `InvalidCredentialsException` (deliberately reused for both "unknown email" and "wrong password" at login, to prevent user enumeration), `DuplicateEmailException` (→ 409)

### 3. Application layer (`Identity.Application`) — depends only on Domain
- Commands + handlers: `RegisterUserCommand`, `LoginCommand`, `RefreshTokenCommand` (rotation: revoke old, issue new pair), `LogoutCommand`, `AssignRoleCommand`
- Query: `GetCurrentUserQuery` (resolves from the `userId` claim, never a client-supplied id)
- DTOs: `AuthResponse`, `UserProfileDto` — deliberately separate from `ApplicationUser` so `PasswordHash` can never leak through the API contract
- Interfaces (the inversion point): `IUserRepository`, `ITokenService`, `IRefreshTokenRepository` — declared here, implemented in Infrastructure, wired in Api
- Validators: `RegisterUserCommandValidator` (email format, password complexity, display name ≤100 chars), `LoginCommandValidator` (email format + non-empty password only — no complexity check, so the endpoint never leaks the password policy)
- Pipeline behavior: `ValidationBehavior<TRequest,TResponse>` — open-generic, runs every matching validator before the handler, throws `ValidationException` on failure

### 4. Infrastructure layer (`Identity.Infrastructure`) — depends on Domain + Application
- `AppDbContext` — hand-rolled `DbContext` with `Users`/`RefreshTokens` DbSets (unique index on `Email`, unique index on `Token`), **not** `IdentityDbContext<ApplicationUser>` — despite referencing `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, only `IPasswordHasher<ApplicationUser>` is used, not `UserManager`/`SignInManager`
- `UserRepository : IUserRepository`, `RefreshTokenRepository : IRefreshTokenRepository`
- `JwtSettings` POCO (`Secret`, `Issuer`, `Audience`, `ExpiryMinutes`) bound from config
- `TokenService : ITokenService` — signs JWTs (HMAC-SHA256) with the four platform-wide claims; issues 7-day refresh tokens via the Domain factory

### 5. API layer (`Identity.Api`)
- Endpoints: `POST /api/auth/register|login|refresh|logout`, `GET /api/users/me`, `POST /api/admin/users/{id}/assign-role`
- `ExceptionHandlingMiddleware` — maps each Domain exception to its HTTP status (400/401/404/409/500), registered first in the pipeline
- `Program.cs` — binds `JwtSettings`; registers `AppDbContext`, the three Infrastructure implementations (Scoped), MediatR scanning `Identity.Application`, `ValidationBehavior<,>` as an open-generic pipeline behavior; configures JWT Bearer **lazily** via `IOptions<JwtSettings>` (so `WebApplicationFactory` config overrides apply in tests — a naive `AddJwtBearer(opts => ...)` would capture config too early, which is exactly the kind of gotcha worth deciding at this first service rather than rediscovering per service); registers the three authorization policies (`RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail`); `/health` against SQL Server; `public partial class Program` for `WebApplicationFactory`
- Dev-only startup: `EnsureCreated()` + seed one admin account (no EF Core migrations for `IdentityDb` — a deliberate scope call, matching Product Service's later approach)

### 6. Tests, per layer (TDD)
- `Identity.Domain.Tests` — pure unit, `ApplicationUser` defaults, `RefreshToken` expiry
- `Identity.Application.Tests` — NSubstitute-mocked repositories/token service; handler happy paths + failure paths (duplicate email, wrong password, unknown/expired refresh token); validator rule tests; `ValidationBehaviorTests`
- `Identity.Infrastructure.Tests` — `TokenService` unit tests (claim shape, uniqueness); `RefreshTokenRepository` tests against real SQL Server via Testcontainers
- `Identity.Api.Tests` — `WebApplicationFactory<Program>` with `AppDbContext` swapped for EF Core InMemory and the two repositories swapped for in-memory fakes (`IdentityApiFactory`, `FakeUserRepository`, `FakeRefreshTokenRepository`, `JwtTokenHelper` for minting test JWTs) — this fixture pattern is what Product's `ProductApiFactory` and Cart's `CartApiFactory` later copy directly

## Verification

- `dotnet test ShopFlow.sln` — all Identity test projects green
- Manual smoke test via Docker: register → login → hit `/api/users/me` with the issued JWT → refresh → logout, confirming the refresh token is actually revoked (a second refresh with the same token fails)
- Confirm `RequireAdmin`/`RequireVendor`/`RequireVerifiedEmail` policies actually gate the endpoints they're applied to (401 without a token, 403 with the wrong role/claim)

## Critical files

- `Documentations/ShopFlow-ProjectSpec.md` (FR-01–FR-10, JWT claim contract)
- `Documentations/Architecture/Identity-Service.md` (full as-built architecture — the most detailed reference for this service)
- `Services/Identity/Identity.Api/Program.cs` (composition root — the template Product/Cart's `Program.cs` later follow)
- `Services/Identity/Identity.Infrastructure/Jwt/TokenService.cs` (the claim shape every downstream service validates against)
