# ShopFlow — Status

> Live dashboard: what's done, what's next. For design/rationale, see the docs linked below — this page only tracks state.

---

## Phase status

| Phase | Scope | Status |
| --- | --- | --- |
| Phase 1 | Infrastructure — Docker Compose, folder structure | ✅ Complete |
| Phase 2 | Identity Service | ✅ Complete |
| Phase 3 | Product Service | ✅ Complete |
| Phase 4 | Cart Service | ✅ Complete |
| Phase 5 | Order + Notification Services | ⏳ Pending |
| Phase 6 | API Gateway (Ocelot) | ⏳ Pending |
| Phase 7 | Angular UI | ⏳ Pending |
| — | `Shared/` class library (event contracts) | ✅ Complete — created in Phase 4 (`OrderPlacedEvent`, `OrderItemDto`, `OrderShippedEvent`) |

Detail per phase: [Phases/Phase1.md](Phases/Phase1.md), [Phases/Phase2.md](Phases/Phase2.md), [Phases/Phase3.md](Phases/Phase3.md), [Phases/Phase4.md](Phases/Phase4.md).

## Test totals (as of Phase 4)

```text
Identity Service   92 tests passed  (21 Domain, 38 Application, 16 Infrastructure*, 17 API)
Product Service    83 tests passed  (10 Domain, 44 Application,  8 Infrastructure*, 21 API)
Cart Service       40 tests passed  ( 1 Domain, 23 Application,  6 Infrastructure*, 10 API)
─────────────────────────────────────────────────────────────────────────────────────────
Total             215 tests passed, 0 failed

* Infrastructure tests require Docker running (Testcontainers)
```

Re-run with `dotnet test ShopFlow.sln` — treat that command, not this file, as the source of truth for current pass/fail counts.

## Immediate next steps

Phase 2 (Identity) is fully done — the two wiring steps previously tracked here are resolved, via a different approach than originally planned:

| Step | Target | Outcome |
| --- | --- | --- |
| 9 | `Identity.Infrastructure` | `UserRepository` is fully implemented, but over a custom `AppDbContext` + `IPasswordHasher<ApplicationUser>` rather than `UserManager<ApplicationUser>`. EF Core migrations were never added — `IdentityDb` is created via `Database.EnsureCreated()` at Development startup instead, confirmed present and in use. See [Phases/Phase2.md](Phases/Phase2.md). |
| 10 | `docker-compose.yml` | `identity-service` block is live (not commented out) and confirmed running healthy alongside `product-service`, `sqlserver`, `redis`, `rabbitmq`. |

**Next up, in order** (per [ShopFlow-Approach.md](ShopFlow-Approach.md)):

| Phase | Service | Key dependency |
| --- | --- | --- |
| Phase 5 | Order + Notification Services | Identity + Product + RabbitMQ |
| Phase 6 | API Gateway (Ocelot) | All services healthy |
| Phase 7 | Angular UI | All API endpoints stable |

## Known gaps

Not yet follow-up tickets, just noted. None currently tracked — new gaps found during work go here; move an entry to [Gaps closed](#gaps-closed) once it's fixed.

## Gaps closed

- **Category seeding** — `Program.cs` now seeds a default category list (`CategorySeed` in `appsettings.Development.json`) at Development startup, so `POST /api/products` no longer needs a `Category` row inserted by hand.
- **Vendor listing IDOR** — `VendorsController.GetVendorProducts` now compares the route `{id}` to the caller's `userId` claim and returns 403 Forbidden on mismatch, matching the owner-only pattern already used by `Update`/`Delete`. Covered by a new test (`GetVendorProducts_AsDifferentVendor_ShouldReturn403`).
- **Serilog wiring** — `Product.Api` now calls `UseSerilog(...)` (console sink, configured via the `Serilog` section in `appsettings.json`) and `UseSerilogRequestLogging()`, so the already-referenced `Serilog.AspNetCore` package is actually in use.
- **MassTransit commercial licensing** — MassTransit introduced a mandatory paid license starting at `9.0.0` (confirmed live: `9.2.0` throws `MassTransit.ConfigurationException` at startup without one). All `MassTransit.RabbitMQ` references (`Cart.Infrastructure`, `Cart.Api`) are pinned to `8.5.10`, the last Apache-2.0 release. Keep Order/Notification services on the same pin in Phase 5 unless the project acquires a license.

83 Product Service tests pass (10 Domain, 44 Application, 8 Infrastructure*, 21 API — Application/API counts grew from the Phase 3 baseline as gap-closing tests were added).

## Where to look for what

| Question | Doc |
| --- | --- |
| What are we building, and what's explicitly out of scope? | [ShopFlow-ProjectSpec.md](ShopFlow-ProjectSpec.md) |
| Why this build order, what to decide early? | [ShopFlow-Approach.md](ShopFlow-Approach.md) |
| How do we test each layer? | [ShopFlow-TDD-Guide.md](ShopFlow-TDD-Guide.md) |
| How is a specific service actually built? | [Architecture/](Architecture/) (per service) |
| What happened during a specific phase, and what issues came up? | [Phases/](Phases/) (per phase) |
| How do I run this locally? | [RUNNING.md](RUNNING.md) (dotnet run) / [DOCKER.md](DOCKER.md) (full Docker) |
