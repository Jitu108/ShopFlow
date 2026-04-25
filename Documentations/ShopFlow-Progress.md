# ShopFlow — Comprehensive Project Documentation

> Living document. Updated as each phase is implemented.  
> Covers every functional requirement, non-functional requirement, architectural decision, and implementation detail.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Functional Requirements](#2-functional-requirements)
3. [Non-Functional Requirements](#3-non-functional-requirements)
4. [Technology Stack](#4-technology-stack)
5. [System Architecture](#5-system-architecture)
6. [Microservices — Detailed Specification](#6-microservices--detailed-specification)
   - [Identity Service](#61-identity-service)
   - [Product Service](#62-product-service)
   - [Order Service](#63-order-service)
   - [Cart Service](#64-cart-service)
   - [Notification Service](#65-notification-service)
7. [API Gateway](#7-api-gateway)
8. [Message Broker — RabbitMQ](#8-message-broker--rabbitmq)
9. [Caching Strategy — Redis](#9-caching-strategy--redis)
10. [Database Design](#10-database-design)
11. [Clean Architecture Pattern](#11-clean-architecture-pattern)
12. [CQRS & MediatR](#12-cqrs--mediatr)
13. [Authorization & Security](#13-authorization--security)
14. [Angular UI](#14-angular-ui)
15. [Docker & Infrastructure](#15-docker--infrastructure)
16. [TDD Approach](#16-tdd-approach)
17. [Implementation Progress](#17-implementation-progress)
    - [Phase 1 — Infrastructure Foundation](#phase-1--infrastructure-foundation)
    - [Phase 2 — Identity Service](#phase-2--identity-service)
18. [Project Dependency Wiring](#18-project-dependency-wiring)
19. [Issues Found and Fixed](#19-issues-found-and-fixed)
20. [Build Status](#20-build-status)
21. [Repository Structure](#21-repository-structure)
22. [Scope Limits](#22-scope-limits)
23. [Stretch Goals](#23-stretch-goals)
24. [What Comes Next](#24-what-comes-next)

---

## 1. Project Overview

ShopFlow is a multi-vendor marketplace platform where vendors register, manage product listings, and track orders. Customers browse the catalog, manage a shopping cart, and place orders. The platform is built as a suite of independently deployable .NET microservices behind an Ocelot API gateway, with an Angular SPA as the frontend.

Orders trigger async workflows — on placement, an event is published to RabbitMQ and consumed by the Notification Service (sends confirmation email) and the Cart Service (clears the cart). This event-driven design keeps services decoupled.

**Complexity level:** Mid-complexity — microservices, CQRS, async messaging, JWT auth, Redis caching — but intentionally scoped to avoid enterprise-scale concerns (no event sourcing, no real payment, no CI/CD pipeline).

---

## 2. Functional Requirements

### 2.1 User Roles

| Role | Description |
| --- | --- |
| `Customer` | Registered user who browses, carts, and purchases products |
| `Vendor` | Registered user who manages their own product listings |
| `Admin` | Platform administrator with full user and order oversight |

### 2.2 Authentication & Identity

| # | Requirement |
| --- | --- |
| FR-01 | Users can register with email, password, and display name |
| FR-02 | Default role on registration is `Customer` |
| FR-03 | Users can log in and receive a JWT access token and a refresh token |
| FR-04 | Access tokens expire; refresh tokens allow silent renewal without re-login |
| FR-05 | Refresh tokens are rotated on each use (old token invalidated) |
| FR-06 | Users can log out (refresh token is invalidated in DB) |
| FR-07 | Users can view and update their own profile (`/api/users/me`) |
| FR-08 | Admins can assign roles to users (`Customer → Vendor`, `Customer → Admin`) |
| FR-09 | JWT carries claims: `userId`, `email`, `role`, `emailVerified` |
| FR-10 | Email verification status is tracked per user (`IsEmailVerified`) |

### 2.3 Product Catalog

| # | Requirement |
| --- | --- |
| FR-11 | Any visitor (anonymous) can browse the product catalog |
| FR-12 | Any visitor can view individual product detail |
| FR-13 | Vendors can create new product listings |
| FR-14 | Vendors can update only their own listings |
| FR-15 | Vendors can delete only their own listings |
| FR-16 | Vendors can view all their own listings |
| FR-17 | Products belong to a category (seeded at startup) |
| FR-18 | Products track stock quantity and active/inactive status |
| FR-19 | Product reads are served from Redis cache (cache-aside); writes invalidate cache |

### 2.4 Shopping Cart

| # | Requirement |
| --- | --- |
| FR-20 | Authenticated users can view their cart |
| FR-21 | Authenticated users can add items to their cart |
| FR-22 | Authenticated users can update item quantity in their cart |
| FR-23 | Authenticated users can remove individual items from their cart |
| FR-24 | Authenticated users can clear their entire cart |
| FR-25 | Cart is automatically cleared when an order is successfully placed |
| FR-26 | Cart persists for 7 days (sliding TTL); resets on each interaction |

### 2.5 Orders

| # | Requirement |
| --- | --- |
| FR-27 | Only users with a verified email can place an order |
| FR-28 | Placing an order creates an `Order` with status `Pending` and snapshots product name and price |
| FR-29 | Orders go through a lifecycle: `Pending → Confirmed → Shipped → Delivered` |
| FR-30 | Order confirmation is stubbed (no real payment); a PUT endpoint simulates it |
| FR-31 | Customers can view their own order history |
| FR-32 | Customers can view individual order detail |
| FR-33 | Admins can view all orders platform-wide |
| FR-34 | On confirmation, an `OrderPlacedEvent` is published to RabbitMQ |
| FR-35 | On shipment, an `OrderShippedEvent` is published to RabbitMQ |

### 2.6 Notifications

| # | Requirement |
| --- | --- |
| FR-36 | Customers receive an "Order Confirmation" email when an order is placed |
| FR-37 | Customers receive a "Your order is on the way" email when an order is shipped |
| FR-38 | Email delivery is fire-and-forget (no delivery receipt or bounce handling) |
| FR-39 | Failed email deliveries are retried up to 3 times with exponential backoff |

### 2.7 Admin

| # | Requirement |
| --- | --- |
| FR-40 | Admins can manage users (view, assign roles) |
| FR-41 | Admins can approve vendor applications (role assignment) |
| FR-42 | Admins can view all orders platform-wide |

---

## 3. Non-Functional Requirements

### 3.1 Security

| # | Requirement |
| --- | --- |
| NFR-01 | All sensitive endpoints are protected with JWT Bearer authentication |
| NFR-02 | Authorization is enforced at two levels: Ocelot gateway (route-level) and individual service controllers (defence-in-depth) |
| NFR-03 | JWT secrets are never hardcoded — injected via environment variables / Docker secrets |
| NFR-04 | Refresh tokens are stored hashed in the DB with expiry and rotation to prevent reuse after logout |
| NFR-05 | JWT tokens are stored in memory on the frontend (not `localStorage`) to prevent XSS token theft |
| NFR-06 | SQL injection is prevented through EF Core parameterised queries — raw SQL is not used |
| NFR-07 | Passwords are hashed by ASP.NET Core Identity (PBKDF2 by default) |

### 3.2 Performance & Caching

| # | Requirement |
| --- | --- |
| NFR-08 | Product catalog reads use a cache-aside pattern with Redis (sliding 10-minute expiry) |
| NFR-09 | Individual product reads are cached per ID (sliding 15-minute expiry) |
| NFR-10 | Cart operations hit Redis directly — no SQL involved |
| NFR-11 | Redis keys are namespaced (`cart:{userId}`, `product:{id}`, `product:catalog`) to prevent collision |

### 3.3 Scalability & Availability

| # | Requirement |
| --- | --- |
| NFR-12 | All services are independently deployable Docker containers |
| NFR-13 | Services declare health check endpoints (`/health`) for container orchestration |
| NFR-14 | The API gateway only routes traffic to healthy downstream services (`condition: service_healthy`) |
| NFR-15 | RabbitMQ consumers use MassTransit retry policies to handle transient failures |
| NFR-16 | Each service owns its own database schema (separate `DbContext`, separate migrations) — no shared DB coupling |

### 3.4 Maintainability

| # | Requirement |
| --- | --- |
| NFR-17 | Each service follows Clean Architecture (Domain / Application / Infrastructure / API) |
| NFR-18 | CQRS separates read and write paths — commands and queries never share handlers |
| NFR-19 | MediatR pipeline behaviors handle cross-cutting concerns (validation, logging) |
| NFR-20 | Repository pattern abstracts data access — handlers depend on interfaces, not EF Core directly |
| NFR-21 | Shared event contracts (`OrderPlacedEvent`, `OrderShippedEvent`) live in a separate `Shared` class library — never duplicated |

### 3.5 Testability

| # | Requirement |
| --- | --- |
| NFR-22 | All code is developed using TDD (Red → Green → Refactor) |
| NFR-23 | Domain layer is pure C# with no external dependencies — fully unit testable |
| NFR-24 | Application layer handlers depend only on interfaces — fully mockable with NSubstitute |
| NFR-25 | Infrastructure tests use Testcontainers to run against real SQL Server, Redis, and RabbitMQ |
| NFR-26 | API tests use `WebApplicationFactory` to test HTTP endpoints end-to-end within the service boundary |
| NFR-27 | Each source project has a paired test project; test projects never cross layer boundaries |

### 3.6 Rate Limiting

| # | Requirement |
| --- | --- |
| NFR-28 | Global rate limiting is enforced at the API gateway: 100 requests per minute per client |

### 3.7 Configuration

| # | Requirement |
| --- | --- |
| NFR-29 | All configuration uses `appsettings.json` + environment variables + Docker secrets — no hardcoded values |
| NFR-30 | The `.env` file is gitignored; `.env.example` is committed as a template |

---

## 4. Technology Stack

| Layer | Technology | Version |
| --- | --- | --- |
| Frontend | Angular + Angular Material + NgRx | 17+ |
| API Gateway | Ocelot (.NET) | Latest |
| Microservices | ASP.NET Core Web API | .NET 10 |
| Authentication | ASP.NET Core Identity + JWT Bearer + Refresh Tokens | .NET 10 |
| Authorization | Policy-based (`RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail`) | — |
| ORM | Entity Framework Core | 10 |
| Database | SQL Server | 2022 |
| Message Broker | RabbitMQ via MassTransit | 3-management |
| Cache | Redis via StackExchange.Redis | 7-alpine |
| Architecture | Clean Architecture — Domain / Application / Infrastructure / API | — |
| Patterns | CQRS, MediatR, Repository, DI, Cache-Aside | — |
| Validation | FluentValidation | — |
| Logging | Serilog | — |
| Email | MailKit / SendGrid | — |
| Containerisation | Docker, Docker Compose | 3.9 |
| Health Checks | AspNetCore.Diagnostics.HealthChecks | — |
| Unit Testing | xUnit + FluentAssertions + NSubstitute | — |
| Integration Testing | Testcontainers (.MsSql, .Redis, .RabbitMq) | — |
| API Testing | Microsoft.AspNetCore.Mvc.Testing | — |

---

## 5. System Architecture

```text
┌─────────────────────────────────────┐
│            Angular SPA              │
│  CustomerModule   │   VendorModule  │
└────────────┬────────────────────────┘
             │ HTTP + JWT
             ▼
┌────────────────────────────────────────┐
│         Ocelot API Gateway             │
│  Routing │ Rate Limiting │ Auth Middleware │
└──┬────┬──────┬────────┬────────┬───────┘
   │    │      │        │        │
   ▼    ▼      ▼        ▼        ▼
Identity  Product   Order    Cart    Notification
Service   Service   Service  Service  Service
   │        │         │        │         │
   ▼        ▼         ▼        │         │
Identity  Product   Order      │         │
  DB        DB        DB       │         │
                      │        │         │
                      ▼        ▼         │
                   RabbitMQ Exchange     │
                  (order.placed,         │
                   order.shipped) ───────┘
                      │
            ┌─────────┴──────────┐
            ▼                    ▼
    Notification Service    Cart Service
    (sends emails)          (clears cart)

            Redis
        ┌────┴────┐
     Cart Hash  Product Cache
```

### Service Port Map (Docker Compose)

| Service | Internal port | Host port |
| --- | --- | --- |
| API Gateway | 80 | 5000 |
| Identity Service | 80 | 5001 |
| Product Service | 80 | 5002 |
| Order Service | 80 | 5003 |
| Cart Service | 80 | 5004 |
| SQL Server | 1433 | 1433 |
| Redis | 6379 | 6379 |
| RabbitMQ AMQP | 5672 | 5672 |
| RabbitMQ Management UI | 15672 | 15672 |
| Angular UI | 80 | 4200 |

---

## 6. Microservices — Detailed Specification

### 6.1 Identity Service

**Responsibility:** User registration, login, JWT issuance, role management, refresh tokens.

#### Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | None | Register a new user |
| `POST` | `/api/auth/login` | None | Login, receive JWT + refresh token |
| `POST` | `/api/auth/refresh` | None | Rotate refresh token, get new JWT |
| `POST` | `/api/auth/logout` | `[Authorize]` | Invalidate refresh token |
| `GET` | `/api/users/me` | `[Authorize]` | Get current user profile |
| `PUT` | `/api/users/me` | `[Authorize]` | Update current user profile |
| `POST` | `/api/admin/users/{id}/assign-role` | `[RequireAdmin]` | Assign role to a user |

#### Domain Model

```text
ApplicationUser : IdentityUser
  ├── Id (Guid)
  ├── Email (string)
  ├── DisplayName (string)
  ├── Role: Customer | Vendor | Admin
  ├── IsEmailVerified (bool)
  └── RefreshTokens[ ] → RefreshToken (one-to-many)

RefreshToken
  ├── Id (Guid)
  ├── Token (string — opaque random bytes)
  ├── ExpiresAt (DateTime)
  ├── CreatedAt (DateTime)
  └── UserId (FK → ApplicationUser)
```

#### Commands & Queries

| Type | Name | Handler Responsibility |
| --- | --- | --- |
| Command | `RegisterUserCommand` | Create user via `UserManager`, assign `Customer` role |
| Command | `LoginCommand` | Validate credentials, issue JWT + refresh token |
| Command | `RefreshTokenCommand` | Validate token, rotate it, issue new JWT |
| Command | `LogoutCommand` | Invalidate refresh token in DB |
| Command | `AssignRoleCommand` | Admin-only role assignment |
| Query | `GetCurrentUserQuery` | Return profile from claims |

#### Authorization Policies (platform-wide)

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireVendor", p => p.RequireRole("Vendor"));
    options.AddPolicy("RequireAdmin",  p => p.RequireRole("Admin"));
    options.AddPolicy("RequireVerifiedEmail",
        p => p.RequireClaim("emailVerified", "true"));
});
```

#### Database: `IdentityDb`

```text
AspNetUsers           (ASP.NET Identity scaffolded)
AspNetRoles
AspNetUserRoles
RefreshTokens         (Id, UserId FK, Token, ExpiresAt, CreatedAt)
```

#### NuGet Packages

| Package | Purpose |
| --- | --- |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | ASP.NET Identity + EF Core |
| `Microsoft.EntityFrameworkCore.SqlServer` | SQL Server provider |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT middleware |
| `System.IdentityModel.Tokens.Jwt` | JWT generation |
| `MediatR` | CQRS pipeline |
| `FluentValidation.AspNetCore` | Request validation |
| `Serilog.AspNetCore` | Structured logging |
| `AspNetCore.Diagnostics.HealthChecks` + `.SqlServer` | Health check |

---

### 6.2 Product Service

**Responsibility:** Vendor catalog — create, update, delete listings; customer browse; inventory tracking.

#### Product Service Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/products` | None | List all products (cached) |
| `GET` | `/api/products/{id}` | None | Get product by ID (cached) |
| `POST` | `/api/products` | `[RequireVendor]` | Create a new product |
| `PUT` | `/api/products/{id}` | `[RequireVendor]` | Update own product |
| `DELETE` | `/api/products/{id}` | `[RequireVendor]` | Delete own product |
| `GET` | `/api/vendors/{id}/products` | `[RequireVendor]` | List vendor's own products |

#### Product Domain Model

```text
Product
  ├── Id (Guid)
  ├── VendorId (Guid FK → ApplicationUser)
  ├── Name (string)
  ├── Description (string)
  ├── Price (decimal)
  ├── StockQuantity (int)
  ├── IsActive (bool)
  ├── CreatedAt (DateTime)
  ├── UpdatedAt (DateTime)
  └── Category → Category (many-to-one)

Category
  ├── Id (int)
  ├── Name (string)
  └── Products[ ]
```

#### Product Commands & Queries

| Type | Name | Description |
| --- | --- | --- |
| Command | `CreateProductCommand` | Create new product listing |
| Command | `UpdateProductCommand` | Update existing product |
| Command | `DeleteProductCommand` | Remove product |
| Query | `GetProductByIdQuery` | Redis → SQL fallback |
| Query | `GetProductListQuery` | Redis → SQL fallback |
| Query | `GetVendorProductsQuery` | All products for a vendor |

#### Cache Keys

| Key | Expiry | Invalidated on |
| --- | --- | --- |
| `product:{id}` | 15 min sliding | Update / Delete |
| `product:catalog` | 10 min sliding | Any Create / Update / Delete |

#### Database: `ProductDb`

```text
Categories   (Id, Name)
Products     (Id, VendorId, CategoryId FK, Name, Description,
              Price, StockQuantity, IsActive, CreatedAt, UpdatedAt)
```

---

### 6.3 Order Service

**Responsibility:** Checkout, order lifecycle (`Pending → Confirmed → Shipped → Delivered`), order history.

#### Order Service Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/orders` | `[RequireVerifiedEmail]` | Place a new order |
| `GET` | `/api/orders` | `[Authorize]` | Get current user's orders |
| `GET` | `/api/orders/{id}` | `[Authorize]` | Get order detail |
| `PUT` | `/api/orders/{id}/confirm` | `[Authorize]` | Confirm order (payment stub) |
| `GET` | `/api/admin/orders` | `[RequireAdmin]` | Get all platform orders |

#### Order Domain Model

```text
Order (Aggregate Root)
  ├── Id (Guid)
  ├── CustomerId (Guid FK)
  ├── Status: Pending | Confirmed | Shipped | Delivered | Cancelled
  ├── TotalAmount (decimal)
  ├── CreatedAt (DateTime)
  ├── UpdatedAt (DateTime)
  └── OrderItems[ ] → OrderItem (one-to-many, cascade delete)

OrderItem
  ├── Id (Guid)
  ├── OrderId (Guid FK)
  ├── ProductId (Guid)
  ├── ProductName (string — snapshot at order time)
  ├── UnitPrice (decimal — snapshot at order time)
  └── Quantity (int)
```

#### Events Published

```csharp
public record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    List<OrderItemDto> Items,
    decimal Total,
    DateTime PlacedAt
);

public record OrderShippedEvent(
    Guid OrderId,
    string TrackingNumber,
    DateTime ShippedAt
);
```

#### Database: `OrderDb`

```text
Orders      (Id, CustomerId, Status, TotalAmount, CreatedAt, UpdatedAt)
OrderItems  (Id, OrderId FK, ProductId, ProductName, UnitPrice, Quantity)
```

---

### 6.4 Cart Service

**Responsibility:** Session-scoped shopping basket. No SQL — entirely Redis-backed.

#### Cart Service Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/cart` | `[Authorize]` | Get current cart |
| `POST` | `/api/cart/items` | `[Authorize]` | Add item to cart |
| `PUT` | `/api/cart/items/{productId}` | `[Authorize]` | Update item quantity |
| `DELETE` | `/api/cart/items/{productId}` | `[Authorize]` | Remove item from cart |
| `DELETE` | `/api/cart` | `[Authorize]` | Clear entire cart |

#### Redis Storage

```text
Key:    cart:{userId}        (Redis Hash)
Field:  {productId}          (string)
Value:  {quantity}           (string/int)
TTL:    7 days (sliding)     reset on every interaction
```

#### Event Subscription

Subscribes to `OrderPlacedEvent` → clears `cart:{customerId}` on receipt.

#### CartItem DTO

```csharp
public record CartItem(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
);
```

---

### 6.5 Notification Service

**Responsibility:** Consume order events, send transactional emails. No HTTP API — entirely event-driven. Stateless (no database).

#### Event Consumers

| Consumer | Trigger | Action |
| --- | --- | --- |
| `OrderPlacedConsumer` | `OrderPlacedEvent` | Send "Order Confirmation" email |
| `OrderShippedConsumer` | `OrderShippedEvent` | Send "Your order is on the way" email |

#### Retry Policy

- 3 attempts with exponential backoff via MassTransit
- Base: 1s, Max: 10s, Multiplier: 2s

```csharp
public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var msg = context.Message;
        await _emailService.SendOrderConfirmationAsync(
            msg.CustomerEmail, msg.OrderId, msg.Items, msg.Total);
    }
}
```

---

## 7. API Gateway

**Technology:** Ocelot (.NET)  
**Responsibility:** Single entry point — route requests to downstream services, enforce JWT auth, rate limit.

### Routing Configuration (`ocelot.json`)

```json
{
  "Routes": [
    {
      "UpstreamPathTemplate": "/api/products/{everything}",
      "UpstreamHttpMethod": ["GET", "POST", "PUT", "DELETE"],
      "DownstreamPathTemplate": "/api/products/{everything}",
      "DownstreamScheme": "http",
      "DownstreamHostAndPorts": [{ "Host": "product-service", "Port": 80 }],
      "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" }
    },
    {
      "UpstreamPathTemplate": "/api/orders/{everything}",
      "DownstreamPathTemplate": "/api/orders/{everything}",
      "DownstreamHostAndPorts": [{ "Host": "order-service", "Port": 80 }],
      "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" },
      "RouteClaimsRequirement": { "emailVerified": "true" }
    }
  ],
  "GlobalConfiguration": {
    "BaseUrl": "https://localhost:5000",
    "RateLimitOptions": {
      "EnableRateLimiting": true,
      "Period": "1m",
      "Limit": 100
    }
  }
}
```

### Gateway Responsibilities

| Concern | Implementation |
| --- | --- |
| Routing | Upstream path → downstream service |
| Authentication | JWT Bearer validation |
| Route-level auth | `RouteClaimsRequirement` (e.g., `emailVerified`) |
| Rate limiting | 100 requests/min globally |
| Health dependency | Only starts after downstream services are healthy |

---

## 8. Message Broker — RabbitMQ

**Technology:** RabbitMQ 3 + MassTransit  
**Pattern:** Publish/Subscribe via exchanges and queues

### Exchange & Queue Map

| Exchange | Queue | Publisher | Consumers |
| --- | --- | --- | --- |
| `order.placed` | `order-placed-queue` | Order Service | Notification Service, Cart Service |
| `order.shipped` | `order-shipped-queue` | Order Service | Notification Service |

### MassTransit Configuration (per consuming service)

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h =>
        {
            h.Username(config["RabbitMQ:User"]);
            h.Password(config["RabbitMQ:Pass"]);
        });
        cfg.ReceiveEndpoint("order-placed-queue", e =>
        {
            e.ConfigureConsumer<OrderPlacedConsumer>(ctx);
            e.UseMessageRetry(r => r.Exponential(3,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2)));
        });
    });
});
```

### Fan-Out Design

`OrderPlacedEvent` is consumed by **two independent queues** — Notification and Cart each have their own `ReceiveEndpoint`. This ensures one consumer's failure does not block the other.

---

## 9. Caching Strategy — Redis

**Technology:** Redis 7 + StackExchange.Redis  
**Two distinct use cases:**

### 9.1 Product Catalog Cache (cache-aside pattern)

```csharp
// Read
var cached = await _cache.GetStringAsync("product:catalog");
if (cached != null) return JsonSerializer.Deserialize<List<ProductDto>>(cached);

// Miss — fetch from SQL
var products = await _productRepo.GetAllAsync();
await _cache.SetStringAsync("product:catalog",
    JsonSerializer.Serialize(products),
    new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });
return products;
```

| Cache key | TTL | Eviction trigger |
| --- | --- | --- |
| `product:catalog` | 10 min sliding | Any product write |
| `product:{id}` | 15 min sliding | Update or delete of that product |

### 9.2 Cart Storage (Redis Hash)

```csharp
// Add/update item
await _db.HashSetAsync($"cart:{userId}", productId.ToString(), quantity);
await _db.KeyExpireAsync($"cart:{userId}", TimeSpan.FromDays(7));

// Read cart
var entries = await _db.HashGetAllAsync($"cart:{userId}");

// Clear cart (on order placed)
await _db.KeyDeleteAsync($"cart:{userId}");
```

| Cart key | Structure | TTL |
| --- | --- | --- |
| `cart:{userId}` | Redis Hash | 7 days sliding |

---

## 10. Database Design

Three independent SQL Server databases — one per domain service. Each has its own EF Core `DbContext` and runs its own migrations independently.

### IdentityDb

```text
AspNetUsers      (ASP.NET Identity scaffolded — Id, Email, PasswordHash, ...)
AspNetRoles      (Id, Name, NormalizedName)
AspNetUserRoles  (UserId FK, RoleId FK)
RefreshTokens    (Id, UserId FK, Token, ExpiresAt, CreatedAt)
```

### ProductDb

```text
Categories  (Id, Name)
Products    (Id, VendorId, CategoryId FK, Name, Description,
             Price, StockQuantity, IsActive, CreatedAt, UpdatedAt)
```

### OrderDb

```text
Orders      (Id, CustomerId, Status, TotalAmount, CreatedAt, UpdatedAt)
OrderItems  (Id, OrderId FK, ProductId, ProductName, UnitPrice, Quantity)
```

> `ProductName` and `UnitPrice` on `OrderItem` are **snapshots** taken at order time. They are intentionally denormalised — product prices can change, but historical order records must remain accurate.

---

## 11. Clean Architecture Pattern

Each microservice follows the same 4-layer structure. Dependency direction always flows inward — outer layers know about inner layers; inner layers know nothing about outer ones.

```text
ServiceName/
├── Domain/
│   ├── Entities/        Pure C# classes — no framework deps
│   ├── Enums/
│   └── Exceptions/      DomainException, NotFoundException
├── Application/
│   ├── Commands/        Write operations + handlers
│   ├── Queries/         Read operations + handlers
│   ├── DTOs/            Data transfer objects
│   ├── Interfaces/      IRepository, ICacheService (defined here)
│   ├── Validators/      FluentValidation rules
│   └── Behaviors/       ValidationBehavior, LoggingBehavior
├── Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   ├── Repositories/    Implements Application interfaces
│   │   └── Migrations/
│   ├── Caching/             RedisCacheService : ICacheService
│   └── Messaging/           MassTransit consumers / publishers
└── API/
    ├── Controllers/
    ├── Middleware/          ExceptionHandlingMiddleware
    └── Program.cs
```

### Dependency Direction Rule

```text
API  →  Infrastructure  →  Application  →  Domain
     ↘                  ↗
          (API also refs Application directly)
```

| Layer | Allowed dependencies | Forbidden dependencies |
| --- | --- | --- |
| `Domain` | None | Everyone |
| `Application` | `Domain` | `Infrastructure`, `API` |
| `Infrastructure` | `Domain`, `Application` | `API` |
| `API` | `Application`, `Infrastructure` | — |

---

## 12. CQRS & MediatR

Commands mutate state. Queries return data. They are never mixed in the same handler.

### Command Example — PlaceOrderCommand

```csharp
public record PlaceOrderCommand(
    Guid CustomerId,
    string CustomerEmail,
    List<CartItemDto> Items
) : IRequest<OrderDto>;

public class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        await _orderRepo.AddAsync(order, ct);
        await _publisher.Publish(new OrderPlacedEvent(...), ct);
        return _mapper.Map<OrderDto>(order);
    }
}
```

#### Query Example — GetProductByIdQuery

```csharp
public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        var cached = await _cache.GetAsync<ProductDto>($"product:{query.Id}");
        if (cached != null) return cached;

        var product = await _productRepo.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(Product), query.Id);

        await _cache.SetAsync($"product:{query.Id}", product, TimeSpan.FromMinutes(15));
        return _mapper.Map<ProductDto>(product);
    }
}
```

#### Pipeline Behaviors (cross-cutting concerns)

| Behavior | Concern | Applied to |
| --- | --- | --- |
| `ValidationBehavior<TRequest, TResponse>` | FluentValidation — rejects invalid requests before handler runs | All commands |
| `LoggingBehavior<TRequest, TResponse>` | Serilog — logs request name, duration, result | All requests |

---

## 13. Authorization & Security

### 13.1 Authorization Policies

| Policy | Requirement | Enforced on |
| --- | --- | --- |
| `RequireVendor` | Role claim = `Vendor` | Product write endpoints |
| `RequireAdmin` | Role claim = `Admin` | Admin panel, user management |
| `RequireVerifiedEmail` | Claim `emailVerified = true` | Order placement |
| Default `[Authorize]` | Valid JWT (any role) | Cart, order history |
| None (anonymous) | — | Product list, product detail |

### 13.2 Defence-in-Depth

Authorization is enforced at **two independent levels**:

1. **Ocelot Gateway** — `RouteClaimsRequirement` blocks requests before they reach services
2. **Service Controllers** — `[Authorize(Policy = "...")]` enforces policies again inside each service

Neither level alone is sufficient — both must pass.

### 13.3 JWT Structure

```json
{
  "userId": "guid",
  "email": "user@example.com",
  "role": "Customer | Vendor | Admin",
  "emailVerified": "true | false",
  "exp": 1234567890
}
```

### 13.4 Refresh Token Rotation

1. Client sends refresh token to `POST /api/auth/refresh`
2. Service validates token exists in DB and is not expired
3. Old token is deleted; new token generated and stored
4. New JWT + new refresh token returned to client
5. If the old token is replayed after rotation → rejected (token not found in DB)

---

## 14. Angular UI

**Technology:** Angular 17 + Angular Material + NgRx

### Module Structure

```text
src/app/
├── core/
│   ├── auth/        JwtInterceptor, TokenRefreshInterceptor, AuthGuard, TokenService
│   └── services/    API service per microservice (IdentityService, ProductService, etc.)
├── customer/
│   ├── catalog/     ProductListComponent, ProductDetailComponent
│   ├── cart/        CartComponent, quantity controls
│   └── orders/      OrderHistoryComponent, OrderDetailComponent
├── vendor/
│   ├── dashboard/   Sales summary
│   └── products/    ProductCrudComponent
├── admin/
│   └── users/       UserListComponent, role assignment
└── shared/
    └── components/  NavbarComponent, ProductCardComponent, StatusBadgeComponent
```

### Auth Flow

1. `LoginComponent` calls `AuthService.login()` → JWT + refresh token stored **in memory** (not `localStorage`)
2. `JwtInterceptor` attaches `Authorization: Bearer <token>` to every outbound request
3. `AuthGuard` reads role claim from JWT before activating vendor/admin routes
4. On `401` response, `TokenRefreshInterceptor` calls `/api/auth/refresh` transparently and retries

### State Management (NgRx)

- Auth state: current user, JWT, role
- Product state: catalog list, selected product
- Cart state: items, total
- Order state: order history, active order

---

## 15. Docker & Infrastructure

### 15.1 Container Overview

| Container | Image | Ports | Volumes | Health Check |
| --- | --- | --- | --- | --- |
| `shopflow-sqlserver` | `mssql/server:2022-latest` | `1433` | `sqlserver-data` | `sqlcmd SELECT 1` |
| `shopflow-redis` | `redis:7-alpine` | `6379` | `redis-data` | `redis-cli ping` |
| `shopflow-rabbitmq` | `rabbitmq:3-management` | `5672`, `15672` | `rabbitmq-data` | `rabbitmq-diagnostics ping` |
| `shopflow-gateway` | `./Gateway` | `5000` | — | — |
| `shopflow-identity` | `./Services/Identity` | `5001` | — | `curl /health` |
| `shopflow-product` | `./Services/Product` | `5002` | — | `curl /health` |
| `shopflow-order` | `./Services/Order` | `5003` | — | `curl /health` |
| `shopflow-cart` | `./Services/Cart` | `5004` | — | `curl /health` |
| `shopflow-notification` | `./Services/Notification` | — | — | — |
| `shopflow-ui` | `./ClientApp` | `4200` | — | — |

### 15.2 Health Check Endpoint (per service)

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString)               // Identity, Product, Order
    .AddRedis(redisConnectionString)              // Cart, Product
    .AddRabbitMQ(rabbitConnectionString);         // Order, Notification, Cart
```

### 15.3 Environment Variables

| Variable | Used by | Purpose |
| --- | --- | --- |
| `SQL_SA_PASSWORD` | SQL Server, all services | Database SA password |
| `JWT_SECRET` | Identity, Gateway, all services | JWT signing key |
| `RABBITMQ_USER` | RabbitMQ, Order, Cart, Notification | Broker username |
| `RABBITMQ_PASS` | RabbitMQ, Order, Cart, Notification | Broker password |
| `SMTP_HOST` | Notification | Email SMTP host |
| `SMTP_PORT` | Notification | Email SMTP port |
| `SMTP_FROM` | Notification | Sender address |
| `SMTP_PASSWORD` | Notification | SMTP auth password |

### 15.4 Connection String Pattern (per service)

```text
Server=sqlserver;Database={ServiceDb};User Id=sa;Password=${SQL_SA_PASSWORD};TrustServerCertificate=True
```

---

## 16. TDD Approach

### 16.1 Cycle

**Red → Green → Refactor.** The test file is always created before the class file. No class exists without a failing test already written for it.

### 16.2 Testing Stack

| Layer | Tool |
| --- | --- |
| Unit tests | xUnit + FluentAssertions + NSubstitute |
| Integration tests | xUnit + Testcontainers (.MsSql, .Redis) |
| API tests | xUnit + `WebApplicationFactory` |
| Angular | Jest + Angular Testing Library |

### 16.3 Layer-by-Layer TDD Strategy

#### Domain layer example

```csharp
[Fact]
public void Order_Create_ShouldCalculateTotalFromItems()
{
    var items = new List<CartItemDto>
    {
        new(Guid.NewGuid(), "Widget", 10.00m, 2),
        new(Guid.NewGuid(), "Gadget", 25.00m, 1)
    };
    var order = Order.Create(Guid.NewGuid(), items);

    order.TotalAmount.Should().Be(45.00m);
    order.Status.Should().Be(OrderStatus.Pending);
}
```

#### Application layer example

```csharp
[Fact]
public async Task GetProductByIdHandler_WhenCacheHit_ShouldNotCallRepository()
{
    var cache = Substitute.For<ICacheService>();
    var repo  = Substitute.For<IProductRepository>();
    cache.GetAsync<ProductDto>($"product:{productId}").Returns(dto);

    var result = await handler.Handle(new GetProductByIdQuery(productId), default);

    await repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
}
```

#### Infrastructure layer example

```csharp
public class ProductRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();
    public async Task InitializeAsync() => await _sql.StartAsync();

    [Fact]
    public async Task AddProduct_ThenGetById_ShouldReturnProduct()
    {
        var repo = new ProductRepository(CreateDbContext(_sql.GetConnectionString()));
        var product = Product.Create(vendorId, "Widget", 9.99m, categoryId);
        await repo.AddAsync(product, default);

        var fetched = await repo.GetByIdAsync(product.Id, default);
        fetched!.Name.Should().Be("Widget");
    }
}
```

#### API layer example

```csharp
[Fact]
public async Task CreateProduct_WithoutVendorRole_ShouldReturn403()
{
    _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", CustomerToken);

    var response = await _client.PostAsJsonAsync("/api/products", payload);
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

### 16.4 TDD Order Per Service (inside-out)

```text
1. Domain entity tests       → write test → implement entity
2. Validator tests           → write test → implement validator
3. ValidationBehavior test   → write test → implement behavior
4. Handler tests             → write test → implement handler
5. Repository tests          → write test → implement repository (Testcontainers)
6. API endpoint tests        → write test → implement controller
```

### 16.5 Test Project Naming Convention

```text
{Service}.Domain.Tests           ← pure unit, no mocks
{Service}.Application.Tests      ← mocked interfaces (NSubstitute)
{Service}.Infrastructure.Tests   ← Testcontainers (real DB/Redis/RabbitMQ)
{Service}.API.Tests              ← WebApplicationFactory
```

---

## 17. Implementation Progress

### Phase 1 — Infrastructure Foundation

Status: ✅ Complete

#### What was done

| Deliverable | Detail |
| --- | --- |
| Folder structure | `Services/`, `Gateway/`, `ClientApp/`, `Shared/` created |
| `docker-compose.yml` | SQL Server + Redis + RabbitMQ with health checks, volumes, shared network |
| `.env.example` | All required secrets templated |
| `.gitignore` | `.env`, `bin/`, `obj/`, `node_modules/`, etc. excluded |

#### How to run

```bash
cp .env.example .env
# fill in .env values

docker compose up -d sqlserver redis rabbitmq
docker compose ps     # all three should show "healthy"
```

---

### Phase 2 — Identity Service

Status: ✅ Complete — pending EF Core migrations and docker-compose wiring (Steps 9–10)

#### Phase 2 deliverables

8 projects created under `Services/Identity/`, added to `ShopFlow.sln`, project references wired, all issues fixed, solution builds cleanly.

#### Projects

| Project | SDK | Role |
| --- | --- | --- |
| `Identity.Domain` | classlib | Entities, enums, exceptions |
| `Identity.Application` | classlib | Commands, queries, DTOs, interfaces, validators |
| `Identity.Infrastructure` | classlib | EF Core, JWT, repositories |
| `Identity.Api` | webapi | Controllers, middleware, Program.cs |
| `Identity.Domain.Tests` | xunit | Pure unit tests |
| `Identity.Application.Tests` | xunit | Mocked handler tests |
| `Identity.Infrastructure.Tests` | xunit | Testcontainers integration tests |
| `Identity.Api.Tests` | xunit | WebApplicationFactory endpoint tests |

#### TDD Implementation Order

```text
Step 1  ✅ Wrote failing tests in Identity.Domain.Tests (ApplicationUser, RefreshToken)
Step 2  ✅ Implemented Domain entities (ApplicationUser, RefreshToken), enums, exceptions
Step 3  ✅ Wrote failing tests for validators, behaviors, handlers in Identity.Application.Tests
Step 4  ✅ Implemented Application layer — commands/handlers, validators, behaviors, interfaces, DTOs
Step 5  ✅ Wrote Testcontainers tests in Identity.Infrastructure.Tests (TokenService, RefreshTokenRepository)
Step 6  ✅ Implemented Infrastructure layer — TokenService, AppDbContext, RefreshTokenRepository, JwtSettings
Step 7  ✅ Wrote WebApplicationFactory tests in Identity.Api.Tests
Step 8  ✅ Implemented API layer — controllers, ExceptionHandlingMiddleware, Program.cs
Step 9  → Add EF Core migrations, run against IdentityDb
Step 10 → Uncomment identity-service block in docker-compose.yml
```

#### Application Layer — Implemented (Step 4)

| Deliverable | Files |
| --- | --- |
| Commands + Handlers | `RegisterUserCommand/Handler`, `LoginCommand/Handler`, `RefreshTokenCommand/Handler` |
| Interfaces | `IUserRepository`, `ITokenService`, `IRefreshTokenRepository` |
| DTOs | `AuthResponse`, `UserProfileDto` |
| Validators | `RegisterUserCommandValidator` (email, strong password, display name), `LoginCommandValidator` |
| Pipeline Behavior | `ValidationBehavior<TRequest, TResponse>` — FluentValidation gate before every handler |
| NuGet added | `MediatR 12.5.0`, `FluentValidation 11.11.0` (Application); `FluentAssertions 6.12.2`, `NSubstitute 5.3.0` (Tests) |
| Domain change | Added public `RefreshToken(token, expiresAt, userId)` constructor for NSubstitute stubs + EF Core materialisation |
| Removed | Placeholder `Class1.cs` and `UnitTest1.cs` scaffolding files |

#### Application Tests — Implemented (Step 3)

| Test class | Scenarios covered |
| --- | --- |
| `RegisterUserCommandHandlerTests` | Happy path, duplicate email guard, `CreateAsync` called once |
| `LoginCommandHandlerTests` | Valid credentials, unknown email, wrong password |
| `RefreshTokenCommandHandlerTests` | Happy path + token rotation, expired token, unknown token |
| `ValidationBehaviorTests` | Valid request calls next, invalid request throws `ValidationException` and does not call next |
| `RegisterUserCommandValidatorTests` | All five password complexity rules, blank/null email, malformed email, blank/oversized display name |
| `LoginCommandValidatorTests` | Blank/null email, malformed email, blank/null password |

#### Domain change

Added `DuplicateEmailException : DomainException`; `RegisterUserCommandHandler` now throws it instead of `InvalidOperationException`, giving `ExceptionHandlingMiddleware` a dedicated type to map to HTTP 409.

#### Infrastructure Layer — Implemented (Steps 5–6)

| Deliverable | Detail |
| --- | --- |
| `JwtSettings` | Config POCO binding `Secret`, `Issuer`, `Audience`, `ExpiryMinutes` from `"JwtSettings"` section |
| `TokenService` | JWT with `userId`, `email`, `role`, `emailVerified` claims; 7-day refresh tokens saved via `IRefreshTokenRepository` |
| `AppDbContext` | Minimal `DbContext` — `RefreshTokens` DbSet, unique index on `Token`, max length 500; ASP.NET Identity tables deferred |
| `RefreshTokenRepository` | EF Core implementation — `GetByToken`, `Save`, `Revoke` (no-op on unknown token) |
| `UserRepository` | Stub — all methods throw `NotImplementedException` pending `UserManager<ApplicationUser>` wiring |
| NuGet added | `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.0`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.0`, `Microsoft.Extensions.Options 10.0.0` |

#### Infrastructure Tests — Implemented (Step 5)

| Test class | Scenarios covered |
| --- | --- |
| `TokenServiceTests` (9 tests) | `userId`, `email`, `role`, `emailVerified` claims, three-part JWT, `SaveAsync` called once, non-empty token, uniqueness across two calls, correct `UserId` |
| `RefreshTokenRepositoryTests` (6 Testcontainers tests) | Save+get roundtrip, persisted `UserId` and `ExpiresAt`, unknown token returns null, revoke+get returns null, revoke of non-existent does not throw |

NuGet added: `FluentAssertions 6.12.2`, `NSubstitute 5.3.0`, `Testcontainers.MsSql 4.4.0`, `Microsoft.EntityFrameworkCore.SqlServer 10.0.0`, `Microsoft.Extensions.Options 10.0.0`

#### API Layer — Implemented (Step 8)

| Deliverable | Detail |
| --- | --- |
| `AuthController` | `POST /api/auth/register` (201), `POST /api/auth/login` (200), `POST /api/auth/refresh` (200), `POST /api/auth/logout` (204, `[Authorize]`) |
| `UsersController` | `GET /api/users/me` (`[Authorize]`), `POST /api/admin/users/{id}/assign-role` (`[RequireAdmin]`) |
| `ExceptionHandlingMiddleware` | Maps `ValidationException` → 400, `InvalidCredentialsException` → 401, `NotFoundException` → 404, `DuplicateEmailException` → 409, `DomainException` → 400, unhandled → 500 |
| `Program.cs` | Full DI wiring — `JwtSettings`, `AppDbContext`, repositories/services, MediatR, `ValidationBehavior`, JWT Bearer (lazy `IOptions` for test override), authorization policies, SQL Server health check |
| NuGet added | `AspNetCore.HealthChecks.SqlServer 9.0.0`, `FluentValidation.DependencyInjectionExtensions 11.11.0`, `MediatR 12.5.0`, `Serilog.AspNetCore 9.0.0` |

#### API Tests — Implemented (Step 7)

| Deliverable | Detail |
| --- | --- |
| `IdentityApiFactory` | Swaps `AppDbContext` for EF Core InMemory; replaces `IUserRepository` and `IRefreshTokenRepository` with singleton fakes; injects deterministic JWT settings |
| `FakeUserRepository` | In-memory `Dictionary<Guid, (User, Password)>` with `Seed()` helper |
| `FakeRefreshTokenRepository` | In-memory `Dictionary<string, RefreshToken>` |
| `JwtTokenHelper` | Generates signed test JWTs matching the production claim structure |
| `AuthControllerTests` (9 tests) | Register 201 + tokens in body, duplicate 409, invalid input 400, login 200 + tokens, wrong password 401, unknown email 401, refresh valid 200, refresh invalid 401, logout 204 |
| `UsersControllerTests` (5 tests) | getMe without token 401, with valid JWT 200, profile body correct, assignRole as Customer 403, as Admin 200 |
| NuGet added | `FluentAssertions 6.12.2`, `Microsoft.AspNetCore.Mvc.Testing 10.0.0`, `Microsoft.EntityFrameworkCore.InMemory 10.0.0` |

---

## 18. Project Dependency Wiring

```text
┌─────────────────────────────────────────────────────────────────┐
│                        Identity Service                          │
│                                                                  │
│   Production Code                    Test Projects              │
│   ───────────────                    ─────────────              │
│                                                                  │
│   ┌──────────────┐                  ┌───────────────────────┐   │
│   │ Identity.API │                  │  Identity.API.Tests   │   │
│   └──────┬───┬───┘                  └───────────┬───────────┘   │
│          │   │ refs                             │ refs          │
│          │   └──────────────────┐              ▼               │
│          │ refs                 │    ┌──────────────────────┐   │
│          ▼                      │    │ Identity.Infra.Tests  │   │
│   ┌──────────────────┐          │    └──────────┬───────────┘   │
│   │  Identity.Infra  │◄─────────┘              │ refs          │
│   └────┬─────────┬───┘                         ▼               │
│        │ refs    │ refs          ┌──────────────────────────┐   │
│        │         │               │  Identity.App.Tests      │   │
│        │         │               └──────────┬───────────────┘   │
│        │         │                          │ refs              │
│        │         ▼                          ▼                   │
│        │  ┌──────────────────┐   ┌──────────────────────────┐   │
│        │  │Identity.Applicat.│◄──│  Identity.Domain.Tests   │   │
│        │  └────────┬─────────┘   └──────────────────────────┘   │
│        │ refs      │ refs                                        │
│        │           ▼                                            │
│        └──►┌──────────────────┐                                 │
│            │  Identity.Domain │                                 │
│            └──────────────────┘                                 │
│                  (no deps)                                       │
└─────────────────────────────────────────────────────────────────┘
```

### Reference Table

| Project | References |
| --- | --- |
| `Identity.Domain` | — |
| `Identity.Application` | `Identity.Domain` |
| `Identity.Infrastructure` | `Identity.Domain` + `Identity.Application` |
| `Identity.API` | `Identity.Application` + `Identity.Infrastructure` |
| `Identity.Domain.Tests` | `Identity.Domain` |
| `Identity.Application.Tests` | `Identity.Application` |
| `Identity.Infrastructure.Tests` | `Identity.Infrastructure` |
| `Identity.API.Tests` | `Identity.API` |

---

## 19. Issues Found and Fixed

| # | Project | Problem | Fix |
| --- | --- | --- | --- |
| 1 | `Identity.Application.csproj` | Had a reference to its own test project `Identity.Application.Tests` — circular and wrong | Removed bad reference; added correct `→ Identity.Domain` |
| 2 | `Identity.API.Tests.csproj` | Referenced `..\Identity.Api\Identity.Api.csproj` but actual filename is `Identity.API.csproj` (capitalisation mismatch) — build would fail | Fixed path to `..\Identity.Api\Identity.API.csproj` |
| 3 | `Identity.Infrastructure.csproj` | No project references — `Infrastructure` had no knowledge of `Domain` or `Application` | Added references to `Identity.Domain` + `Identity.Application` |
| 4 | `Identity.API.csproj` | No project references — `API` had no knowledge of `Application` or `Infrastructure` | Added references to `Identity.Application` + `Identity.Infrastructure` |
| 5 | `Identity.Domain.Tests/` | csproj filename was `Idetity.Domain.Tests.csproj` (missing 'n') — typo in filename and solution entry | Renamed file to `Identity.Domain.Tests.csproj`; updated path in `ShopFlow.sln` |

---

## 20. Build Status

```bash
dotnet build ShopFlow.sln

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.59
```

All 8 Identity Service projects build cleanly. No warnings.

---

## 21. Repository Structure

```text
ShopFlow/
├── Services/
│   ├── Identity/
│   │   ├── Identity.Domain/
│   │   ├── Identity.Application/
│   │   ├── Identity.Infrastructure/
│   │   ├── Identity.Api/
│   │   ├── Identity.Domain.Tests/
│   │   ├── Identity.Application.Tests/
│   │   ├── Identity.Infrastructure.Tests/
│   │   └── Identity.Api.Tests/
│   ├── Product/           (Phase 3 — pending)
│   ├── Order/             (Phase 5 — pending)
│   ├── Cart/              (Phase 4 — pending)
│   └── Notification/      (Phase 5 — pending)
├── Gateway/               (Phase 6 — pending)
├── ClientApp/             (Phase 7 — pending)
├── Shared/                (event contracts — pending)
├── Documentations/
│   ├── Phases/
│   │   ├── Phase1.md
│   │   └── Phase2.md
│   ├── ShopFlow-Approach.md
│   ├── ShopFlow-ProjectSpec.md
│   ├── ShopFlow-TDD-Guide.md
│   └── ShopFlow-Progress.md
├── docker-compose.yml
├── .env.example
├── .gitignore
└── ShopFlow.sln
```

---

## 22. Scope Limits

These are intentional simplifications to keep the project mid-complexity rather than enterprise-scale:

| Area | Simplification | Stretch goal |
| --- | --- | --- |
| Payment | `PUT /orders/{id}/confirm` is a stub — no real payment | Stripe integration |
| Vendors | Single vendor per product — no revenue splits | Vendor analytics dashboard |
| Database | One SQL Server instance, three DBs via connection strings | Separate DB containers |
| Email | Fire-and-forget — no delivery tracking or bounce handling | Delivery receipts |
| Search | Redis catalog cache — no full-text search | Elasticsearch |
| Events | Outbox pattern stubbed — event published in same transaction scope | Full Inbox/Outbox table |
| CI/CD | Docker Compose only | GitHub Actions |

---

## 23. Stretch Goals

| Goal | What it adds |
| --- | --- |
| Stripe payment integration | Real `Confirmed` state transition |
| Elasticsearch for product search | Full-text, faceted filtering |
| Outbox pattern (Inbox/Outbox table) | Guaranteed-once event delivery |
| SignalR order status updates | Real-time push to Angular UI |
| GitHub Actions CI/CD | Build, test, push to registry on PR merge |
| Kubernetes manifests | Helm chart replacing Docker Compose |
| Vendor analytics dashboard | Sales charts, revenue over time |
| Per-user rate limiting | Per-JWT sliding window in Redis |

---

## 24. What Comes Next

### Immediate — Finish Phase 2 (Steps 9–10)

Steps 1–8 are complete. Two wiring steps remain before the Identity Service is fully runnable:

| Step | Target | Task |
| --- | --- | --- |
| ~~1~~ | ~~`Identity.Domain.Tests`~~ | ~~Write failing tests for `ApplicationUser`, `RefreshToken`~~ |
| ~~2~~ | ~~`Identity.Domain`~~ | ~~Implement entities to pass domain tests~~ |
| ~~3~~ | ~~`Identity.Application.Tests`~~ | ~~Write failing tests for validators, behaviors, handlers~~ |
| ~~4~~ | ~~`Identity.Application`~~ | ~~Implement validators, pipeline behaviors, command handlers~~ |
| ~~5~~ | ~~`Identity.Infrastructure.Tests`~~ | ~~Write Testcontainers tests for `TokenService`, `RefreshTokenRepository`~~ |
| ~~6~~ | ~~`Identity.Infrastructure`~~ | ~~Implement EF Core DbContext, repositories, JWT TokenService~~ |
| ~~7~~ | ~~`Identity.Api.Tests`~~ | ~~Write `WebApplicationFactory` tests for all endpoints~~ |
| ~~8~~ | ~~`Identity.Api`~~ | ~~Implement controllers, `ExceptionHandlingMiddleware`, `Program.cs`~~ |
| 9 | `Identity.Infrastructure` | Wire `UserRepository` to `UserManager<ApplicationUser>`; run EF Core migrations; confirm `IdentityDb` is created |
| 10 | `docker-compose.yml` | Uncomment `identity-service` block; verify service starts healthy |

### Upcoming Phases

| Phase | Service | Key dependency |
| --- | --- | --- |
| Phase 3 | Product Service | Identity (JWT validation) |
| Phase 4 | Cart Service | Identity + RabbitMQ |
| Phase 5 | Order + Notification Services | Identity + Product + RabbitMQ |
| Phase 6 | API Gateway (Ocelot) | All services healthy |
| Phase 7 | Angular UI | All API endpoints stable |
| — | `Shared/` class library | Before Phase 5 (shared event contracts) |
