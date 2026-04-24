# ShopFlow — Multi-Vendor E-Commerce Platform

> A mid-complexity, full-stack marketplace where vendors register, list products, and customers buy.
> Orders trigger async workflows across independently deployable microservices.

---

## Table of Contents

- [Project Overview](#project-overview)
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

**Responsibility:** User registration, login, JWT issuance, role management, refresh tokens.

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

**Responsibility:** Vendor catalog — create, update, delete listings; customer search and browse; inventory tracking.

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

**Responsibility:** Checkout, order lifecycle management (`Pending → Confirmed → Shipped → Delivered`), order history.

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

**Responsibility:** Session-scoped shopping basket. No SQL — entirely Redis-backed.

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

**Responsibility:** Consume order events, send transactional emails. No HTTP API, entirely event-driven.

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

To keep the project "mid-complex" rather than enterprise-scale, these are intentionally simplified:

- **No real payment processing** — `POST /orders/{id}/confirm` stubs this; integrate Stripe as a stretch goal
- **Single vendor per product** — no revenue splits or co-vendors
- **One SQL Server instance** — three databases via separate connection strings; not separate containers
- **Email is fire-and-forget** — no delivery tracking or bounce handling
- **No full-text search** — Redis cache on catalog is sufficient; Elasticsearch is a stretch goal
- **No event sourcing** — outbox pattern is stubbed; a full outbox table is a stretch goal
- **No CI/CD pipeline** — Docker Compose only; GitHub Actions is a stretch goal

---

## Stretch Goals

| Goal | Adds |
|---|---|
| Stripe payment integration | Real `Confirmed` state transition |
| Elasticsearch for product search | Full-text, faceted filtering |
| Outbox pattern (Inbox/Outbox table) | Guaranteed-once event delivery |
| SignalR order status updates | Real-time push to Angular |
| GitHub Actions CI/CD | Build, test, push to registry on PR |
| Kubernetes manifests | Helm chart replacing Docker Compose |
| Vendor analytics dashboard | Sales charts, revenue over time |
| Rate limiting per user (not just global) | Per-JWT sliding window in Redis |
