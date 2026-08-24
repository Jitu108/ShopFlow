# ShopFlow Knowledge Hub

A technology-by-technology reference for everything used to build ShopFlow — what each piece is, why ShopFlow specifically uses it, and how it's actually wired in this codebase. Every file is grounded in real source read from this repo (file paths, code excerpts, real config), not generic textbook explanations.

This complements [Documentations/Architecture/](../Architecture/), which documents each *service* end-to-end. This hub instead cuts across services by *technology*, so "why do we use Redis" or "how does MassTransit retry work" has one canonical answer instead of being repeated per service.

## Architecture patterns

| Doc | Covers |
| --- | --- |
| [01-clean-architecture.md](./01-clean-architecture.md) | The 4-layer Domain/Application/Infrastructure/Api split every service follows, and the dependency-inversion rule behind it |
| [02-cqrs-mediatr.md](./02-cqrs-mediatr.md) | CQRS (commands vs. queries) and MediatR as the in-process dispatcher, plus the pipeline behavior mechanism |
| [03-fluentvalidation.md](./03-fluentvalidation.md) | FluentValidation as a MediatR pipeline behavior, keeping validation out of Domain/DTOs |

## Data & messaging

| Doc | Covers |
| --- | --- |
| [04-efcore-sqlserver.md](./04-efcore-sqlserver.md) | EF Core 10 + SQL Server for Identity, Product, Order; DbContext, repositories, the Order aggregate |
| [05-redis.md](./05-redis.md) | Redis in its two roles: Product's cache-aside layer, and Cart's sole persistence (no SQL Server at all) |
| [06-rabbitmq-masstransit.md](./06-rabbitmq-masstransit.md) | RabbitMQ + MassTransit 8 for async eventing (Order → Cart, Order → Notification), including the 8.5.10 version-pin gotcha |

## Security & gateway

| Doc | Covers |
| --- | --- |
| [07-jwt-authentication.md](./07-jwt-authentication.md) | JWT Bearer auth issued by Identity, validated everywhere else; claims, shared secret, authorization policies |
| [08-ocelot-gateway.md](./08-ocelot-gateway.md) | Ocelot as the single public entry point; route config, auth-provider keys, the port-5005 AirPlay gotcha |
| [09-serilog-logging.md](./09-serilog-logging.md) | Serilog structured logging setup across services (and a real gap found in Identity) |
| [10-swagger-openapi.md](./10-swagger-openapi.md) | Swashbuckle/OpenAPI for interactive API docs and manual testing during development |
| [11-health-checks.md](./11-health-checks.md) | Per-service `/health` endpoints tailored to each service's real dependencies |

## Testing & notifications

| Doc | Covers |
| --- | --- |
| [12-testing-stack.md](./12-testing-stack.md) | xUnit, FluentAssertions, NSubstitute, Testcontainers, WebApplicationFactory, EF Core InMemory, coverlet — and the inside-out TDD philosophy tying them together |
| [13-mailkit-notifications.md](./13-mailkit-notifications.md) | MailKit SMTP sending in Notification.Api, and smtp4dev as the local email catcher |

## Frontend & infrastructure

| Doc | Covers |
| --- | --- |
| [14-angular-material-cdk.md](./14-angular-material-cdk.md) | Angular 21 + Angular Material/CDK: standalone components, Material 3 theming, dialogs |
| [15-ngrx-state-management.md](./15-ngrx-state-management.md) | NgRx, deliberately scoped to only the `auth` and `cart` features |
| [16-docker-compose.md](./16-docker-compose.md) | Docker Compose orchestration of the full stack — services, networks, volumes, port mappings |

---

**How to use this hub:** if you're touching a piece of infrastructure and want to know *why* it's shaped the way it is before changing it, start here. If you want to know how a whole *service* fits together, start in [Architecture/](../Architecture/) instead.
