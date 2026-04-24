# ShopFlow — Project Approach Guide

## What You're Building

5 .NET 8 microservices + Angular 17 SPA + Ocelot gateway, all wired through SQL Server, Redis, and RabbitMQ, running in Docker Compose.

---

## Recommended Build Order (Critical Path)

### Phase 1 — Infrastructure Foundation
Set up `docker-compose.yml` with SQL Server, Redis, and RabbitMQ first. All services depend on these. Don't touch application code until containers start cleanly.

### Phase 2 — Identity Service
**Build this first — everything depends on JWT auth.**
- ASP.NET Core Identity + `ApplicationUser`
- JWT issuance with claims (`userId`, `email`, `role`, `emailVerified`)
- Refresh token rotation
- The three authorization policies go here and propagate platform-wide

### Phase 3 — Product Service
Second priority because cart and order reference product IDs.
- Clean Architecture scaffold (Domain → Application → Infrastructure → API)
- CQRS with MediatR, FluentValidation behaviors
- Redis cache-aside on reads
- EF Core migrations → `ProductDb`

### Phase 4 — Cart Service
Simplest service — no SQL, pure Redis Hash.
- `ICartRepository` over StackExchange.Redis
- MassTransit consumer for `OrderPlacedEvent` (clears cart)
- 7-day sliding TTL

### Phase 5 — Order Service + Notification Service *(parallel)*
These can be built simultaneously:
- **Order**: aggregate root, EF Core → `OrderDb`, publishes `OrderPlacedEvent` + `OrderShippedEvent`
- **Notification**: stateless MassTransit consumer, MailKit email sending, no DB

### Phase 6 — API Gateway (Ocelot)
Wire `ocelot.json` routes only after all downstream services are healthy. Gateway is pure config — no custom code except middleware.

### Phase 7 — Angular UI
Build last when all API endpoints are stable:
1. `core/auth` (JWT interceptor, refresh interceptor, AuthGuard)
2. `customer/catalog` → `customer/cart` → `customer/orders`
3. `vendor/products` (CRUD)
4. `admin/users`

---

## Key Technical Decisions to Nail Early

| Decision | What to do |
|---|---|
| JWT secret sharing | Use env var `JWT_SECRET` via Docker secrets; all services validate with same secret |
| EF migrations | Each service runs `dotnet ef migrations add Init` independently against its own `DbContext` |
| MassTransit fan-out | `OrderPlacedEvent` needs two separate queue endpoints — one for Notification, one for Cart |
| Redis connection | Share single Redis instance; namespace keys (`cart:`, `product:`) to avoid collisions |
| Auth at gateway vs service | Enforce at both layers (Ocelot + `[Authorize]` attribute) for defense-in-depth |

---

## What to Avoid / Scope Discipline

The spec explicitly keeps these out — don't scope-creep:
- No real payment (stub `PUT /orders/{id}/confirm`)
- No full outbox table (just publish inside transaction scope)
- No full-text search (Redis catalog cache is enough)
- One SQL Server instance, three databases via connection strings

---

## Suggested Folder Layout

```
ShopFlow/
├── Services/
│   ├── Identity/
│   ├── Product/
│   ├── Order/
│   ├── Cart/
│   └── Notification/
├── Gateway/              (Ocelot config + Program.cs)
├── ClientApp/            (Angular)
├── Shared/               (shared event contracts — OrderPlacedEvent, etc.)
└── docker-compose.yml
```

The `Shared/` contracts library (event records like `OrderPlacedEvent`) should be a separate class library referenced by Order, Cart, and Notification services — don't duplicate the event definitions.

---

## Start Here

Start with docker-compose + Identity Service. Once login/JWT works end-to-end, everything else has a foundation to build on.
