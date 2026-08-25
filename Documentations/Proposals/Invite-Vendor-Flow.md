# Invite Vendor Flow — Draft

**Status:** Draft / proposal, not implemented. Written for discussion — this is not a description of current behavior. Reflects the "+ Invite vendor" UI sketched into the [ShopFlow Page Redesigns mockup](https://claude.ai/code/artifact/288abc0f-0b1b-4269-bf31-a9f8643afa94) admin Vendors page, which only had the button wired to nothing before this proposal.

## Current state

There is no invite concept anywhere in Identity today. The only path to becoming a vendor is a two-step, fully manual process:

1. A user self-registers as a normal `Customer` via `RegisterUserCommandHandler` ([Identity.Application/Commands/RegisterUserCommandHandler.cs](../../Services/Identity/Identity.Application/Commands/RegisterUserCommandHandler.cs)) — there's no way to register directly as a vendor.
2. An admin manually promotes that existing user by calling `POST /api/admin/users/{id}/assign-role` ([UsersController.cs](../../Services/Identity/Identity.Api/Controllers/UsersController.cs)), which runs `AssignRoleCommandHandler` ([Identity.Application/Commands/AssignRoleCommand.cs](../../Services/Identity/Identity.Application/Commands/AssignRoleCommand.cs)) and just flips `ApplicationUser.Role` on an already-existing account.

There's no notion of "pending" anywhere in the domain — `ApplicationUser` ([Identity.Domain/Entities/ApplicationUser.cs](../../Services/Identity/Identity.Domain/Entities/ApplicationUser.cs)) goes straight from `Customer` to `Vendor` the moment an admin calls that endpoint. The mockup's admin Vendors table draws a `Pending review` status with an `Approve` action (e.g. the "Cedar & Salt" row) that has no backing state today — nothing produces that status.

## What the mockup implies

The `+ Invite vendor` button now opens a modal (added in this session) collecting business name, contact email, and an optional starting commission rate (ties into [Vendor-Commission-System.md](Vendor-Commission-System.md)). Submitting it — in the mockup — appends a row with an `Invited` status chip and a disabled "Resend invite" action, reusing the same `.status-chip.pending` styling already used for `Pending review`.

That UI implies two states that don't exist in the domain yet:

- **`Invited`** — an invite has been sent, but the invitee hasn't completed registration. No `ApplicationUser` exists yet.
- **`Pending review`** (already drawn, unbacked) — the invitee has registered and is now a `Vendor`-role user, but an admin hasn't approved them to actually sell yet.

## Design

### New entity — `Identity.Domain/Entities/VendorInvite.cs`

```csharp
public class VendorInvite
{
    public Guid Id { get; private set; }
    public string Email { get; private set; }
    public string BusinessName { get; private set; }
    public int? CommissionRateBps { get; private set; }   // null = platform default, see Vendor-Commission-System.md
    public string Token { get; private set; }              // single-use, unguessable
    public InviteStatus Status { get; private set; }        // Sent, Accepted, Expired, Revoked
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
```

A separate entity rather than a field on `ApplicationUser`, since an invite doesn't have a user yet — there's nothing to attach it to until it's accepted.

### New endpoints — `Identity.Api`

- `POST /api/admin/vendor-invites` `{ email, businessName, commissionRate? }` (admin-only) — creates a `VendorInvite`, generates a token, and (via Notification.Api, following the existing pattern in `notification-email-conventions`) emails a signup link containing the token. Row appears in admin Vendors table as `Invited`.
- `GET /api/vendor-invites/{token}` (unauthenticated) — validates the token (exists, not expired, not already accepted) so the signup page can prefill business name/email before the invitee sets a password.
- `POST /api/auth/register-via-invite` `{ token, password }` (unauthenticated) — validates the token, creates the `ApplicationUser` directly with `Role = Vendor` (skipping the normal Customer default in `ApplicationUser.Create`), marks the invite `Accepted`, and issues a JWT like `RegisterUserCommandHandler` does today. The new vendor lands in `Pending review`, not `Active`.

### Status derivation, not a new enum on `ApplicationUser`

Rather than adding a third "pending" value to `UserRole`, the admin Vendors table's status column is derived: `Invited` = a `VendorInvite` with `Status = Sent`; `Pending review` = an `ApplicationUser` with `Role = Vendor` and no approval flag set yet; `Active`/`Suspended` = the existing role plus a suspension flag. This means `ApplicationUser` needs one new field — `bool IsApproved` (or equivalently, keep using `AssignRoleCommand`'s existing suspend/reinstate pattern but gate initial `Vendor` role grants behind an `Approve` action instead of granting full access immediately). The `Approve`/`Suspend`/`Reinstate` actions already sketched in the mockup's `admin-vendors` JS (`rowTransition`) map directly onto this — no new admin interaction pattern needed, just a new gate at vendor creation time.

### Token security

The token needs to be cryptographically random (not a sequential id), single-use (flip `Status` to `Accepted` on redemption, `GET`/`POST` both reject non-`Sent` invites), and time-limited (`ExpiresAt`, e.g. 7 days) — this is the main new attack surface, since `register-via-invite` is unauthenticated by necessity.

## Trade-off vs. current manual promotion

| | Current (manual promote) | Invite flow |
|---|---|---|
| Vendor onboarding | Admin creates account behind the scenes or asks the user to register as Customer first, then promotes | Admin sends one invite; vendor completes their own signup |
| New surface | None | `VendorInvite` entity, 3 endpoints, 1 unauthenticated token-validation endpoint, an email template |
| Approval gate | Implicit — promotion IS activation, no review step | Explicit — registration and approval are separate steps, matching the mockup's `Pending review` state |
| Risk | Admin must have the user's info to create/promote correctly | Unguessable, expiring, single-use token is the only new thing to get right |

## Recommendation

Build it — the current flow (customer registers, admin manually promotes) has no way to actually invite someone who doesn't already have a ShopFlow account, and the mockup's `Pending review`/`Approve` status already assumes an approval gate exists, which today's `AssignRoleCommand` doesn't provide. This is a natural pairing with [Vendor-Commission-System.md](Vendor-Commission-System.md), since the invite is the natural place to set a vendor's starting commission rate. Scope the first version to invite → register → pending review → approve; skip invite expiry reminders or resend-with-new-token handling until there's a real need for them.
