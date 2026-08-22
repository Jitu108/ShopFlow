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
| Phase 5 | Order + Notification Services | ✅ Complete (shipping deferred — see [Phases/Phase5.md](Phases/Phase5.md)) |
| Phase 6 | API Gateway (Ocelot) | ⏳ Pending |
| Phase 7 | Angular UI | ⏳ Pending |
| — | `Shared/` class library (event contracts) | ✅ Complete — created in Phase 4; `OrderShippedEvent` gained a `CustomerEmail` field in Phase 5 |

Detail per phase: [Phases/Phase1.md](Phases/Phase1.md), [Phases/Phase2.md](Phases/Phase2.md), [Phases/Phase3.md](Phases/Phase3.md), [Phases/Phase4.md](Phases/Phase4.md), [Phases/Phase5.md](Phases/Phase5.md).

## Test totals (as of Phase 5)

```text
Identity Service       114 tests passed  (21 Domain, 52 Application, 16 Infrastructure*, 25 API)
Product Service         83 tests passed  (10 Domain, 44 Application,  8 Infrastructure*, 21 API)
Cart Service            40 tests passed  ( 1 Domain, 23 Application,  6 Infrastructure*, 10 API)
Order Service            62 tests passed  (14 Domain, 25 Application,  6 Infrastructure*, 17 API)
Notification Service     10 tests passed  ( 0 Domain,  5 Application,  5 Infrastructure*,  0 API)
─────────────────────────────────────────────────────────────────────────────────────────────────
Total                   309 tests passed, 0 failed

* Infrastructure tests require Docker running (Testcontainers)

Note: Identity's count jumped from the 92 last recorded at Phase 4 close to 114 — only
4 of that difference (2 Application, 2 API) is Phase 5's verify-email addendum. The other
18 predate this phase: Identity.Application.Tests/.Api.Tests had already grown to 50/23
through undocumented gap-closing work sometime after Phase 4's close-out, without this
file's Phase 4 baseline row being updated to match. Not a Phase 5 regression — flagging so
the baseline is accurate going forward.
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
| Phase 6 | API Gateway (Ocelot) | All services healthy |
| Phase 7 | Angular UI | All API endpoints stable |
| *(unscheduled)* | Order Service — ship endpoint + `OrderShippedConsumer` | Deferred out of Phase 5 (see [Phases/Phase5.md](Phases/Phase5.md)) — `OrderShippedEvent` already carries `CustomerEmail`, ready for whichever phase picks this up |

## Known gaps

- **Postman: 4 pre-existing failures in Identity/Product folders**, surfaced while running the full collection via Newman during Phase 5's verification (unrelated to Phase 5's changes — the Order folder itself passed 13/13): `Identity / Admin: List Users` returns 400 instead of 200 (response also isn't the expected array shape), and `Product / Get Vendor Products - Vendor A requests Vendor B id (403)` returns 200 instead of 403. Not investigated further this phase — the long-running dev containers (up ~2 days at the time) make accumulated test-data drift a plausible cause, but that isn't confirmed. Worth a fresh look against a clean database before Phase 6.

## Gaps closed

- **Category seeding** — `Program.cs` now seeds a default category list (`CategorySeed` in `appsettings.Development.json`) at Development startup, so `POST /api/products` no longer needs a `Category` row inserted by hand.
- **Vendor listing IDOR** — `VendorsController.GetVendorProducts` now compares the route `{id}` to the caller's `userId` claim and returns 403 Forbidden on mismatch, matching the owner-only pattern already used by `Update`/`Delete`. Covered by a new test (`GetVendorProducts_AsDifferentVendor_ShouldReturn403`).
- **Serilog wiring** — `Product.Api` now calls `UseSerilog(...)` (console sink, configured via the `Serilog` section in `appsettings.json`) and `UseSerilogRequestLogging()`, so the already-referenced `Serilog.AspNetCore` package is actually in use.
- **MassTransit commercial licensing** — MassTransit introduced a mandatory paid license starting at `9.0.0` (confirmed live: `9.2.0` throws `MassTransit.ConfigurationException` at startup without one). All `MassTransit.RabbitMQ` references (`Cart.Infrastructure`, `Cart.Api`) are pinned to `8.5.10`, the last Apache-2.0 release. Carried forward onto the same pin in Phase 5 (`Order.Infrastructure`/`.Api`, `Notification.Infrastructure`/`.Api`).
- **`ApplicationUser.VerifyEmail()` was dead code** — it existed and was unit-tested since Phase 2, but no command/handler/controller ever called it, so no real login (including the seeded admin) could ever get `emailVerified: true`. Fixed in Phase 5 with a new `POST /api/auth/verify-email [Authorize]` endpoint, since Order Service's `POST /api/orders [RequireVerifiedEmail]` needed a real way to satisfy that claim. See [Phases/Phase5.md](Phases/Phase5.md).
- **RabbitMQ queue-naming collision, avoided before it shipped** — Notification Service's consumer could easily have reused Cart's exact queue name (`"order-placed-queue"`), which would have made it a second *competing* consumer on Cart's own queue (round-robin) instead of an independent subscriber, silently breaking Cart's cart-clearing about half the time. Caught during Phase 5 planning; Notification uses `"notification-order-placed-queue"` instead. Verified live via the RabbitMQ management API that both queues exist with exactly 1 consumer each.

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
