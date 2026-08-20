# ShopFlow — Multi-Vendor E-Commerce Platform

> A mid-complexity, full-stack marketplace where vendors register, list products, and customers buy.
> Orders trigger async workflows across independently deployable microservices.

---

## Table of Contents

- [Project Overview](#project-overview)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)
- [Technology Stack](#technology-stack)
- [Architecture Overview](#architecture-overview)
- [Microservices](#microservices)
  - [Identity Service](#1-identity-service)
  - [Product Service](#2-product-service)
  - [Order Service](#3-order-service)
  - [Cart Service](#4-cart-service)
  - [Notification Service](#5-notification-service)
- [API Gateway](#api-gateway-ocelot)
- [Message Broker — RabbitMQ](#message-broker--rabbitmq)
- [Caching — Redis](#caching--redis)
- [Angular UI](#angular-ui)
- [Database Design](#database-design)
- [Clean Architecture — Per Service](#clean-architecture--per-service)
- [CQRS & MediatR](#cqrs--mediatr)
- [Docker & Cloud Native](#docker--cloud-native)
- [Authorization Policies](#authorization-policies)
- [Complexity Anchors (Scope Limits)](#complexity-anchors-scope-limits)
- [Stretch Goals](#stretch-goals)

---

## Project Overview

ShopFlow is a multi-vendor marketplace platform. Vendors can register, manage product listings, and track orders. Customers browse the catalog, manage a cart, and place orders. The platform is built as a suite of .NET Core microservices behind an API gateway, with an Angular SPA as the frontend.

**User Roles:**
| Role | Capabilities |
|---|---|
| `Customer` | Browse products, manage cart, place & track orders |
| `Vendor` | Create/update/delete own listings, view order demand |
| `Admin` | Manage users, approve vendors, view platform analytics |

---

## Functional Requirements

### Authentication & Identity

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

### Product Catalog

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

### Shopping Cart

| # | Requirement |
| --- | --- |
| FR-20 | Authenticated users can view their cart |
| FR-21 | Authenticated users can add items to their cart |
| FR-22 | Authenticated users can update item quantity in their cart |
| FR-23 | Authenticated users can remove individual items from their cart |
| FR-24 | Authenticated users can clear their entire cart |
| FR-25 | Cart is automatically cleared when an order is successfully placed |
| FR-26 | Cart persists for 7 days (sliding TTL); resets on each interaction |

### Orders

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

### Notifications

| # | Requirement |
| --- | --- |
| FR-36 | Customers receive an "Order Confirmation" email when an order is placed |
| FR-37 | Customers receive a "Your order is on the way" email when an order is shipped |
| FR-38 | Email delivery is fire-and-forget (no delivery receipt or bounce handling) |
| FR-39 | Failed email deliveries are retried up to 3 times with exponential backoff |

### Admin

| # | Requirement |
| --- | --- |
| FR-40 | Admins can manage users (view, assign roles) |
| FR-41 | Admins can approve vendor applications (role assignment) |
| FR-42 | Admins can view all orders platform-wide |

---

## Non-Functional Requirements

### Security

| # | Requirement |
| --- | --- |
| NFR-01 | All sensitive endpoints are protected with JWT Bearer authentication |
| NFR-02 | Authorization is enforced at two levels: Ocelot gateway (route-level) and individual service controllers (defence-in-depth) |
| NFR-03 | JWT secrets are never hardcoded — injected via environment variables / Docker secrets |
| NFR-04 | Refresh tokens are stored hashed in the DB with expiry and rotation to prevent reuse after logout |
| NFR-05 | JWT tokens are stored in memory on the frontend (not `localStorage`) to prevent XSS token theft |
| NFR-06 | SQL injection is prevented through EF Core parameterised queries — raw SQL is not used |
| NFR-07 | Passwords are hashed by ASP.NET Core Identity (PBKDF2 by default) |

### Performance & Caching

| # | Requirement |
| --- | --- |
| NFR-08 | Product catalog reads use a cache-aside pattern with Redis (sliding 10-minute expiry) |
| NFR-09 | Individual product reads are cached per ID (sliding 15-minute expiry) |
| NFR-10 | Cart operations hit Redis directly — no SQL involved |
| NFR-11 | Redis keys are namespaced (`cart:{userId}`, `product:{id}`, `product:catalog`) to prevent collision |

### Scalability & Availability

| # | Requirement |
| --- | --- |
| NFR-12 | All services are independently deployable Docker containers |
| NFR-13 | Services declare health check endpoints (`/health`) for container orchestration |
| NFR-14 | The API gateway only routes traffic to healthy downstream services (`condition: service_healthy`) |
| NFR-15 | RabbitMQ consumers use MassTransit retry policies to handle transient failures |
| NFR-16 | Each service owns its own database schema (separate `DbContext`, separate migrations) — no shared DB coupling |

### Maintainability

| # | Requirement |
| --- | --- |
| NFR-17 | Each service follows Clean Architecture (Domain / Application / Infrastructure / API) |
| NFR-18 | CQRS separates read and write paths — commands and queries never share handlers |
| NFR-19 | MediatR pipeline behaviors handle cross-cutting concerns (validation, logging) |
| NFR-20 | Repository pattern abstracts data access — handlers depend on interfaces, not EF Core directly |
| NFR-21 | Shared event contracts (`OrderPlacedEvent`, `OrderShippedEvent`) live in a separate `Shared` class library — never duplicated |

### Testability

| # | Requirement |
| --- | --- |
| NFR-22 | All code is developed using TDD (Red → Green → Refactor) |
| NFR-23 | Domain layer is pure C# with no external dependencies — fully unit testable |
| NFR-24 | Application layer handlers depend only on interfaces — fully mockable with NSubstitute |
| NFR-25 | Infrastructure tests use Testcontainers to run against real SQL Server, Redis, and RabbitMQ |
| NFR-26 | API tests use `WebApplicationFactory` to test HTTP endpoints end-to-end within the service boundary |
| NFR-27 | Each source project has a paired test project; test projects never cross layer boundaries |

### Rate Limiting

| # | Requirement |
| --- | --- |
| NFR-28 | Global rate limiting is enforced at the API gateway: 100 requests per minute per client |

### Configuration

| # | Requirement |
| --- | --- |
| NFR-29 | All configuration uses `appsettings.json` + environment variables + Docker secrets — no hardcoded values |
| NFR-30 | The `.env` file is gitignored; `.env.example` is committed as a template |

---

## Technology Stack

| Layer | Technology |
|---|---|
| Frontend | Angular 17+, Angular Material, NgRx (state) |
| API Gateway | Ocelot (.NET Core) |
| Microservices | ASP.NET Core 8 Web API |
| Authentication | ASP.NET Core Identity + JWT Bearer + Refresh Tokens |
| Authorization | Policy-based (`RequireVendor`, `RequireAdmin`, `RequireVerifiedEmail`) |
| ORM | Entity Framework Core 8 |
| Database | SQL Server (one instance, three databases) |
| Message Broker | RabbitMQ via MassTransit |
| Cache | Redis via StackExchange.Redis |
| Architecture | Clean Architecture — Domain / Application / Infrastructure / API |
| Patterns | CQRS, MediatR, Repository Pattern, DI |
| Containerization | Docker, Docker Compose |
| Health Checks | `AspNetCore.Diagnostics.HealthChecks` |
| Configuration | `appsettings.json` + environment variables + Docker secrets |

---

## Architecture Overview

```
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
   │        │         │        │
   ▼        ▼         ▼        │
Identity  Product   Order      │
  DB        DB        DB       │
                      │        │
                      ▼        ▼
                   RabbitMQ Exchange
                  (order.placed, order.shipped)
                      │
                      ▼
                 Notification Service (consumer)
                 Cart Service (consumer — clears cart)

            Redis
        ┌────┴────┐
     Cart Hash  Product Cache
```

---

## Microservices

### 1. Identity Service

**Responsibility:** Owns authentication and account management for the whole platform. Every user — customer, vendor, or admin — registers and logs in through this service, and every other service trusts the JWT it issues rather than authenticating anyone itself. Because everything downstream depends on the identity it establishes, it's the first service built.

**Key Endpoints:**
```
POST   /api/auth/register
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout
GET    /api/users/me
PUT    /api/users/me
POST   /api/admin/users/{id}/assign-role
```

**Technical Highlights:**
- `ASP.NET Core Identity` with `ApplicationUser : IdentityUser`
- Custom `UserManager<ApplicationUser>` extensions
- JWT issued with claims: `userId`, `email`, `role`, `emailVerified`
- Refresh token stored in DB with expiry and rotation
- Three authorization policies enforced platform-wide:

```csharp
// Program.cs
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireVendor", p => p.RequireRole("Vendor"));
    options.AddPolicy("RequireAdmin",  p => p.RequireRole("Admin"));
    options.AddPolicy("RequireVerifiedEmail",
        p => p.RequireClaim("emailVerified", "true"));
});
```

**Domain Model:**
```
ApplicationUser
  ├── Id (Guid)
  ├── Email
  ├── DisplayName
  ├── Role: Customer | Vendor | Admin
  ├── IsEmailVerified
  └── RefreshTokens[ ] → RefreshToken (one-to-many)

RefreshToken
  ├── Token (string)
  ├── ExpiresAt
  └── UserId (FK)
```

**Database:** `IdentityDb` (SQL Server)

---

### 2. Product Service

**Responsibility:** Owns the product catalog. Vendors create and manage their own listings; customers and anonymous visitors browse and search the catalog publicly. It validates JWTs issued by Identity rather than authenticating anyone itself, and caches catalog reads in Redis so browsing stays fast as the catalog grows.

**Key Endpoints:**
```
GET    /api/products                  (public, cached)
GET    /api/products/{id}             (public, cached)
POST   /api/products                  [RequireVendor]
PUT    /api/products/{id}             [RequireVendor]
DELETE /api/products/{id}             [RequireVendor]
GET    /api/vendors/{id}/products     [RequireVendor]
```

**Technical Highlights:**
- CQRS with MediatR — commands and queries fully separated
- Repository pattern: `IProductRepository` over EF Core
- Cache-aside pattern: reads check Redis first, fall back to SQL, then populate cache
- Sliding expiry on cache keys (`product:{id}`, `product:catalog`)
- EF Core migrations with seed data for categories

**CQRS Example:**
```csharp
// Commands
CreateProductCommand  → CreateProductCommandHandler
UpdateProductCommand  → UpdateProductCommandHandler
DeleteProductCommand  → DeleteProductCommandHandler

// Queries
GetProductByIdQuery   → GetProductByIdQueryHandler   (Redis → SQL)
GetProductListQuery   → GetProductListQueryHandler    (Redis → SQL)
GetVendorProductsQuery
```

**MediatR Pipeline Behaviors:**
- `ValidationBehavior<TRequest, TResponse>` — FluentValidation
- `LoggingBehavior<TRequest, TResponse>` — Serilog

**Domain Model:**
```
Product
  ├── Id (Guid)
  ├── VendorId (FK → ApplicationUser)
  ├── Name
  ├── Description
  ├── Price (decimal)
  ├── StockQuantity
  ├── IsActive
  ├── CreatedAt / UpdatedAt
  └── Category → Category (many-to-one)

Category
  ├── Id
  ├── Name
  └── Products[ ]
```

**Database:** `ProductDb` (SQL Server)

---

### 3. Order Service

**Responsibility:** Owns checkout and the order lifecycle. A customer places an order from their cart contents, and it moves through `Pending → Confirmed → Shipped → Delivered` as fulfillment progresses. It's the trigger point for downstream async work — placing and shipping an order both publish events that the Cart and Notification services react to, without Order needing to know either one exists.

**Key Endpoints:**
```
POST   /api/orders                    [RequireVerifiedEmail]
GET    /api/orders                    [RequireCustomer]
GET    /api/orders/{id}
PUT    /api/orders/{id}/confirm       (payment stub)
GET    /api/admin/orders              [RequireAdmin]
```

**Technical Highlights:**
- `Order` is an aggregate root owning `OrderItems`
- On confirmation, publishes `OrderPlacedEvent` to RabbitMQ
- Repository: `IOrderRepository` with `Include()` for eager-loading items
- EF Core relationship: `Order` 1→N `OrderItem`, `OrderItem` N→1 `Product`
- Outbox pattern stub — event published inside same DB transaction scope

**Domain Model:**
```
Order
  ├── Id (Guid)
  ├── CustomerId (FK)
  ├── Status: Pending | Confirmed | Shipped | Delivered | Cancelled
  ├── TotalAmount (decimal)
  ├── CreatedAt
  ├── UpdatedAt
  └── OrderItems[ ] → OrderItem (one-to-many, cascade delete)

OrderItem
  ├── Id (Guid)
  ├── OrderId (FK)
  ├── ProductId
  ├── ProductName (snapshot at order time)
  ├── UnitPrice (snapshot)
  └── Quantity
```

**Events Published:**
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

**Database:** `OrderDb` (SQL Server)

---

### 4. Cart Service

**Responsibility:** Owns the shopping cart — letting a logged-in customer add, update, and remove items before checkout. It holds no durable business data of its own: the cart is meant to be disposable and fast, so it lives entirely in Redis with a sliding TTL, and it clears itself automatically by listening for `OrderPlacedEvent` rather than Order Service calling it directly.

**Key Endpoints:**
```
GET    /api/cart                      [Authorize]
POST   /api/cart/items                [Authorize]
PUT    /api/cart/items/{productId}    [Authorize]
DELETE /api/cart/items/{productId}    [Authorize]
DELETE /api/cart                      [Authorize]
```

**Technical Highlights:**
- `ICartRepository` implemented over `StackExchange.Redis`
- Cart stored as Redis Hash: key = `cart:{userId}`, field = `productId`, value = `quantity`
- TTL: 7 days (sliding), reset on each interaction
- Subscribes to `OrderPlacedEvent` via RabbitMQ → clears cart on successful order

**CartItem DTO:**
```csharp
public record CartItem(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
);
```

---

### 5. Notification Service

**Responsibility:** Owns customer-facing transactional email. It has no API and no one calls it directly — it exists purely to listen for order events (placed, shipped) published by Order Service and turn each into the corresponding confirmation or shipping email, decoupling email delivery from the checkout flow itself.

**Consumers:**
```
OrderPlacedConsumer  → sends "Order Confirmation" email to customer
OrderShippedConsumer → sends "Your order is on the way" email
```

**Technical Highlights:**
- MassTransit consumer registration over RabbitMQ
- MailKit (or SendGrid) for email sending
- Retry policy: 3 attempts with exponential backoff via MassTransit
- No database — stateless service

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

## API Gateway (Ocelot)

**Responsibility:** The single entry point every client — the Angular UI, or any external caller — talks to. It exists so downstream services never need to be reachable directly: it routes each upstream path to the right microservice, and enforces JWT authentication and rate limiting in one place, blocking unauthenticated or unverified requests before they ever reach a service. This is deliberately redundant with each service's own `[Authorize]` checks — defence-in-depth, not a replacement for them.

Configured via `ocelot.json`. Each route specifies upstream path, downstream service URL, allowed HTTP methods, and auth policy.

```json
{
  "Routes": [
    {
      "UpstreamPathTemplate": "/api/products/{everything}",
      "UpstreamHttpMethod": ["GET", "POST", "PUT", "DELETE"],
      "DownstreamPathTemplate": "/api/products/{everything}",
      "DownstreamScheme": "http",
      "DownstreamHostAndPorts": [{ "Host": "product-service", "Port": 80 }],
      "AuthenticationOptions": {
        "AuthenticationProviderKey": "Bearer"
      }
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

---

## Message Broker — RabbitMQ

**Responsibility:** The async backbone that lets Order Service announce what happened without knowing who's listening. Order publishes `OrderPlacedEvent`/`OrderShippedEvent` once, and RabbitMQ (via MassTransit) fans each out to every interested consumer — Notification Service to send email, Cart Service to clear the basket — each on its own queue, so one consumer's failure or backlog never blocks the other.

**MassTransit configuration** (shared across Order, Cart, Notification services):

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
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

**Exchanges & Queues:**
| Exchange | Queue | Consumers |
|---|---|---|
| `order.placed` | `order-placed-queue` | NotificationService, CartService |
| `order.shipped` | `order-shipped-queue` | NotificationService |

---

## Caching — Redis

**Responsibility:** One Redis instance doing two structurally different jobs, both aimed at keeping hot data out of SQL Server. Product Service uses it as a cache-aside layer over the catalog — a performance optimization; the data still lives in SQL and Redis is disposable. Cart Service uses it as the cart's *only* store — there is no SQL fallback, so a cart's entire lifetime lives and dies in Redis with a sliding TTL.

**Product catalog** (cache-aside in `GetProductListQueryHandler`):
```csharp
var cached = await _cache.GetStringAsync("product:catalog");
if (cached != null) return JsonSerializer.Deserialize<List<ProductDto>>(cached);

var products = await _productRepo.GetAllAsync();
await _cache.SetStringAsync("product:catalog",
    JsonSerializer.Serialize(products),
    new DistributedCacheEntryOptions
    {
        SlidingExpiration = TimeSpan.FromMinutes(10)
    });
return products;
```

**Cart** (Redis Hash via StackExchange.Redis):
```csharp
// Set item
await _db.HashSetAsync($"cart:{userId}", productId.ToString(), quantity);
await _db.KeyExpireAsync($"cart:{userId}", TimeSpan.FromDays(7));

// Get cart
var entries = await _db.HashGetAllAsync($"cart:{userId}");
```

---

## Angular UI

**Responsibility:** The single frontend for all three user roles — customer, vendor, admin — built as one SPA rather than separate apps, since they share login, navigation shell, and API access patterns and differ only in which routes/modules they can reach. It talks to every backend service exclusively through the API Gateway, never directly, and keeps auth tokens in memory rather than persistent storage to limit XSS exposure.

**Module structure:**
```
src/
├── app/
│   ├── core/
│   │   ├── auth/           (JWT interceptor, auth guard, token service)
│   │   └── services/       (API services per microservice)
│   ├── customer/
│   │   ├── catalog/        (product list, product detail)
│   │   ├── cart/           (cart view, quantity controls)
│   │   └── orders/         (order history, order detail)
│   ├── vendor/
│   │   ├── dashboard/      (sales summary)
│   │   └── products/       (CRUD listing management)
│   ├── admin/
│   │   └── users/          (role assignment, vendor approval)
│   └── shared/
│       └── components/     (navbar, product card, status badge)
```

**Auth flow:**
1. `LoginComponent` calls `AuthService.login()` → stores JWT + refresh token in memory (not localStorage)
2. `JwtInterceptor` attaches `Authorization: Bearer <token>` to every request
3. `AuthGuard` checks role claim before activating vendor/admin routes
4. On 401, `TokenRefreshInterceptor` calls `/api/auth/refresh` transparently

---

## Database Design

Three independent SQL Server databases, one per domain:

**IdentityDb:**
```
AspNetUsers           (ASP.NET Identity scaffolded)
AspNetRoles
AspNetUserRoles
RefreshTokens         (Id, UserId FK, Token, ExpiresAt, CreatedAt)
```

**ProductDb:**
```
Categories            (Id, Name)
Products              (Id, VendorId, CategoryId FK, Name, Description,
                       Price, StockQuantity, IsActive, CreatedAt, UpdatedAt)
```

**OrderDb:**
```
Orders                (Id, CustomerId, Status, TotalAmount, CreatedAt, UpdatedAt)
OrderItems            (Id, OrderId FK, ProductId, ProductName, UnitPrice, Quantity)
```

> Each service owns its schema via separate EF Core `DbContext` and runs its own migrations independently.

---

## Clean Architecture — Per Service

Each microservice follows the same layered structure:

```
ServiceName/
├── Domain/
│   ├── Entities/           (Product, Order, etc.)
│   ├── Enums/              (OrderStatus, UserRole)
│   └── Exceptions/         (DomainException, NotFoundException)
├── Application/
│   ├── Commands/           (CreateProductCommand + Handler)
│   ├── Queries/            (GetProductByIdQuery + Handler)
│   ├── DTOs/               (ProductDto, OrderDto)
│   ├── Interfaces/         (IProductRepository, ICacheService)
│   ├── Validators/         (FluentValidation)
│   └── Behaviors/          (ValidationBehavior, LoggingBehavior)
├── Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   ├── Repositories/   (ProductRepository : IProductRepository)
│   │   └── Migrations/
│   ├── Caching/            (RedisCacheService : ICacheService)
│   └── Messaging/          (MassTransit consumers / publishers)
└── API/
    ├── Controllers/
    ├── Middleware/          (ExceptionHandlingMiddleware)
    └── Program.cs
```

**Dependency direction:** `API → Application ← Infrastructure`. The Application layer defines interfaces; Infrastructure implements them. Domain has zero external dependencies.

---

## CQRS & MediatR

**Responsibility:** Keeps every service's write path and read path from tangling into one bloated handler. Commands (`CreateProductCommand`, `PlaceOrderCommand`, ...) mutate state and never return query-shaped data; queries (`GetProductByIdQuery`, ...) read and never mutate — each gets its own handler, so a change to how orders are placed can't accidentally break how orders are listed. MediatR is just the dispatch mechanism: controllers send a command/query object and never call a handler directly, which is also what lets cross-cutting concerns (validation, logging) be added once as pipeline behaviors instead of copy-pasted into every handler.

**Command example — PlaceOrderCommand:**
```csharp
// Application/Commands/PlaceOrderCommand.cs
public record PlaceOrderCommand(
    Guid CustomerId,
    string CustomerEmail,
    List<CartItemDto> Items
) : IRequest<OrderDto>;

// Application/Commands/PlaceOrderCommandHandler.cs
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

**Query example — GetProductByIdQuery:**
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

        await _cache.SetAsync($"product:{query.Id}", product,
            TimeSpan.FromMinutes(15));
        return _mapper.Map<ProductDto>(product);
    }
}
```

---

## Docker & Cloud Native

**Responsibility:** Makes every service and its infrastructure dependency (SQL Server, Redis, RabbitMQ) independently deployable and reproducible on any machine without a locally installed .NET SDK. Health checks and `depends_on: condition: service_healthy` chains mean a service only starts once what it actually needs is ready — e.g. the Gateway won't come up until Identity and Product report healthy — rather than crash-looping on a database that hasn't finished initializing yet.

**docker-compose.yml (excerpt):**
```yaml
version: '3.9'
services:

  gateway:
    build: ./Gateway
    ports: ["5000:80"]
    depends_on:
      identity-service:
        condition: service_healthy
      product-service:
        condition: service_healthy

  identity-service:
    build: ./Services/Identity
    environment:
      - ConnectionStrings__Default=Server=sqlserver;Database=IdentityDb;...
      - JwtSettings__Secret=${JWT_SECRET}
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:80/health"]
      interval: 15s
      timeout: 5s
      retries: 3

  product-service:
    build: ./Services/Product
    depends_on:
      - redis
      - sqlserver

  order-service:
    build: ./Services/Order
    depends_on:
      - rabbitmq
      - sqlserver

  cart-service:
    build: ./Services/Cart
    depends_on:
      - redis
      - rabbitmq

  notification-service:
    build: ./Services/Notification
    depends_on:
      - rabbitmq

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: ${SQL_SA_PASSWORD}
      ACCEPT_EULA: Y

  rabbitmq:
    image: rabbitmq:3-management
    ports: ["15672:15672"]    # management UI

  redis:
    image: redis:7-alpine

  angular-ui:
    build: ./ClientApp
    ports: ["4200:80"]
```

**Health check endpoint (per service):**
```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString)
    .AddRedis(redisConnectionString)     // Cart, Product
    .AddRabbitMQ(rabbitConnectionString); // Order, Notification, Cart
```

---

## Authorization Policies

**Responsibility:** The one set of role/claim rules every service authorizes against, defined once by Identity (the only service that issues JWTs) and enforced identically everywhere a token is checked. Because the claims themselves — `role`, `emailVerified` — are baked into the JWT, no service needs to call back to Identity to authorize a request; it just reads the token it already has.

| Policy | Requirement | Applied to |
|---|---|---|
| `RequireVendor` | Role = `Vendor` | Product write endpoints |
| `RequireAdmin` | Role = `Admin` | Admin panel, user management |
| `RequireVerifiedEmail` | Claim `emailVerified = true` | Order placement |
| Default `[Authorize]` | Valid JWT | Cart, order history |
| None | Anonymous | Product list, product detail |

Policies are enforced at two levels: the **Ocelot gateway** (route-level claim checks) and the **individual service controllers** (policy attribute), giving defence-in-depth.

---

## Complexity Anchors (Scope Limits)

To keep the project "mid-complex" rather than enterprise-scale, these are intentionally simplified. Each row states not just what's cut, but what risk that acceptably leaves on the table today, and whether reversing it is even planned.

| Limitation | Why it's acceptable at this scope | Risk being accepted | Reversed by |
| --- | --- | --- | --- |
| No real payment processing | Payment gateway integration (PCI compliance, webhooks, idempotency) is a project of its own; a stub keeps focus on the order lifecycle and event flow | None in production as-is — `PUT /orders/{id}/confirm` must not ship live without a real processor behind it | Stripe payment integration |
| Single vendor per product | Avoids revenue-split logic, co-vendor conflict resolution, and multi-party payout — each product has exactly one owner | A marketplace with shared or co-branded listings isn't representable | *(permanent — not planned)* |
| One SQL Server instance, three databases | Separate DB containers would add Compose complexity and resource cost without changing the architecture — each service still owns its schema exclusively via its own connection string | One SQL Server outage takes down all three services' storage simultaneously, even though the services are otherwise independently deployable | *(permanent — not planned; separate containers would change deployment topology, not the architecture)* |
| Email is fire-and-forget | No delivery-receipt or bounce-handling integration; MassTransit's retry policy is the only reliability mechanism | A permanently undeliverable email (bad address) fails silently after 3 retries — the customer never receives it and nothing surfaces the failure | *(permanent — not planned; would require a transactional email provider with delivery webhooks)* |
| No full-text search | Redis cache-aside over the catalog is sufficient at this data volume; a dedicated search engine is unjustified complexity here | No fuzzy matching, typo tolerance, or faceted filtering — catalog search is exact-ish at best | Elasticsearch for product search |
| No event sourcing / full outbox | The event publish happens inside the same request as the DB write, not atomically with it via a durable outbox table | The classic dual-write problem: if the DB commit succeeds but the RabbitMQ publish then fails (network blip, broker down), the event is silently lost — Cart never clears, Notification never fires, and nothing retries it | Outbox pattern (Inbox/Outbox table) |
| No CI/CD pipeline | Docker Compose covers local/dev needs; a pipeline is orthogonal to proving the architecture works | Every deploy is manual; no automated test gate before a change reaches a running environment | GitHub Actions CI/CD |

---

## Stretch Goals

| Goal | Adds | Reverses |
| --- | --- | --- |
| Stripe payment integration | Real `Confirmed` state transition, real money movement | No real payment processing |
| Elasticsearch for product search | Full-text, faceted filtering | No full-text search |
| Outbox pattern (Inbox/Outbox table) | Guaranteed-once event delivery | No event sourcing / full outbox |
| SignalR order status updates | Real-time push to Angular UI when order status changes | *(additive — no corresponding limitation above)* |
| GitHub Actions CI/CD | Build, test, push to registry on PR merge | No CI/CD pipeline |
| Kubernetes manifests | Helm chart replacing Docker Compose | *(additive — a deployment-topology change, not a removed limit)* |
| Vendor analytics dashboard | Sales charts, revenue over time | *(additive)* |
| Per-user rate limiting | Per-JWT sliding window in Redis, instead of one global cap | *(refines NFR-28's global limit — not a Complexity Anchor)* |
