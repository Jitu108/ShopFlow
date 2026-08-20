# ShopFlow

A mid-complexity, full-stack multi-vendor e-commerce marketplace built as a suite of independently deployable .NET microservices behind an Ocelot API gateway, with an Angular SPA as the frontend.

Vendors register, list products, and track orders. Customers browse the catalog, manage a cart, and place orders. Orders trigger async workflows across services via RabbitMQ.

---

## Tech Stack

| Layer | Technology |
| --- | --- |
| Frontend | Angular 17+, Angular Material, NgRx |
| API Gateway | Ocelot (.NET 10) |
| Microservices | ASP.NET Core 10 Web API |
| Authentication | ASP.NET Core Identity + JWT Bearer + Refresh Tokens |
| Authorization | Policy-based (`RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail`) |
| ORM | Entity Framework Core 10 |
| Database | SQL Server 2022 (one instance, three databases) |
| Message Broker | RabbitMQ via MassTransit |
| Cache | Redis via StackExchange.Redis |
| Architecture | Clean Architecture — Domain / Application / Infrastructure / API |
| Patterns | CQRS, MediatR, Repository, Cache-Aside |
| Containerisation | Docker, Docker Compose |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers |

---

## Architecture

```text
┌─────────────────────────────────────┐
│            Angular SPA              │
│  CustomerModule   │   VendorModule  │
└────────────┬────────────────────────┘
             │ HTTP + JWT
             ▼
┌────────────────────────────────────────┐
│         Ocelot API Gateway             │
│  Routing │ Rate Limiting │ JWT Auth    │
└──┬────┬──────┬────────┬────────┬───────┘
   │    │      │        │        │
   ▼    ▼      ▼        ▼        ▼
Identity  Product   Order    Cart    Notification
Service   Service   Service  Service  Service
   │        │         │        │
   ▼        ▼         ▼        │
Identity  Product   Order      │
  DB        DB        DB       │
                      │        │
                      ▼        ▼
                   RabbitMQ Exchange
              (order.placed, order.shipped)
                      │
            ┌─────────┴──────────┐
            ▼                    ▼
    Notification Service    Cart Service
    (sends emails)          (clears cart)

               Redis
           ┌────┴────┐
        Cart Hash  Product Cache
```

---

## Microservices

| Service | Responsibility | Database |
| --- | --- | --- |
| **Identity** | Registration, login, JWT, refresh tokens, role management | `IdentityDb` |
| **Product** | Vendor catalog — CRUD listings, customer browse, inventory | `ProductDb` |
| **Order** | Checkout, order lifecycle, order history | `OrderDb` |
| **Cart** | Session-scoped basket — Redis only, no SQL | Redis |
| **Notification** | Consume order events, send transactional emails | None (stateless) |

---

## User Roles

| Role | Capabilities |
| --- | --- |
| `Customer` | Browse products, manage cart, place & track orders |
| `Vendor` | Create/update/delete own listings, view order demand |
| `Admin` | Manage users, approve vendors, view platform analytics |

---

## Identity Service

The first service to be fully implemented. All four layers are complete and covered by tests.

### Endpoints

| Method | Path | Auth | Response |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | None | 201 — `AuthResponse` |
| `POST` | `/api/auth/login` | None | 200 — `AuthResponse` |
| `POST` | `/api/auth/refresh` | None | 200 — `AuthResponse` |
| `POST` | `/api/auth/logout` | `[Authorize]` | 204 |
| `GET` | `/api/users/me` | `[Authorize]` | 200 — `UserProfileDto` |
| `POST` | `/api/admin/users/{id}/assign-role` | `[RequireAdmin]` | 200 |

### Test coverage

| Project | Type | Tests |
| --- | --- | --- |
| `Identity.Domain.Tests` | Pure unit | Entity defaults, refresh token expiry |
| `Identity.Application.Tests` | Unit (NSubstitute) | All command handlers, validators, pipeline behavior |
| `Identity.Infrastructure.Tests` | Unit + Testcontainers | `TokenService` JWT claims, `RefreshTokenRepository` against real SQL Server |
| `Identity.Api.Tests` | WebApplicationFactory | All endpoints — happy paths, auth failures, validation errors |

