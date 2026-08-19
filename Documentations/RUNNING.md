# Running ShopFlow

This guide covers everything needed to run the application locally with `dotnet run` — from first-time setup through running tests and exploring the API. Infrastructure (SQL Server, Redis, RabbitMQ) still runs in Docker; only the .NET services run as local processes.

> Want to run the whole application — services included — as Docker containers instead? See [DOCKER.md](./DOCKER.md).
>
> **Current state:** Identity Service (Phase 2) and Product Service (Phase 3) are implemented. The steps below cover both. Other services will be added as phases are completed.

---

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Latest | SQL Server, Redis, RabbitMQ containers |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | 10.0+ | Build and run the services |

Verify your installations:

```bash
docker --version
dotnet --version   # must be 10.x
```

---

## 1. One-Time Setup

### 1a. Clone and configure environment

The `.env` file at the repo root supplies secrets to Docker Compose. It is gitignored — copy the example and it is ready to go:

```bash
cd ShopFlow
cp .env.example .env
```

The default `.env.example` values work for local development as-is. Edit if you want different passwords:

```env
SQL_SA_PASSWORD=YourStrong@Password123   # SQL Server SA password
JWT_SECRET=your-super-secret-jwt-key-change-this-in-production
RABBITMQ_USER=guest
RABBITMQ_PASS=guest
```

> **Note:** The `appsettings.Development.json` in both `Identity.Api` and `Product.Api` already point to `localhost:1433` with `YourStrong@Password123`. If you change `SQL_SA_PASSWORD` in `.env`, update both files too.

### 1b. Build the solution

```bash
dotnet build ShopFlow.sln
```

This restores NuGet packages and verifies everything compiles before you run.

---

## 2. Start Infrastructure Containers

Identity requires SQL Server. Product additionally requires Redis. RabbitMQ is needed by later phases, but you can start all three now:

```bash
docker compose up -d sqlserver redis rabbitmq
```

Wait for all three to report **healthy** — SQL Server takes around 30 seconds on first start:

```bash
docker compose ps
```

Expected output (all three `healthy`):

```
NAME                    STATUS
shopflow-sqlserver      Up X seconds (healthy)
shopflow-redis          Up X seconds (healthy)
shopflow-rabbitmq       Up X seconds (healthy)
```

If a container shows `starting` instead of `healthy`, wait a few more seconds and re-run `docker compose ps`.

---

## 3. Run the Services

Each service runs as its own process. Open a separate terminal per service, or run one at a time.

### Identity Service

```bash
dotnet run --project Services/Identity/Identity.Api
```

On first run in Development, the application automatically:
1. Creates the `IdentityDb` database and schema via `EnsureCreated()`
2. Seeds an admin account from `appsettings.Development.json`

Expected console output:

```
info: Identity.Api[0] Admin account seeded: admin@shopflow.com
info: Microsoft.Hosting.Lifetime[14] Now listening on: http://localhost:5015
info: Microsoft.Hosting.Lifetime[0] Application started.
```

### Product Service

```bash
dotnet run --project Services/Product/Product.Api
```

