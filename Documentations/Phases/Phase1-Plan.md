# Phase 1 — Infrastructure Foundation: Plan

> **Note on this document:** Phase 1 was already built and shipped before this plan was written. No pre-implementation plan file existed for it at the time — this is a retroactive reconstruction, written by working backward from [Phase1.md](Phase1.md) (the completion log), [ShopFlow-Approach.md](../ShopFlow-Approach.md), and [ShopFlow-ProjectSpec.md](../ShopFlow-ProjectSpec.md). It documents what the plan effectively was, not a historical artifact captured before the work happened.

## Context

ShopFlow's build order (per `ShopFlow-Approach.md`) puts infrastructure first: "Set up `docker-compose.yml` with SQL Server, Redis, and RabbitMQ first. All services depend on these. Don't touch application code until containers start cleanly." Every later phase (Identity, Product, Cart, ...) assumes these three containers exist, are healthy, and are reachable by service name on a shared network — so Phase 1's job is purely to stand that up, with zero application code.

## Step-by-step plan

### 1. Folder scaffolding
Create the top-level layout the rest of the project will fill in over later phases:
```
ShopFlow/
├── Services/{Identity,Product,Order,Cart,Notification}/   (empty — filled in phase by phase)
├── Gateway/            (empty — Phase 6)
├── ClientApp/          (empty — Phase 7)
├── Shared/             (empty — event contracts, filled in when a consumer first needs them)
├── Documentations/
├── docker-compose.yml
├── .env.example
└── .gitignore
```

### 2. `docker-compose.yml`
Define the three infrastructure containers, plus commented-out stubs for every microservice (uncommented one at a time as each phase builds it — this is what lets `docker-compose.yml` exist from day one without breaking `docker compose up` before any service has code):

| Container | Image | Ports | Purpose |
| --- | --- | --- | --- |
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | `1433` | Backs three logical databases (IdentityDb, ProductDb, OrderDb) via separate connection strings — one SQL Server instance, not three containers, per the scope-discipline list in `ShopFlow-Approach.md` |
| `redis` | `redis:7-alpine` | `6379` | Product catalog cache + (later) Cart storage, namespaced by key prefix (`product:`, `cart:`) to share one instance safely |
| `rabbitmq` | `rabbitmq:3-management` | `5672`, `15672` | Async event bus for MassTransit, once a service needs it |

Each gets a health check (`sqlcmd -Q "SELECT 1"`, `redis-cli ping`, `rabbitmq-diagnostics ping`) so later `depends_on: condition: service_healthy` blocks can rely on them, a named volume so data survives container restarts, and membership in one shared bridge network (`shopflow-net`) so containers can address each other by service name.

### 3. `.env.example` + `.gitignore`
- `.env.example` — template for every secret later phases will need: `SQL_SA_PASSWORD`, `JWT_SECRET`, `RABBITMQ_USER`, `RABBITMQ_PASS`, SMTP settings for Notification. Committed as a template; the real `.env` is filled in locally and gitignored.
- `.gitignore` — `.env`, `bin/`, `obj/`, `.vs/`, `node_modules/`, `dist/`, `.DS_Store`.

### 4. Verify containers start cleanly
`docker compose up -d sqlserver redis rabbitmq`, then `docker compose ps` to confirm all three reach `healthy` — this is the phase's actual exit criterion, not a specific line of code.

## Does this phase need TDD?

No. There's no application logic to drive with tests — TDD starts in Phase 2, the first phase that introduces .NET classes with behavior.

## Verification

- `docker compose ps` shows `sqlserver`, `redis`, `rabbitmq` all healthy
- RabbitMQ management UI reachable at `http://localhost:15672`
- `redis-cli -h localhost ping` → `PONG`
- `sqlcmd` (or any SQL client) connects to `localhost:1433` with the SA password from `.env`

## Critical files

- `docker-compose.yml` — the only file with real content this phase
- `.env.example`
- `Documentations/ShopFlow-Approach.md` (build-order rationale this phase follows)
