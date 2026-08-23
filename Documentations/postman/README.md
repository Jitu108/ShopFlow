# ShopFlow Postman Suite

Functional test suite for the Identity, Product, Cart, and Order services.

## Collections

- `ShopFlow.postman_collection.json` — the full 64-request regression suite (Identity → Product → Cart → Order), described below.
- `ShopFlow.vendor-onboarding.postman_collection.json` — a small, self-contained flow: register a new vendor → admin promotes it to the `Vendor` role → login as the vendor → create a product under that vendor. Independent of the full suite; run it on its own with either environment file. It resolves a `categoryId` at runtime (reuses an existing category if one exists, otherwise creates one as admin), so it works against a fresh database with no categories yet.

## Import (full suite)

1. Import `ShopFlow.postman_collection.json`.
2. Import one of the environment files and select it as the active environment:
   - `ShopFlow.local.postman_environment.json` — for services run via `dotnet run` / IDE (launchSettings HTTP ports).
   - `ShopFlow.docker.postman_environment.json` — for services run via `docker compose up`, hitting each service's own published port directly.
   - `ShopFlow.gateway.postman_environment.json` — all four `*BaseUrl` variables point at the API Gateway (`http://localhost:5005`) instead of each service's own port, so the entire suite runs through Ocelot's routing/auth/rate-limiting instead of bypassing it. This is how Phase 6 (API Gateway) was verified — the collection itself is unchanged; only the base URLs differ. Requires the `gateway` container from `docker compose up`.
3. Run the whole collection with the Collection Runner, **folder order: Identity → Product → Cart → Order**. The requests are stateful — later requests read collection variables (tokens, ids) set by earlier ones, so don't run folders in isolation or out of order on a fresh environment. (The Order folder specifically needs the Identity folder's `POST /api/auth/verify-email` endpoint and the admin login to have already run, for its own verified-customer flow and its `adminToken`-gated admin check respectively.)

## Requirements

- Identity service must be running with `ASPNETCORE_ENVIRONMENT=Development` (the default) so the seeded admin account (`admin@shopflow.com` / `Admin@12345`) exists. The suite logs in as this account to assign the `Vendor` role to its test users and to exercise admin-only endpoints — there is no other way to bootstrap an admin.
- Each run registers fresh, timestamped users (customer, vendor A, vendor B, a throwaway reset-password user) so the suite can be re-run repeatedly against the same database without colliding on duplicate emails.

## What's covered

- **Identity**: register (+ duplicate-email 409, weak-password 400), login (+ bad credentials 401), get current user, admin user listing, role assignment (+ non-admin 403), password reset (+ old password rejected after reset), token refresh, logout.
- **Product**: category creation (+ non-admin 403), product CRUD, ownership enforcement — a vendor updating/deleting another vendor's product gets **404** (`UpdateProductCommandHandler`/`DeleteProductCommandHandler` treat mismatched `VendorId` as not-found), while listing another vendor's products via `GET /api/vendors/{id}/products` gets **403** (`VendorsController` calls `Forbid()` directly on id mismatch) — these are genuinely different codes for a similar-looking check, and the suite asserts both explicitly so a future refactor that makes them consistent doesn't silently pass.
- **Cart**: auth guard (401), add/update/remove/clear, and the business rule that adding an already-present `productId` **increments** its quantity rather than replacing it (asserted directly, since there's no server-side cart total to check against — `GET /api/cart` returns a bare item array with no computed total/item-count field).
- **Order**: registers and verifies a dedicated customer (`POST /api/auth/verify-email` then re-login, since `POST /api/orders` requires `emailVerified: true`), place order (+ no-auth 401, empty-items 400), computed total, get mine, get by id (+ unknown 404), confirm (+ already-confirmed 400), admin list-all (+ forbidden-for-customer 403). Ownership-mismatch-as-404 (a non-owner requesting someone else's order) is covered at the unit/API-test level (`Order.Api.Tests`) rather than in Postman, since it needs a second customer identity the folder doesn't otherwise need.

## Not covered

- **Notification** — has no HTTP surface to assert against in Postman (no controllers, only `/health`); its email-sending behavior is covered by `Notification.Infrastructure.Tests` (a real SMTP container) and was verified live via smtp4dev during Phase 5 instead. See [Phases/Phase5.md](../Phases/Phase5.md).
- **Shipping** (`OrderShippedEvent`, a ship endpoint) — deferred out of Phase 5 to a later phase; nothing to test yet.
- CI/Newman automation — this is a manual/local Postman Runner suite for now (though it's been run via Newman a couple of times to confirm changes end-to-end against the live Docker stack: once during Phase 5 for the new Order folder, and again during Phase 6 with the `ShopFlow.gateway.postman_environment.json` environment — see [Phases/Phase6.md](../Phases/Phase6.md)).