On first run in Development, the application creates the `ProductDb` database and schema via `EnsureCreated()`. There is no seed data — see [Section 4](#pre-seeded-admin-account) for how to create a category and product once you're logged in.

Expected console output:

```
info: Microsoft.Hosting.Lifetime[14] Now listening on: http://localhost:5016
info: Microsoft.Hosting.Lifetime[0] Application started.
```

Each service is ready when you see `Application started`.

### Available URLs

| Service | URL | Purpose |
|---|---|---|
| Identity | `http://localhost:5015` | API base URL |
| Identity | `https://localhost:7043` | API base URL (HTTPS) |
| Identity | `http://localhost:5015/swagger` | Swagger UI |
| Identity | `http://localhost:5015/health` | Health check endpoint |
| Product | `http://localhost:5016` | API base URL |
| Product | `https://localhost:7044` | API base URL (HTTPS) |
| Product | `http://localhost:5016/swagger` | Swagger UI |
| Product | `http://localhost:5016/health` | Health check endpoint |

To use HTTPS locally you may need to trust the dev certificate:

```bash
dotnet dev-certs https --trust
```

---

## 4. Explore the API

### Swagger UI

Open `http://localhost:5015/swagger` (Identity) or `http://localhost:5016/swagger` (Product) in your browser. All endpoints are listed with request/response schemas and a **Try it out** button. The UI includes an **Authorize** button to paste a JWT for authenticated endpoints.

### Pre-seeded Admin Account

A ready-to-use admin account is seeded by the Identity Service on every Development startup:

| Field | Value |
|---|---|
| Email | `admin@shopflow.com` |
| Password | `Admin@12345` |
| Role | `Admin` |

This account is only known to the Identity Service, but the JWT it issues is trusted by every other service (they share `JwtSettings:Secret`), so log in once here and reuse the token everywhere.

### Quick API Walkthrough — Identity

**1. Register a new user**

```bash
curl -X POST http://localhost:5015/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com",
    "password": "Secret@123",
    "displayName": "Test User"
  }'
```

Response `201 Created`:

```json
{
  "accessToken": "<jwt>",
  "refreshToken": "<token>",
  "email": "user@example.com",
  "displayName": "Test User",
  "role": "Customer"
}
```

**2. Login**

```bash
curl -X POST http://localhost:5015/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@shopflow.com",
    "password": "Admin@12345"
  }'
```

Copy the `accessToken` from the response for the next step.

**3. Get current user profile** (requires JWT)

```bash
curl http://localhost:5015/api/users/me \
  -H "Authorization: Bearer <accessToken>"
```

**4. Refresh tokens**

```bash
curl -X POST http://localhost:5015/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{ "refreshToken": "<refreshToken>" }'
```

**5. Assign a role** (admin only)

```bash
curl -X POST http://localhost:5015/api/admin/users/<userId>/assign-role \
  -H "Authorization: Bearer <adminJwt>" \
  -H "Content-Type: application/json" \
  -d '{ "role": "Vendor" }'
```

**6. Reset a user's password** (admin only)

```bash
curl -X POST http://localhost:5015/api/admin/users/<userId>/reset-password \
  -H "Authorization: Bearer <adminJwt>" \
  -H "Content-Type: application/json" \
  -d '{ "newPassword": "NewStrong@Password1" }'
```

**7. Logout**

```bash
curl -X POST http://localhost:5015/api/auth/logout \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{ "refreshToken": "<refreshToken>" }'
```

### Quick API Walkthrough — Product

Categories are a separate, admin-managed taxonomy — products reference a `categoryId`, so create a category before creating a product. Product creation is Vendor-only and always attributes the product to the caller's own user ID (from the JWT), so assign yourself the `Vendor` role first via the Identity endpoint above if you're testing with a non-vendor account.

**1. List categories** (public)

```bash
curl http://localhost:5016/api/categories
```

**2. Create a category** (admin only)

```bash
curl -X POST http://localhost:5016/api/categories \
  -H "Authorization: Bearer <adminJwt>" \
  -H "Content-Type: application/json" \
  -d '{ "name": "Electronics" }'
```

**3. Create a product** (vendor only)

```bash
curl -X POST http://localhost:5016/api/products \
  -H "Authorization: Bearer <vendorJwt>" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Widget",
    "description": "A useful widget",
    "price": 9.99,
    "stockQuantity": 100,
    "categoryId": "<categoryId>"
  }'
```

**4. List products** (public)

```bash
curl http://localhost:5016/api/products
```

### All Endpoints — Identity

| Method | Path | Auth Required | Response |
|---|---|---|---|
| `POST` | `/api/auth/register` | None | 201 — `AuthResponse` |
| `POST` | `/api/auth/login` | None | 200 — `AuthResponse` |
| `POST` | `/api/auth/refresh` | None | 200 — `AuthResponse` |
| `POST` | `/api/auth/logout` | Bearer JWT | 204 |
| `GET` | `/api/users/me` | Bearer JWT | 200 — `UserProfileDto` |
| `GET` | `/api/admin/users?name=X` | Admin JWT | 200 — `UserProfileDto[]` |
| `POST` | `/api/admin/users/{id}/assign-role` | Admin JWT | 200 |
| `POST` | `/api/admin/users/{id}/reset-password` | Admin JWT | 200 |
| `GET` | `/health` | None | 200 — health status |

### All Endpoints — Product

| Method | Path | Auth Required | Response |
|---|---|---|---|
| `GET` | `/api/products` | None | 200 — `ProductDto[]` |
| `GET` | `/api/products/{id}` | None | 200 — `ProductDto` |
| `POST` | `/api/products` | Vendor JWT | 201 — `ProductDto` |
| `PUT` | `/api/products/{id}` | Vendor JWT | 200 — `ProductDto` |
| `DELETE` | `/api/products/{id}` | Vendor JWT | 204 |
| `GET` | `/api/vendors/{id}/products` | Vendor JWT | 200 — `ProductDto[]` |
| `GET` | `/api/categories` | None | 200 — `CategoryDto[]` |
| `POST` | `/api/categories` | Admin JWT | 201 — `CategoryDto` |
| `GET` | `/health` | None | 200 — health status |

---

## 5. Run Tests

```bash
dotnet test ShopFlow.sln
```

### What each test project does

| Project | Type | Requires Docker? |
|---|---|---|
| `Identity.Domain.Tests` | Pure unit — no I/O | No |
| `Identity.Application.Tests` | Unit with NSubstitute mocks | No |
| `Identity.Infrastructure.Tests` | Real SQL Server via Testcontainers | **Yes** |
| `Identity.Api.Tests` | Integration with WebApplicationFactory + in-memory fakes | No |
| `Product.Domain.Tests` | Pure unit — no I/O | No |
| `Product.Application.Tests` | Unit with NSubstitute mocks | No |
| `Product.Infrastructure.Tests` | Real SQL Server via Testcontainers | **Yes** |
| `Product.Api.Tests` | Integration with WebApplicationFactory + in-memory fakes | No |

> **Testcontainers note:** The `*.Infrastructure.Tests` projects each spin up their own SQL Server container automatically via Docker. Docker Desktop must be running. Containers are created and torn down per test run — no manual setup needed.

### Run a single test project

```bash
dotnet test Services/Identity/Identity.Domain.Tests
dotnet test Services/Identity/Identity.Application.Tests
dotnet test Services/Identity/Identity.Infrastructure.Tests
dotnet test Services/Identity/Identity.Api.Tests

dotnet test Services/Product/Product.Domain.Tests
dotnet test Services/Product/Product.Application.Tests
dotnet test Services/Product/Product.Infrastructure.Tests
dotnet test Services/Product/Product.Api.Tests
```

### Run with verbose output

```bash
dotnet test ShopFlow.sln --logger "console;verbosity=normal"
```

### Run a specific test

```bash
dotnet test Services/Identity/Identity.Application.Tests \
  --filter "FullyQualifiedName~RegisterUserCommandHandlerTests"
```

---

## 6. Stop Everything

```bash
# Stop each dotnet run process: Ctrl+C

# Stop and remove containers (data is preserved in Docker volumes)
docker compose down

# Stop and remove containers AND all data volumes (full reset)
docker compose down -v
```

---

## 7. Troubleshooting

### SQL Server container not becoming healthy

SQL Server 2022 needs around 30 seconds to initialize on first start. Check its logs:

```bash
docker compose logs sqlserver
```

Look for `SQL Server is now ready for client connections`. If it never appears, check that `SQL_SA_PASSWORD` in your `.env` meets SQL Server's complexity requirements (uppercase, lowercase, digit, symbol, 8+ chars).

### "Cannot connect to SQL Server" on startup

Verify the container is healthy before starting the API:

```bash
docker compose ps
```

If the container is healthy but the API still fails, confirm the password in `appsettings.Development.json` (Identity and/or Product) matches `SQL_SA_PASSWORD` in your `.env`:

```json
"ConnectionStrings": {
  "Default": "Server=localhost,1433;Database=IdentityDb;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=True"
}
```

### Port already in use

Either stop the conflicting process, or override the port for whichever service is affected:

```bash
ASPNETCORE_URLS="http://localhost:5099" dotnet run --project Services/Identity/Identity.Api
ASPNETCORE_URLS="http://localhost:5098" dotnet run --project Services/Product/Product.Api
```

### Testcontainers tests fail or are skipped

Docker Desktop must be running. Verify:

```bash
docker info
```

### HTTPS certificate not trusted

```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Admin seed not appearing

The admin seed only runs in the Identity Service's `Development` environment. Check that `ASPNETCORE_ENVIRONMENT` is set to `Development` (it is by default via `launchSettings.json`). If the admin email already exists in the database, the seed is skipped without error.

---

## 8. Port Reference

| Service | URL / Address |
|---|---|
| Identity API (HTTP) | `http://localhost:5015` |
| Identity API (HTTPS) | `https://localhost:7043` |
| Identity Swagger UI | `http://localhost:5015/swagger` |
| Identity health check | `http://localhost:5015/health` |
| Product API (HTTP) | `http://localhost:5016` |
| Product API (HTTPS) | `https://localhost:7044` |
| Product Swagger UI | `http://localhost:5016/swagger` |
| Product health check | `http://localhost:5016/health` |
| SQL Server | `localhost:1433` |
| Redis | `localhost:6379` |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ Management UI | `http://localhost:15672` (guest / guest) |

> Note: these are the local `dotnet run` ports (from each service's `launchSettings.json`). Running the same services via Docker Compose instead maps them to `5001` (Identity) and `5002` (Product) — see [DOCKER.md](./DOCKER.md).

---

## 9. Environment Configuration Reference

| Key | Location | Default (Development) | Purpose |
|---|---|---|---|
| `ConnectionStrings:Default` | `Identity.Api/appsettings.Development.json` | `Server=localhost,1433;Database=IdentityDb;...` | SQL Server connection |
| `JwtSettings:Secret` | `Identity.Api/appsettings.Development.json` | `shopflow-dev-jwt-secret-key-32-chars-min` | JWT signing key |
| `JwtSettings:Issuer` | `Identity.Api/appsettings.Development.json` | `ShopFlow` | JWT issuer claim |
| `JwtSettings:Audience` | `Identity.Api/appsettings.Development.json` | `ShopFlow` | JWT audience claim |
| `JwtSettings:ExpiryMinutes` | `Identity.Api/appsettings.Development.json` | `60` | Access token lifetime |
| `AdminSeed:Email` | `Identity.Api/appsettings.Development.json` | `admin@shopflow.com` | Seeded admin email |
| `AdminSeed:Password` | `Identity.Api/appsettings.Development.json` | `Admin@12345` | Seeded admin password |
| `ConnectionStrings:Default` | `Product.Api/appsettings.Development.json` | `Server=localhost,1433;Database=ProductDb;...` | SQL Server connection |
| `ConnectionStrings:Redis` | `Product.Api/appsettings.Development.json` | `localhost:6379` | Redis connection (product catalog cache) |
| `JwtSettings:Secret` | `Product.Api/appsettings.Development.json` | `shopflow-dev-jwt-secret-key-32-chars-min` | JWT signing key — must match Identity's |
| `SQL_SA_PASSWORD` | `.env` | `YourStrong@Password123` | Docker SQL Server SA password |
| `JWT_SECRET` | `.env` | _(change in production)_ | Docker JWT secret override |
