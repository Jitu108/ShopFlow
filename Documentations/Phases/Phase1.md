# Phase 1 — Infrastructure Foundation

## Goal

Set up all infrastructure containers and the project folder structure before writing any application code. All services depend on these — nothing gets built until containers start cleanly.

---

## Project Structure

```
ShopFlow/
├── Services/
│   ├── Identity/
│   ├── Product/
│   ├── Order/
│   ├── Cart/
│   └── Notification/
├── Gateway/
├── ClientApp/
├── Shared/
├── Documentations/
├── docker-compose.yml
├── .env.example
└── .gitignore
```

---

## Infrastructure Containers

| Container | Image | Ports | Purpose |
|---|---|---|---|
| `shopflow-sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | `1433` | Three databases — IdentityDb, ProductDb, OrderDb |
| `shopflow-redis` | `redis:7-alpine` | `6379` | Cart storage + product cache |
| `shopflow-rabbitmq` | `rabbitmq:3-management` | `5672`, `15672` | Async event bus (MassTransit) |

---

## Files Created

**`docker-compose.yml`**
- SQL Server, Redis, RabbitMQ with health checks and named volumes
- All microservice definitions included but commented out — uncommented as each phase is built
- All containers on a shared `shopflow-net` bridge network

**`.env.example`**
- Template for all required secrets: `SQL_SA_PASSWORD`, `JWT_SECRET`, `RABBITMQ_USER`, `RABBITMQ_PASS`, SMTP settings
- Copy to `.env` and fill in values before running — `.env` is gitignored

**`.gitignore`**
- `.env` (secrets), `bin/`, `obj/`, `.vs/`, `node_modules/`, `dist/`, `.DS_Store`

---

## Does Phase 1 Require TDD?

**No.** Phase 1 is pure infrastructure configuration — Docker Compose, environment files, folder scaffolding. There is no application logic to drive with tests. TDD begins in Phase 2 when .NET classes with behavior are introduced.

---

## How to Run

```bash
# 1. Copy and fill in secrets
cp .env.example .env

# 2. Start infrastructure containers only
docker compose up -d sqlserver redis rabbitmq

# 3. Verify all three are healthy
docker compose ps
```

**RabbitMQ management UI:** `http://localhost:15672`
**Redis:** `localhost:6379`
**SQL Server:** `localhost:1433` (SA login with password from `.env`)

---

## Health Checks

Each container is configured with a health check so downstream services can use `condition: service_healthy` in `depends_on`:

| Container | Health check command |
|---|---|
| SQL Server | `sqlcmd -Q "SELECT 1"` |
| Redis | `redis-cli ping` |
| RabbitMQ | `rabbitmq-diagnostics ping` |

---

## Key Decisions

| Decision | Detail |
|---|---|
| Single SQL Server instance | Three databases via separate connection strings — not separate containers (scope limit) |
| Named volumes | Data persists across container restarts |
| Shared bridge network | All containers communicate by service name (e.g., `redis`, `rabbitmq`, `sqlserver`) |
| Service stubs commented out | Prevents broken builds during early phases; uncomment as each service is implemented |
| Secrets via `.env` | Never hardcoded; `.env` is gitignored, `.env.example` is committed as a template |
