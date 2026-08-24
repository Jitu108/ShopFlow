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
| Phase 6 | API Gateway (Ocelot) | ✅ Complete — see [Phases/Phase6.md](Phases/Phase6.md) |
| Phase 7 | Angular UI | ✅ Complete — see [Phases/Phase7.md](Phases/Phase7.md) |
| — | `Shared/` class library (event contracts) | ✅ Complete — created in Phase 4; `OrderShippedEvent` gained a `CustomerEmail` field in Phase 5 |

All 7 planned phases are complete. Detail per phase: [Phases/Phase1.md](Phases/Phase1.md), [Phases/Phase2.md](Phases/Phase2.md), [Phases/Phase3.md](Phases/Phase3.md), [Phases/Phase4.md](Phases/Phase4.md), [Phases/Phase5.md](Phases/Phase5.md), [Phases/Phase6.md](Phases/Phase6.md), [Phases/Phase7.md](Phases/Phase7.md).

## Test totals (as of pre-Phase-6 gap fixes)

```text
Identity Service       118 tests passed  (21 Domain, 52 Application, 16 Infrastructure*, 29 API)
Product Service         83 tests passed  (10 Domain, 44 Application,  8 Infrastructure*, 21 API)
Cart Service            40 tests passed  ( 1 Domain, 23 Application,  6 Infrastructure*, 10 API)
Order Service            62 tests passed  (14 Domain, 25 Application,  6 Infrastructure*, 17 API)
Notification Service     10 tests passed  ( 0 Domain,  5 Application,  5 Infrastructure*,  0 API)
─────────────────────────────────────────────────────────────────────────────────────────────────
Total                   313 tests passed, 0 failed

* Infrastructure tests require Docker running (Testcontainers)

Note: Identity API grew from 25 to 29 — the 4 tests added while fixing the `Admin: List Users`
400 bug ahead of Phase 6 (see "Gaps closed" below), not part of Phase 5 itself.
```

Re-run with `dotnet test ShopFlow.sln` — treat that command, not this file, as the source of truth for current pass/fail counts.

## Immediate next steps

Phase 2 (Identity) is fully done — the two wiring steps previously tracked here are resolved, via a different approach than originally planned:

| Step | Target | Outcome |
| --- | --- | --- |
| 9 | `Identity.Infrastructure` | `UserRepository` is fully implemented, but over a custom `AppDbContext` + `IPasswordHasher<ApplicationUser>` rather than `UserManager<ApplicationUser>`. EF Core migrations were never added — `IdentityDb` is created via `Database.EnsureCreated()` at Development startup instead, confirmed present and in use. See [Phases/Phase2.md](Phases/Phase2.md). |
| 10 | `docker-compose.yml` | `identity-service` block is live (not commented out) and confirmed running healthy alongside `product-service`, `sqlserver`, `redis`, `rabbitmq`. |

**Next up:** none scheduled — all 7 planned phases are complete. One item remains deliberately deferred:

| Item | Key dependency |
| --- | --- |
| Order Service — ship endpoint + `OrderShippedConsumer` | Deferred out of Phase 5 (see [Phases/Phase5.md](Phases/Phase5.md)) — `OrderShippedEvent` already carries `CustomerEmail`, ready for whichever future work picks this up |