The service is fully runnable both standalone (`dotnet run`) and via Docker Compose — SQL schema is created automatically via `EnsureCreated`, with an admin account seeded on first run. See [Documentations/RUNNING.md](Documentations/RUNNING.md) for full run instructions and [Documentations/Architecture/Identity-Service.md](Documentations/Architecture/Identity-Service.md) for the detailed architecture writeup.

```bash
docker compose up -d sqlserver identity-service
curl http://localhost:5001/health
```

---

## Product Service

The second service to be fully implemented, following the same Clean Architecture + CQRS + TDD pattern as Identity. Reads are served cache-aside through Redis.

### Endpoints

| Method | Path | Auth | Response |
| --- | --- | --- | --- |
| `GET` | `/api/products` | None | 200 — `ProductDto[]` (cached) |
| `GET` | `/api/products/{id}` | None | 200 — `ProductDto` (cached) |
| `POST` | `/api/products` | `[RequireVendor]` | 201 — `ProductDto` |
| `PUT` | `/api/products/{id}` | `[RequireVendor]` (owner only) | 200 — `ProductDto` |
| `DELETE` | `/api/products/{id}` | `[RequireVendor]` (owner only) | 204 |
| `GET` | `/api/vendors/{id}/products` | `[RequireVendor]` | 200 — `ProductDto[]` |

### Test coverage

| Project | Type | Tests |
| --- | --- | --- |
| `Product.Domain.Tests` | Pure unit | Entity invariants (price/stock, name) |
| `Product.Application.Tests` | Unit (NSubstitute) | Command/query handlers, validators, pipeline behaviors, cache-aside logic |
| `Product.Infrastructure.Tests` | Unit + Testcontainers | `ProductRepository` against real SQL Server, `RedisCacheService` against real Redis |
| `Product.Api.Tests` | WebApplicationFactory | All endpoints — public reads, vendor-only writes, ownership checks, validation errors |

Product tokens are validated with the same JWT secret/issuer/audience as Identity, so a JWT issued by Identity works here unchanged.

```bash
docker compose up -d sqlserver redis product-service
curl http://localhost:5002/health
```

---

## Getting Started

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (for Angular UI)

### 1. Clone and configure environment

```bash
git clone <repo-url>
cd ShopFlow
cp .env.example .env
# Open .env and fill in SQL_SA_PASSWORD, JWT_SECRET, RABBITMQ_USER, RABBITMQ_PASS
```

### 2. Start infrastructure

```bash
docker compose up -d sqlserver redis rabbitmq
docker compose ps   # wait until all three show "healthy"
```

### 3. Build the solution

```bash
dotnet build ShopFlow.sln
```

### 4. Run tests

```bash
dotnet test ShopFlow.sln
```

---

## Project Structure

```text
ShopFlow/
├── Services/
│   ├── Identity/                Phase 2 — ✅ complete
│   │   ├── Dockerfile
│   │   ├── Identity.Domain/
│   │   ├── Identity.Application/
│   │   ├── Identity.Infrastructure/
│   │   ├── Identity.Api/
│   │   ├── Identity.Domain.Tests/
│   │   ├── Identity.Application.Tests/
│   │   ├── Identity.Infrastructure.Tests/
│   │   └── Identity.Api.Tests/
│   ├── Product/                 Phase 3 — ✅ complete
│   │   ├── Dockerfile
│   │   ├── Product.Domain/
│   │   ├── Product.Application/
│   │   ├── Product.Infrastructure/
│   │   ├── Product.Api/
│   │   ├── Product.Domain.Tests/
│   │   ├── Product.Application.Tests/
│   │   ├── Product.Infrastructure.Tests/
│   │   └── Product.Api.Tests/
│   ├── Order/                  Phase 5 — pending
│   ├── Cart/                   Phase 4 — complete
│   └── Notification/           Phase 5 — pending
├── Gateway/                    Phase 6 — pending
├── ClientApp/                  Phase 7 — pending (Angular)
├── Shared/                     Shared event contracts (pending)
├── Documentations/
│   ├── Phases/
│   │   ├── Phase1.md
│   │   ├── Phase2.md
│   │   └── Phase3.md
│   ├── Architecture/
│   │   ├── Identity-Service.md
│   │   └── Product-Service.md
│   ├── STATUS.md
│   ├── RUNNING.md
│   ├── DOCKER.md
│   ├── ShopFlow-Approach.md
│   ├── ShopFlow-ProjectSpec.md
│   └── ShopFlow-TDD-Guide.md
├── docker-compose.yml
├── .env.example
├── .gitignore
└── ShopFlow.sln
```

