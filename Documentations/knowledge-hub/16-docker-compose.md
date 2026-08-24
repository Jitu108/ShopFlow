# Docker Compose

## Abstract

ShopFlow's entire stack — five ASP.NET Core microservices, the Ocelot gateway, the Angular UI, and SQL Server/Redis/RabbitMQ/smtp4dev infrastructure — is orchestrated by one file, [docker-compose.yml](../../docker-compose.yml). This document covers what Docker Compose is, why ShopFlow uses it for local development, and how the real service list, health-gated startup order, named volumes, shared network, and `.env` wiring are actually configured.

## What it is

Docker Compose is a tool for defining and running multi-container applications from a single declarative YAML file. Each top-level entry under `services:` describes one container: what image to build or pull, what ports to publish to the host, what environment variables to inject, what other services it depends on, and how to check whether it's healthy. Compose also manages **named volumes** (persistent storage that survives a container being recreated) and **networks** (so containers can address each other by service name instead of an IP or `localhost`).

## Why ShopFlow uses it

1. **One command brings up the whole platform.** `docker compose up -d --build` starts SQL Server, Redis, RabbitMQ, all four data-owning microservices, the Notification worker, smtp4dev, the gateway, and the Angular UI — nine containers from one invocation, with no local .NET SDK required (the Dockerfiles build inside the SDK image and publish into a slim ASP.NET runtime image). See [Documentations/DOCKER.md](../DOCKER.md).
2. **Reproducible dev environment.** Every developer gets the same SQL Server/Redis/RabbitMQ versions, the same network topology, and the same startup order — instead of each person installing and configuring infrastructure locally by hand.
3. **Health-gated startup order matches real service dependencies**, not a blanket "wait for everything" — `cart-service` depends only on Redis and RabbitMQ (it has no SQL Server dependency at all, since Cart persists to Redis, not SQL Server — see [Cart-Service.md](../Architecture/Cart-Service.md)), while `identity-service` and `product-service` depend on SQL Server. This is expressed directly in the compose file rather than left to chance or manual sequencing.

## How it's used

### Real service list

[docker-compose.yml](../../docker-compose.yml) defines exactly these services, in this order:

| Service | Image / build | Published port(s) | Depends on (`service_healthy` unless noted) |
| --- | --- | --- | --- |
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | `1433:1433` | — |
| `redis` | `redis:7-alpine` | `6379:6379` | — |
| `rabbitmq` | `rabbitmq:3-management` | `5672:5672` (AMQP), `15672:15672` (management UI) | — |
| `gateway` | `build: ./Gateway` | `5005:80` | `identity-service`, `product-service`, `order-service`, `cart-service` |
| `identity-service` | `build: ./Services/Identity` | `5001:80` | `sqlserver` |
| `product-service` | `build: ./Services/Product` | `5002:80` | `sqlserver`, `redis`, `rabbitmq` |
| `order-service` | `build: { context: ., dockerfile: Services/Order/Dockerfile }` | `5003:80` | `sqlserver`, `rabbitmq` |
| `cart-service` | `build: { context: ., dockerfile: Services/Cart/Dockerfile }` | `5004:80` | `redis`, `rabbitmq` (no SQL Server) |
| `notification-service` | `build: { context: ., dockerfile: Services/Notification/Dockerfile }` | *(none published)* | `rabbitmq` (`service_healthy`), `smtp4dev` (`service_started`) |
| `smtp4dev` | `rnwood/smtp4dev:v3` | `5099:80` | — |
| `angular-ui` | `build: ./ClientApp` | `4200:80` | `gateway` |

`order-service`, `cart-service`, and `notification-service` use the `context: .` + `dockerfile: Services/<Name>/Dockerfile` build form (rather than `build: ./Services/<Name>`) specifically because their Dockerfiles need to reference `ShopFlow.Shared` from the repo root — `identity-service`/`product-service` don't need this and build with the simpler form.

### Health-gated startup ordering

Every service that other services depend on defines a `healthcheck`, and dependents use `depends_on: condition: service_healthy` rather than the default `service_started` — meaning Compose won't start a dependent until the healthcheck actually passes, not just until the container process launches. `sqlserver`'s check is representative:

```yaml
healthcheck:
  test: ["CMD", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "${SQL_SA_PASSWORD}", "-N", "-C", "-Q", "SELECT 1"]
  interval: 15s
  timeout: 5s
  retries: 5
  start_period: 30s
```

The real dependency chain is health, not a hard-coded sleep:

```text
sqlserver, redis, rabbitmq                          infra
        │
        ▼
identity-service, product-service,                  each depends_on only the
order-service, cart-service,                        infra it actually uses —
notification-service                                cart-service has no SQL
        │                                            Server dependency at all
        ▼
gateway (5005)                depends_on identity/product/order/cart being healthy
        │
        ▼
angular-ui (4200)              depends_on gateway being healthy
```

`gateway`'s own healthcheck hits its `/health` endpoint the same way every microservice does:

```yaml
gateway:
  ...
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:80/health"]
```

`smtp4dev` is the one exception in the dependency chain — `notification-service` depends on it with `condition: service_started`, not `service_healthy`, since `smtp4dev` doesn't define its own healthcheck.

### Named volumes and the shared network

```yaml
volumes:
  sqlserver-data:
  redis-data:
  rabbitmq-data:

networks:
  shopflow-net:
    driver: bridge
```

