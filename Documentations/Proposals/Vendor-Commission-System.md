# Vendor Commission System — Draft

**Status:** Draft / proposal, not implemented. Written for discussion — this is not a description of current behavior. Nothing in ShopFlow today calculates, stores, or negotiates a commission; this document lays out the option space and a recommendation.

## Current state

There is no commission concept anywhere in the codebase today:

- `OrderItemEntity` ([Order.Domain/Entities/OrderItemEntity.cs](../../Services/Order/Order.Domain/Entities/OrderItemEntity.cs)) has no `VendorId` — a line item only knows `ProductId`, `ProductName`, `UnitPrice`, `Quantity`. `ProductEntity` ([Product.Domain/Entities/ProductEntity.cs](../../Services/Product/Product.Domain/Entities/ProductEntity.cs)) does carry `VendorId`, but that association is never carried over into Order.
- `PlaceOrderCommandHandler` ([Order.Application/Commands/PlaceOrderCommandHandler.cs](../../Services/Order/Order.Application/Commands/PlaceOrderCommandHandler.cs)) builds `OrderItemEntity`s directly from client-supplied `command.Items` — there's no server-side call to Product at order-creation time at all today. Any commission design that needs per-vendor, per-line data has to introduce that lookup (or ride along on an existing one, e.g. the stock-check path described in [Order-Saga-With-RabbitMQ.md](Order-Saga-With-RabbitMQ.md)).
- `ApplicationUser` ([Identity.Domain/Entities/ApplicationUser.cs](../../Services/Identity/Identity.Domain/Entities/ApplicationUser.cs)) is the only vendor record that exists — there is no separate `VendorProfile` entity. `Identity.Application` already exposes vendor-facing read paths like `GetVendorNamesByIdsQuery` → `VendorSummaryDto` ([Identity.Application/Queries/GetVendorNamesByIdsQuery.cs](../../Services/Identity/Identity.Application/Queries/GetVendorNamesByIdsQuery.cs)), which is a template for how a rate would be looked up cross-service.

## Option space

### Rate model

| Model | How it works | Complexity | Notes |
|---|---|---|---|
| Flat platform-wide % | One rate applies to every vendor, every order | Lowest — a config value | No per-vendor differentiation possible later without a migration |
| Per-vendor % | Each vendor has its own rate, stored on its record | Low–medium | Natural fit given `ApplicationUser` already holds per-vendor fields (`DisplayName`, `Role`) |
| Per-category % | Rate varies by product category | Medium | Needs a category→rate map; an order line can only cleanly map to one rate if categories don't overlap |
| Tiered/volume-based % | Rate drops as a vendor's rolling sales volume grows | Higher | Needs a recurring aggregation of vendor sales; no such job exists today |
| Hybrid — platform default + per-vendor override | Global default rate; a vendor record can override it | Low–medium, most flexible | Reduces to "flat" for any vendor with no override set |

### Negotiation mechanism

| Mechanism | How it works | Build cost |
|---|---|---|
| Fixed by policy | Rate is a non-negotiable constant | None — no negotiation surface at all |
| Admin sets rate per vendor | An admin sets/edits a vendor's rate directly (API or DB) | A `CommissionRate` field + an admin-only update endpoint, audited |
| Vendor requests, admin approves | Vendor proposes a rate; sits `Requested` until an admin approves/rejects | A small state machine (`Requested → Approved/Rejected`) + notification to the vendor either way |
| Contract/tier-derived, no discretion | Rate is computed automatically from a rule (e.g. trailing-90-day volume) | An aggregation job/query + a rate-lookup table; no human sets anything |

### Timing of calculation

| When | How it works | Trade-off |
|---|---|---|
| At order placement | Commission computed and stored as part of the order the moment it's created | Simple; the order is the single source of truth for what the platform earned on it. Needs to handle refunds/cancellations as a separate reversal, since the amount was already booked |
| At payment/settlement | Commission only realized when a payout run actually moves money to the vendor | More correct for accounting (naturally absorbs pre-payout refunds/cancellations) but requires a payout/settlement concept that doesn't exist in Order or Identity today |

### Where it lives architecturally

- **Vendor's commission rate** → on the vendor's record in **Identity**. Given there's no separate `VendorProfile` entity, the two choices are: (a) a nullable `CommissionRateBps` field directly on `ApplicationUser`, meaningful only when `Role == Vendor`, following the existing pattern where all per-user data lives on one entity; or (b) introduce a new `VendorProfile` entity keyed by user id, which is cleaner separation but is new surface not justified unless more vendor-only fields are anticipated soon.
- **Per-order commission calculation & storage** → in **Order**, since that's where line totals are computed. This requires two additions that don't exist today: `OrderItemEntity` needs a `VendorId` (sourced from Product, since Order doesn't currently look up product data server-side — see gap above), and Order needs a way to fetch each vendor's rate from Identity, mirroring the existing `GetVendorNamesByIdsQuery` cross-service pattern.

## Recommendation

Hybrid rate model (platform default + per-vendor override), admin-set with no negotiation workflow, calculated at order placement.

This is the smallest change that's still realistic for a marketplace:

1. Add `CommissionRateBps` (nullable) to `ApplicationUser`, falling back to a configured platform default when unset. Admin-only endpoint to set/clear a vendor's override.
2. Add `VendorId` to `OrderItemEntity`, populated via a server-side Product lookup in `PlaceOrderCommandHandler` (which needs to start calling Product at all, since it currently trusts client-supplied item data wholesale — a pre-existing gap this would incidentally close).
3. Add a `CommissionAmount` per order line, computed at `OrderEntity.Create` time from each line's vendor rate, using a batch rate lookup from Identity analogous to `GetVendorNamesByIdsQuery`.

Leaves room to add a vendor-request negotiation workflow or settlement-based accrual later without a rewrite — those are additive on top of this shape, not replacements for it. Not recommended now: per-category/tiered rates or settlement-time accrual, since neither has a concrete requirement driving it yet and both add real complexity (category-rate conflicts, a payout system that doesn't exist).