---

## Infrastructure Ports

| Service | URL |
| --- | --- |
| API Gateway | `http://localhost:5000` |
| Identity Service | `http://localhost:5001` |
| Product Service | `http://localhost:5002` |
| Order Service | `http://localhost:5003` |
| Cart Service | `http://localhost:5004` |
| Angular UI | `http://localhost:4200` |
| RabbitMQ Management | `http://localhost:15672` |
| SQL Server | `localhost:1433` |

> These are the Docker Compose port mappings. Running the Identity Service directly via `dotnet run` (outside Docker) instead uses `http://localhost:5015`, per its `launchSettings.json`.
| Redis | `localhost:6379` |

---

## Development Approach

This project is built using **Test-Driven Development (TDD)** — tests are written before implementation, following an inside-out layer order:

```text
Domain Tests → Application Tests → Infrastructure Tests → API Tests
```

Each microservice follows **Clean Architecture** with strict dependency direction:

```text
API → Infrastructure → Application → Domain
```

See [Documentations/ShopFlow-TDD-Guide.md](Documentations/ShopFlow-TDD-Guide.md) for the full TDD strategy.

---

## Build Phases

| Phase | Scope | Status |
| --- | --- | --- |
| Phase 1 | Infrastructure — Docker Compose, folder structure | ✅ Complete |
| Phase 2 | Identity Service | ✅ Complete |
| Phase 3 | Product Service | ✅ Complete |
| Phase 4 | Cart Service | ✅ Complete |
| Phase 5 | Order + Notification Services | ⏳ Pending |
| Phase 6 | API Gateway (Ocelot) | ⏳ Pending |
| Phase 7 | Angular UI | ⏳ Pending |

---

## Documentation

| Document | Description |
| --- | --- |
| [STATUS.md](Documentations/STATUS.md) | Live dashboard — phase status, test totals, immediate next steps, known gaps |
| [ShopFlow-ProjectSpec.md](Documentations/ShopFlow-ProjectSpec.md) | The spec — requirements, tech stack, architecture, scope limits |
| [ShopFlow-Approach.md](Documentations/ShopFlow-Approach.md) | Build order and key decisions |
| [ShopFlow-TDD-Guide.md](Documentations/ShopFlow-TDD-Guide.md) | TDD strategy per layer |
| [Architecture/Identity-Service.md](Documentations/Architecture/Identity-Service.md) | Detailed as-built architecture of the Identity Service's eight projects |
| [Architecture/Product-Service.md](Documentations/Architecture/Product-Service.md) | Detailed as-built architecture of the Product Service's eight projects |
| [Phases/Phase1.md](Documentations/Phases/Phase1.md) | Phase 1 implementation log |
| [Phases/Phase2.md](Documentations/Phases/Phase2.md) | Phase 2 implementation log |
| [Phases/Phase3.md](Documentations/Phases/Phase3.md) | Phase 3 implementation log |
| [RUNNING.md](Documentations/RUNNING.md) | Full local setup and run guide — `dotnet run` against Dockerized infra |
| [DOCKER.md](Documentations/DOCKER.md) | Running the entire app — services included — via Docker Compose |
