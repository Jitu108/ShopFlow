# ShopFlow — Status

> Live dashboard: what's done, what's next. For design/rationale, see the docs linked below — this page only tracks state.

---

## Phase status

| Phase | Scope | Status |
| --- | --- | --- |
| Phase 1 | Infrastructure — Docker Compose, folder structure | ✅ Complete |
| Phase 2 | Identity Service | ✅ Complete — see [gap below](#immediate-next-steps) |
| Phase 3 | Product Service | ✅ Complete |
| Phase 4 | Cart Service | ⏳ Pending |
| Phase 5 | Order + Notification Services | ⏳ Pending |
| Phase 6 | API Gateway (Ocelot) | ⏳ Pending |
| Phase 7 | Angular UI | ⏳ Pending |
| — | `Shared/` class library (event contracts) | ⏳ Pending — needed before Phase 5 |

Detail per phase: [Phases/Phase1.md](Phases/Phase1.md), [Phases/Phase2.md](Phases/Phase2.md), [Phases/Phase3.md](Phases/Phase3.md).

## Test totals (as of the known-gaps fix)

```text
Identity Service   92 tests passed  (21 Domain, 38 Application, 16 Infrastructure*, 17 API)
Product Service    83 tests passed  (10 Domain, 44 Application,  8 Infrastructure*, 21 API)
─────────────────────────────────────────────────────────────────────────────────────────
Total             175 tests passed, 0 failed

* Infrastructure tests require Docker running (Testcontainers)
```

Re-run with `dotnet test ShopFlow.sln` — treat that command, not this file, as the source of truth for current pass/fail counts.

## Immediate next steps

**Finish Phase 2 (Identity)** — steps 1–8 are done; two wiring steps remain:

| Step | Target | Task |
| --- | --- | --- |
| 9 | `Identity.Infrastructure` | Wire `UserRepository` to `UserManager<ApplicationUser>`; run EF Core migrations; confirm `IdentityDb` is created |
| 10 | `docker-compose.yml` | Uncomment `identity-service` block; verify service starts healthy |

**Then, in order** (per [ShopFlow-Approach.md](ShopFlow-Approach.md)):

| Phase | Service | Key dependency |
| --- | --- | --- |
| Phase 4 | Cart Service | Identity + RabbitMQ |
| Phase 5 | Order + Notification Services | Identity + Product + RabbitMQ |
| Phase 6 | API Gateway (Ocelot) | All services healthy |
| Phase 7 | Angular UI | All API endpoints stable |

## Known gaps

Not yet follow-up tickets, just noted. None currently tracked — new gaps found during work go here; move an entry to [Gaps closed](#gaps-closed) once it's fixed.

## Gaps closed

- **Category seeding** — `Program.cs` now seeds a default category list (`CategorySeed` in `appsettings.Development.json`) at Development startup, so `POST /api/products` no longer needs a `Category` row inserted by hand.
- **Vendor listing IDOR** — `VendorsController.GetVendorProducts` now compares the route `{id}` to the caller's `userId` claim and returns 403 Forbidden on mismatch, matching the owner-only pattern already used by `Update`/`Delete`. Covered by a new test (`GetVendorProducts_AsDifferentVendor_ShouldReturn403`).
- **Serilog wiring** — `Product.Api` now calls `UseSerilog(...)` (console sink, configured via the `Serilog` section in `appsettings.json`) and `UseSerilogRequestLogging()`, so the already-referenced `Serilog.AspNetCore` package is actually in use.

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
