# ShopFlow Postman Suite

Functional test suite for the Identity, Product, and Cart services.

## Collections

- `ShopFlow.postman_collection.json` — the full 51-request regression suite (Identity → Product → Cart), described below.
- `ShopFlow.vendor-onboarding.postman_collection.json` — a small, self-contained flow: register a new vendor → admin promotes it to the `Vendor` role → login as the vendor → create a product under that vendor. Independent of the full suite; run it on its own with either environment file. It resolves a `categoryId` at runtime (reuses an existing category if one exists, otherwise creates one as admin), so it works against a fresh database with no categories yet.

## Import (full suite)

1. Import `ShopFlow.postman_collection.json`.
2. Import one of the environment files and select it as the active environment:
   - `ShopFlow.local.postman_environment.json` — for services run via `dotnet run` / IDE (launchSettings HTTP ports).
   - `ShopFlow.docker.postman_environment.json` — for services run via `docker compose up`.
3. Run the whole collection with the Collection Runner, **folder order: Identity → Product → Cart**. The requests are stateful — later requests read collection variables (tokens, ids) set by earlier ones, so don't run folders in isolation or out of order on a fresh environment.

## Requirements

- Identity service must be running with `ASPNETCORE_ENVIRONMENT=Development` (the default) so the seeded admin account (`admin@shopflow.com` / `Admin@12345`) exists. The suite logs in as this account to assign the `Vendor` role to its test users and to exercise admin-only endpoints — there is no other way to bootstrap an admin.
- Each run registers fresh, timestamped users (customer, vendor A, vendor B, a throwaway reset-password user) so the suite can be re-run repeatedly against the same database without colliding on duplicate emails.

## What's covered

- **Identity**: register (+ duplicate-email 409, weak-password 400), login (+ bad credentials 401), get current user, admin user listing, role assignment (+ non-admin 403), password reset (+ old password rejected after reset), token refresh, logout.
- **Product**: category creation (+ non-admin 403), product CRUD, ownership enforcement — a vendor updating/deleting another vendor's product gets **404** (`UpdateProductCommandHandler`/`DeleteProductCommandHandler` treat mismatched `VendorId` as not-found), while listing another vendor's products via `GET /api/vendors/{id}/products` gets **403** (`VendorsController` calls `Forbid()` directly on id mismatch) — these are genuinely different codes for a similar-looking check, and the suite asserts both explicitly so a future refactor that makes them consistent doesn't silently pass.
- **Cart**: auth guard (401), add/update/remove/clear, and the business rule that adding an already-present `productId` **increments** its quantity rather than replacing it (asserted directly, since there's no server-side cart total to check against — `GET /api/cart` returns a bare item array with no computed total/item-count field).

## Not covered

- Order/Notification/Gateway — not implemented in the codebase yet.
- CI/Newman automation — this is a manual/local Postman Runner suite for now.