Phase 6 (API Gateway) is done: `Gateway/Gateway.Api` (Ocelot 25.0.0) routes all 4 downstream services, enforces JWT auth + a `RouteClaimsRequirement` on order placement, and rate-limits at 100 req/min/client (IP-identified via a small custom middleware — Ocelot's rate limiter has no IP fallback of its own). Reachable at `http://localhost:5005` (not port 5000 — see [Phases/Phase6.md](Phases/Phase6.md) for why). Verified by running the full existing Postman collection through the gateway instead of each service directly: 64 requests, 118 assertions, 0 failed.

Phase 7 (Angular UI) is done: `ClientApp/` (Angular 21.2, Material, NgRx scoped to exactly the `auth`+`cart` slices), all three role experiences (customer catalog/cart/checkout/orders, vendor products/dashboard, admin users/orders/categories), and a Dockerized `angular-ui` service completing the compose stack. Built across 8 sub-phases, each verified live against the real running backend (not mocks) before moving on — seven real correctness hazards were found and fixed along the way (a concurrent-401-refresh storm, a stale-JWT-after-email-verification trap, a two-step order-placement flow, a soft-delete visibility split, two DTO/claim naming mismatches, and — the most serious — an app-initializer race that shipped a permanently blank screen on every first browser visit, since curl-based verification can't execute JS and never caught it; only found by actually loading the app in a real browser via Chrome DevTools Protocol). 65 unit tests pass. Full narrative, hazard-by-hazard evidence, and the toolchain findings (Node/Angular/NgRx version pins, Vitest not Karma) are in [Phases/Phase7.md](Phases/Phase7.md); the architecture (module tree, auth/token flow, NgRx-scoping rationale) is in [Architecture/Angular-UI.md](Architecture/Angular-UI.md).

## Known gaps

- **No DB-level concurrency protection on stock writes.** `ProductRepository.UpdateAsync` (Product Service) is a plain read-modify-write with no optimistic-concurrency token — two concurrent `CartStockAdjustedEvent`s for the same product can still lose an update to each other. The TJKG-014 stock-tracking work (see "Gaps closed" below) narrows the oversell window considerably by moving reservation to cart-time and adding a hard availability check at order confirmation, but does not close this race at the database layer. Accepted as a known gap, not currently scheduled.
- **`ClearCartCommand` doesn't release reserved stock.** `DELETE /api/cart` deletes the whole Redis hash but publishes no `CartStockAdjustedEvent` for any item it contained — every unit those items reserved in Product stays reserved. Same for the natural 7-day cart TTL expiry. Not currently scheduled.

## Gaps closed

- **`Identity / Admin: List Users` returned 400 instead of 200.** Real code bug, not data drift: `UsersController.SearchUsers` bound its `name` query parameter as non-nullable `string` while `Identity.Api` has `<Nullable>enable</Nullable>`, so ASP.NET Core's implicit-required-for-non-nullable-reference-types model validation rejected any request that omitted `?name=` — which is exactly how the Postman "List Users" request calls it (list-all, no filter). Fixed by making `name` optional end-to-end (`UsersController` → `SearchUsersByNameQuery` → `IUserRepository.SearchByNameAsync` → both `UserRepository` and the test `FakeUserRepository`), treating a missing/blank name as "no filter." Verified live against a rebuilt `identity-service`: `GET /api/admin/users` now returns 200 with the full user list, and `?name=Vendor` returns the filtered subset. +4 API tests in `UsersControllerTests` (no filter, with filter, 403 as customer, 401 unauthenticated) — this endpoint previously had zero test coverage.
- **`Product / Get Vendor Products - Vendor A requests Vendor B id (403)` returned 200 instead of 403.** Not a code bug — `VendorsController.GetVendorProducts` already had the correct ownership check (added back in the `TJKG-004-known-gap` fix, well before Phase 3). The `shopflow-product` and `shopflow-cart` containers had simply been running for 2+ days on stale images that predated later fixes, the same staleness Phase 5 already hit and worked around for `identity-service`. Rebuilt and restarted both containers; re-verified live with two freshly-registered vendors that cross-vendor access now correctly returns 403. No source change needed. Take-away for future phases: rebuild long-running dev containers before trusting a Postman/Newman run against them, rather than assuming a real regression.

- **Category seeding** — `Program.cs` now seeds a default category list (`CategorySeed` in `appsettings.Development.json`) at Development startup, so `POST /api/products` no longer needs a `Category` row inserted by hand.
- **Vendor listing IDOR** — `VendorsController.GetVendorProducts` now compares the route `{id}` to the caller's `userId` claim and returns 403 Forbidden on mismatch, matching the owner-only pattern already used by `Update`/`Delete`. Covered by a new test (`GetVendorProducts_AsDifferentVendor_ShouldReturn403`).
- **Serilog wiring** — `Product.Api` now calls `UseSerilog(...)` (console sink, configured via the `Serilog` section in `appsettings.json`) and `UseSerilogRequestLogging()`, so the already-referenced `Serilog.AspNetCore` package is actually in use.
- **MassTransit commercial licensing** — MassTransit introduced a mandatory paid license starting at `9.0.0` (confirmed live: `9.2.0` throws `MassTransit.ConfigurationException` at startup without one). All `MassTransit.RabbitMQ` references (`Cart.Infrastructure`, `Cart.Api`) are pinned to `8.5.10`, the last Apache-2.0 release. Carried forward onto the same pin in Phase 5 (`Order.Infrastructure`/`.Api`, `Notification.Infrastructure`/`.Api`).
- **`ApplicationUser.VerifyEmail()` was dead code** — it existed and was unit-tested since Phase 2, but no command/handler/controller ever called it, so no real login (including the seeded admin) could ever get `emailVerified: true`. Fixed in Phase 5 with a new `POST /api/auth/verify-email [Authorize]` endpoint, since Order Service's `POST /api/orders [RequireVerifiedEmail]` needed a real way to satisfy that claim. See [Phases/Phase5.md](Phases/Phase5.md).
- **RabbitMQ queue-naming collision, avoided before it shipped** — Notification Service's consumer could easily have reused Cart's exact queue name (`"order-placed-queue"`), which would have made it a second *competing* consumer on Cart's own queue (round-robin) instead of an independent subscriber, silently breaking Cart's cart-clearing about half the time. Caught during Phase 5 planning; Notification uses `"notification-order-placed-queue"` instead. Verified live via the RabbitMQ management API that both queues exist with exactly 1 consumer each.

83 Product Service tests pass (10 Domain, 44 Application, 8 Infrastructure*, 21 API — Application/API counts grew from the Phase 3 baseline as gap-closing tests were added).

- **TJKG-014 — catalog stock count never changed on cart activity or order confirmation, and could be oversold with no validation.** Three real gaps, not one: (1) `ProductEntity.StockQuantity` never moved on add/update/remove-cart-item — no wiring existed between Cart and Product at all; (2) order confirmation didn't touch stock either, and had no way to detect insufficient stock before confirming, so two customers could both successfully confirm an order for the same last unit; (3) an interim confirm-time decrement (built first, then superseded once cart-time reservation replaced it) floored silently at zero instead of rejecting anything. Fixed by moving the actual reservation to cart-time — `CartStockAdjustedEvent`, published by Cart's `AddCartItemCommandHandler`/`UpdateCartItemCommandHandler`/`RemoveCartItemCommandHandler` and consumed by Product's new `CartStockAdjustedConsumer` (`ProductEntity.DecrementStock`/`IncrementStock`) — and adding a hard `IStockAvailabilityChecker` gate at confirmation (MassTransit request/response — `CheckStockRequest`/`CheckStockResponse`, Product's `CheckStockConsumer`) that `ConfirmOrderCommandHandler` must pass before an order can be confirmed; on failure the order stays `Pending` and nothing is published. Full detail in [Product-Service.md §3](Architecture/Product-Service.md#3-productinfrastructure--persistence-caching-messaging-jwt-settings), [Cart-Service.md §2](Architecture/Cart-Service.md#2-cartapplication--use-cases-cqrs), and [Order-Service.md §2](Architecture/Order-Service.md#2-orderapplication--use-cases-cqrs). Does **not** close the DB-level concurrency race — see "Known gaps" above. +5 Product Domain tests, +7 Product Infrastructure tests, +6 Cart Application tests, +1 Cart Infrastructure test, +1 Order Application test, +2 Order Infrastructure tests — updated totals: **95 Product Service** (15 Domain, 44 Application, 15 Infrastructure*, 21 API), **47 Cart Service** (1 Domain, 29 Application, 7 Infrastructure*, 10 API), **65 Order Service** (14 Domain, 26 Application, 8 Infrastructure*, 17 API).

## Where to look for what

| Question | Doc |
| --- | --- |
| What are we building, and what's explicitly out of scope? | [ShopFlow-ProjectSpec.md](ShopFlow-ProjectSpec.md) |
| Why this build order, what to decide early? | [ShopFlow-Approach.md](ShopFlow-Approach.md) |
| How do we test each layer? | [ShopFlow-TDD-Guide.md](ShopFlow-TDD-Guide.md) |
| How is a specific service actually built? | [Architecture/](Architecture/) (per service) |
| What happened during a specific phase, and what issues came up? | [Phases/](Phases/) (per phase) |
| How do I run this locally? | [RUNNING.md](RUNNING.md) (dotnet run) / [DOCKER.md](DOCKER.md) (full Docker) |
