---
name: docker-compose-dev
description: How ShopFlow's local Docker Compose stack is wired — service dependency order, host ports, and required .env vars. Use when running/debugging the stack locally, adding a new service to docker-compose.yml, or diagnosing a container that won't start.
---

# Docker Compose (local dev stack)

Defined in [docker-compose.yml](../../../docker-compose.yml). Copy `.env.example` to `.env` first — Compose interpolates `SQL_SA_PASSWORD`, `JWT_SECRET`, `RABBITMQ_USER`/`PASS`, `SMTP_*` from it; missing vars silently produce empty strings, not a Compose error, so a container failing auth against SQL Server/RabbitMQ often means a missing/blank `.env` value, not a code bug.

## Topology

```
sqlserver, redis, rabbitmq           infra — must be "healthy" before dependents start
        ↓
identity-service (5001), product-service (5002),   — each depends_on the infra it actually uses,
order-service (5003), cart-service (5004),            not all three (e.g. cart-service has no
notification-service (no published port)              SQL Server dependency, only Redis)
        ↓
gateway (5005)                        depends_on all four core services being healthy
        ↓
angular-ui (4200)                     depends_on gateway being healthy
```

`notification-service` also depends on `smtp4dev` (dev-only SMTP catcher, UI+API on 5099) — real provider SMTP settings in `.env.example` are for production and unused locally.

## Host ports (browser/Postman-facing)

| Port | Service |
|---|---|
| 4200 | Angular UI |
| 5005 | Gateway (call this, not individual services, for anything auth'd — see [[jwt-rest-auth-conventions]]) |
| 5001–5004 | identity/product/order/cart services directly (bypasses gateway — only for isolated debugging) |
| 5099 | smtp4dev web UI (view emails the Notification service "sent") |
| 1433 / 6379 / 5672+15672 | SQL Server / Redis / RabbitMQ (+ management UI) |

Everything inside the Compose network addresses other containers by service name (`sqlserver`, `redis`, `rabbitmq`, `product-service`, ...) on their internal port `80` (or SQL Server's `1433`), never `localhost` — `localhost` inside a container means that container, not the host.

## Health-gated startup

Every service defines a `healthcheck` hitting `GET /health`; `depends_on: { condition: service_healthy }` means Compose won't start a dependent until the healthcheck passes, not just until the container process starts. If a service seems to "hang" on startup, check `docker compose ps` for an unhealthy upstream dependency before assuming the service itself is broken.

## Adding a new service to the stack

1. Give it its own `Dockerfile` (or `build: ./Services/<Name>` if the Dockerfile lives at that path; use the `context: .` + `dockerfile: Services/<Name>/Dockerfile` form instead if it needs to reference `ShopFlow.Shared` from the repo root — see `order-service`/`cart-service`).
2. Add a `/health` endpoint and a matching `healthcheck` block.
3. Add `depends_on` only for the infra it actually talks to (don't default to depending on all three infra services).
4. Add its route(s) to `Gateway/Gateway.Api/ocelot.json` — see [[jwt-rest-auth-conventions]] for the full checklist.