Every service that persists data mounts one of these three named volumes (`sqlserver-data:/var/opt/mssql`, `redis-data:/data`, `rabbitmq-data:/var/lib/rabbitmq`) so data survives `docker compose down` (but not `docker compose down -v`, which deletes the volumes too). Every single service in the file is attached to the same `shopflow-net` bridge network, which is what lets them address each other by service name — `sqlserver`, `redis`, `rabbitmq`, `product-service`, etc. — on their internal container port (usually `80`, or `1433` for SQL Server), rather than `localhost` (inside a container, `localhost` means that container, not the host or its siblings).

### `.env` wiring

[.env.example](../../.env.example) is the template a developer copies to a gitignored `.env`:

```env
SQL_SA_PASSWORD=YourStrong@Password123
JWT_SECRET=your-super-secret-jwt-key-change-this-in-production
RABBITMQ_USER=guest
RABBITMQ_PASS=guest
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_FROM=noreply@shopflow.com
SMTP_PASSWORD=your-smtp-password
```

Compose interpolates these into the service definitions with `${VAR}` syntax — for example, `identity-service`'s connection string and JWT settings:

```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Development
  - ConnectionStrings__Default=Server=sqlserver;Database=IdentityDb;User Id=sa;Password=${SQL_SA_PASSWORD};TrustServerCertificate=True
  - JwtSettings__Secret=${JWT_SECRET}
  - JwtSettings__Issuer=ShopFlow
  - JwtSettings__Audience=ShopFlow
  - JwtSettings__ExpiryMinutes=60
```

Note `SMTP_HOST`/`SMTP_PORT`/`SMTP_PASSWORD` in `.env.example` are production-provider placeholders and are **not** actually used by `notification-service` in this compose file — its real environment block hardcodes the local catcher instead:

```yaml
notification-service:
  environment:
    - SMTP_HOST=smtp4dev
    - SMTP_PORT=25
    - SMTP_FROM=${SMTP_FROM}
    - SMTP_PASSWORD=${SMTP_PASSWORD}
  depends_on:
    rabbitmq:
      condition: service_healthy
    smtp4dev:
      condition: service_started
```

with a comment directly above it in the file explaining why: *"Dev-only SMTP catcher — Notification's real `.env.example` SMTP_* values are for a production provider; this local server lets the confirmation email be observed (web UI + REST API) without sending anywhere real."* Missing `.env` values are interpolated as empty strings, not a Compose error — a container failing auth against SQL Server or RabbitMQ is often a blank `.env` value, not a code bug.

### The gateway's non-standard port, and the UI's static-only serving

`gateway` publishes host port **5005**, not 5000 — [Gateway.md](../Architecture/Gateway.md) documents this as a deliberate workaround for a macOS AirPlay Receiver conflict on port 5000, not an arbitrary choice:

```yaml
gateway:
  build: ./Gateway
  ports:
    - "5005:80"
```

`angular-ui` is built as a two-stage Dockerfile — [ClientApp/Dockerfile](../../ClientApp/Dockerfile) compiles with `node:20-alpine` then serves the static output with `nginx:alpine`:

```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build

FROM nginx:alpine
COPY --from=build /app/dist/shopflow-ui/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

nginx serves static files only — it does not reverse-proxy `/api` to the gateway. [nginx.conf](../../ClientApp/nginx.conf) says so directly:

```nginx
# Static files only — no reverse proxy to the Gateway. The browser calls
# the Gateway directly via environment.prod.ts's apiBaseUrl; see
# Documentations/Phases/Phase7-Plan.md Decision #5 for why.
location / {
    try_files $uri $uri/ /index.html;
}
```

The browser therefore calls `http://localhost:5005` directly in the containerized setup, which is why `gateway`'s CORS policy (`Cors__AllowedOrigins__0=http://localhost:4200`, injected as an environment variable in the `gateway` service block) has to allow the UI's origin explicitly.

### Host ports at a glance

| Port | Service | Notes |
| --- | --- | --- |
| 4200 | `angular-ui` | Browser entry point |
| 5005 | `gateway` | Call this, not individual services, for anything authenticated |
| 5001 | `identity-service` | Direct access bypasses the gateway — debugging only |
| 5002 | `product-service` | Direct access bypasses the gateway — debugging only |
| 5003 | `order-service` | Direct access bypasses the gateway — debugging only |
| 5004 | `cart-service` | Direct access bypasses the gateway — debugging only |
| 5099 | `smtp4dev` | Web UI + REST API to inspect emails "sent" in dev |
| 1433 | `sqlserver` | |
| 6379 | `redis` | |
| 5672 / 15672 | `rabbitmq` | AMQP / management UI (`guest`/`guest`) |
| *(none)* | `notification-service` | Pure RabbitMQ consumer — no REST surface, nothing published |

## Gotchas & deviations

- **Compose does not hot-reload.** A code change requires `docker compose build <service>` then `docker compose up -d <service>` (or `--force-recreate` if only an `.env` value changed) — the running container reflects whatever image was built, not what's currently on disk, per [Documentations/DOCKER.md](../DOCKER.md).
- **`cart-service` intentionally has no SQL Server dependency** — it's the one service in the whole file that departs from the "every service needs `sqlserver`" assumption, because Cart's aggregate lives in a Redis hash, not a relational table (see [01-clean-architecture.md](./01-clean-architecture.md)'s note on Cart having no `Domain` entities at all).
- **`notification-service` publishes no port at all** — it's a pure RabbitMQ consumer with no REST surface for the Angular UI (or Postman) to call directly; its only externally observable effect in dev is an email landing in `smtp4dev` at port 5099.
- A full reset (`docker compose down -v`) deletes `sqlserver-data`, `redis-data`, and `rabbitmq-data` — the next `up -d --build` starts from a completely empty database, and each service recreates its schema via EF Core's `EnsureCreated()` plus the Identity Service's admin seed.
