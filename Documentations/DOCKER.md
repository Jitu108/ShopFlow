# Running ShopFlow with Docker

This guide covers running the **entire application** — infrastructure and microservices — through Docker Compose, with no local .NET SDK required. If you'd rather run a service directly with `dotnet run` against Dockerized infrastructure only, see [RUNNING.md](./RUNNING.md).

> **Current state:** All 7 planned phases are complete and containerized — Identity, Product, Order, Cart, Notification, the API Gateway, and the Angular UI (`angular-ui`, port 4200).

---

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Latest | Builds and runs every container below |

Verify:

```bash
docker --version
docker compose version
```

No .NET SDK is required for this workflow — the Dockerfiles build inside the SDK image and publish into a slim ASP.NET runtime image.

---

## 1. One-Time Setup

```bash
cd ShopFlow
cp .env.example .env
```

The `.env` file supplies secrets to Docker Compose and is gitignored. Defaults work as-is for local development:

```env
SQL_SA_PASSWORD=YourStrong@Password123
JWT_SECRET=your-super-secret-jwt-key-change-this-in-production
RABBITMQ_USER=guest
RABBITMQ_PASS=guest
```

> Unlike the local `dotnet run` workflow, you do **not** need to keep `appsettings.Development.json` in sync with `.env` — Docker Compose injects `ConnectionStrings__Default` and `JwtSettings__Secret` as environment variables, which override the values baked into the container's `appsettings.Development.json`.

---

## 2. Build and Start Everything

```bash
docker compose up -d --build
```

This builds the `identity-service` and `product-service` images from their Dockerfiles and starts every service defined in `docker-compose.yml` (SQL Server, Redis, RabbitMQ, Identity, Product), each waiting on its dependencies' health checks.

First build takes a few minutes (NuGet restore + publish for each service). Subsequent runs are fast thanks to Docker layer caching — only changed layers rebuild.

Check status:

```bash
docker compose ps
```

Expected (all `healthy`):

```
NAME                  STATUS
shopflow-sqlserver    Up X seconds (healthy)
shopflow-redis        Up X seconds (healthy)
shopflow-rabbitmq     Up X seconds (healthy)
shopflow-identity     Up X seconds (healthy)
shopflow-product      Up X seconds (healthy)
```

SQL Server takes ~30 seconds to report healthy on first start; the microservices wait for it via `depends_on: condition: service_healthy` before starting.

On first run against a fresh database, each service creates its schema via EF Core's `EnsureCreated()` and the Identity Service seeds an admin account — see [Pre-seeded Admin Account](#pre-seeded-admin-account) below.

---

## 3. Available URLs

| Service | Swagger | Health | Base URL |
|---|---|---|---|
| Identity | `http://localhost:5001/swagger` | `http://localhost:5001/health` | `http://localhost:5001` |
| Product | `http://localhost:5002/swagger` | `http://localhost:5002/health` | `http://localhost:5002` |

| Infrastructure | Address |
|---|---|
| SQL Server | `localhost:1433` |
| Redis | `localhost:6379` |
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ Management UI | `http://localhost:15672` (guest / guest) |

---

## 4. Working with Individual Services

### Rebuild and restart one service after a code change

Docker Compose does **not** hot-reload — you must rebuild the image and recreate the container:

```bash
docker compose build identity-service
docker compose up -d identity-service
```

Swagger reflects whatever image is currently running, so a stale container will hide new endpoints even though the source on disk has them. Always rebuild after pulling or making changes.

Do the same for `product-service` when it changes.

### View logs

```bash
docker compose logs -f identity-service
docker compose logs -f product-service
```

### Restart a single container without rebuilding

```bash
docker compose restart identity-service
```

### Open a shell inside a running container

```bash
docker exec -it shopflow-identity /bin/bash
```

---

## 5. Pre-seeded Admin Account

The Identity Service seeds an admin account on every Development startup (skipped if the email already exists):

| Field | Value |
|---|---|
| Email | `admin@shopflow.com` |
| Password | `Admin@12345` |
| Role | `Admin` |

Use this to obtain an admin JWT via `POST /api/auth/login`, needed for admin-only endpoints like `/api/admin/users/{id}/assign-role`, `/api/admin/users/{id}/reset-password`, and `POST /api/categories`.

---

## 6. Quick Smoke Test

```bash
# Identity: log in as the seeded admin
curl -s -X POST http://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "admin@shopflow.com", "password": "Admin@12345" }'

# Product: list categories (public)
curl -s http://localhost:5002/api/categories
```

---

## 7. Inspecting the Database Directly

```bash
docker exec -it shopflow-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$(grep SQL_SA_PASSWORD .env | cut -d= -f2)" -C \
  -Q "SELECT name FROM sys.databases"
```

Each service gets its own database (`IdentityDb`, `ProductDb`, ...) on the same SQL Server instance.

---

## 8. Stop / Reset

```bash
# Stop containers, keep data volumes
docker compose down

# Stop containers and remove Docker network only (containers already down)
docker compose down

# Full reset — stop containers AND delete all data volumes
docker compose down -v
```

After `down -v`, the next `docker compose up -d --build` starts from a completely empty database — schemas and the admin seed are recreated automatically.

---

## 9. Troubleshooting

### New endpoint not showing in Swagger

The running container is stale — it was built before your code change. Rebuild and restart it:

```bash
docker compose build <service-name>
docker compose up -d <service-name>
```

### A service is `starting` and never becomes `healthy`

Check its logs for the actual error:

```bash
docker compose logs <service-name>
```

Common cause: SQL Server itself isn't healthy yet. Services with `depends_on: condition: service_healthy` will wait, but if `sqlserver` never reports healthy, check `docker compose logs sqlserver` for `SQL Server is now ready for client connections`.

### Port already in use

Something else on the host is bound to one of `5001`, `5002`, `1433`, `6379`, `5672`, `15672`. Either stop the conflicting process or edit the `ports:` mapping for that service in `docker-compose.yml` (left side of `host:container`).

### Changes to `.env` not taking effect

Environment variables are read when the container is created, not on every request. Recreate the container:

```bash
docker compose up -d --force-recreate identity-service
```

### Full clean rebuild (nuclear option)

```bash
docker compose down -v
docker compose build --no-cache
docker compose up -d
```

---

## 10. How This Differs from `RUNNING.md`

| | This guide (Docker) | RUNNING.md (local `dotnet run`) |
|---|---|---|
| .NET SDK required | No | Yes |
| Services run as | Containers | Local processes |
| Code changes | Rebuild image (`docker compose build`) | Instant on next `dotnet run` |
| Infra (SQL/Redis/RabbitMQ) | Containers | Containers (same either way) |
| Best for | Running the app as a whole, demoing, testing Docker builds | Active development with fast iteration and a debugger attached |
