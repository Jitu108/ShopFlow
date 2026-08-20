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

## Test totals (as of Phase 3)

```text
Identity Service   92 tests passed  (21 Domain, 38 Application, 16 Infrastructure*, 17 API)
Product Service    65 tests passed  (10 Domain, 34 Application,  8 Infrastructure*, 13 API)
─────────────────────────────────────────────────────────────────────────────────────────
Total             157 tests passed, 0 failed

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

## Known gaps (not yet follow-up tickets, just noted)

- No category-seeding step in Product Service — `POST /api/products` requires a `Category` row created manually today (see [Phases/Phase3.md](Phases/Phase3.md)).
- `VendorsController.GetVendorProducts` (Product Service) doesn't check that the caller owns the vendor ID being queried — any authenticated Vendor can list any other vendor's products (see [Architecture/Product-Service.md](Architecture/Product-Service.md)).
- `Serilog.AspNetCore` is referenced in `Product.Api` but never wired via `UseSerilog(...)` — likely a leftover from copying Identity's `.csproj` (see [Architecture/Product-Service.md](Architecture/Product-Service.md)).

## Where to look for what

| Question | Doc |
| --- | --- |
| What are we building, and what's explicitly out of scope? | [ShopFlow-ProjectSpec.md](ShopFlow-ProjectSpec.md) |
| Why this build order, what to decide early? | [ShopFlow-Approach.md](ShopFlow-Approach.md) |
| How do we test each layer? | [ShopFlow-TDD-Guide.md](ShopFlow-TDD-Guide.md) |
| How is a specific service actually built? | [Architecture/](Architecture/) (per service) |
| What happened during a specific phase, and what issues came up? | [Phases/](Phases/) (per phase) |
| How do I run this locally? | [RUNNING.md](RUNNING.md) (dotnet run) / [DOCKER.md](DOCKER.md) (full Docker) |
