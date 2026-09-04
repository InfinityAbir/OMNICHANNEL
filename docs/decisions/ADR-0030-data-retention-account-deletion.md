# ADR-0030: Data Retention and Account Deletion

**Status:** Accepted
**Date:** 2026-09-05

## Context

The last item explicitly tracked as a gap since Phase 15 (`docs/privacy.md`: "no automated
retention/deletion policy exists yet... a tenant/account has no self-service 'delete my data' or
'delete my account' flow") and requested directly by the user alongside JWT key rotation
(ADR-0029). Two distinct things needed: a business closing its whole account, and an individual
user leaving one.

## Decision

**Two separate flows, not one.** "Delete my account" (any user, any time, `DELETE
/api/v1/users/me`) and "delete this business" (tenant Owner only, `POST`/`DELETE
/api/v1/tenant/deletion`) are different operations with different blast radii — one person's
membership vs. an entire business's conversations, contacts, and every setting. Conflating them
would either let a non-owner accidentally take down a shared business account, or make an
individual's own departure needlessly heavy.

**A 14-day grace period, never instant deletion, for both.** `Tenant.ScheduleDeletion` sets a
*future* `ScheduledDeletionAt`, not an immediate purge — the same "schedule, don't destroy on the
first click" shape ADR-0029 already used for JWT key retirement. A business's data is exactly the
kind of thing an accidental click (or a moment of frustration) shouldn't be able to destroy with
no way back; 14 days is long enough to notice and reconsider, short enough that "we said we'd
delete it" stays true within a reasonable window.

**Requesting tenant deletion blocks new logins to that tenant immediately, without touching any
data until the purge actually runs.** `AuthService.GetActiveTenantContextAsync` (already used by
both Login and Refresh) now filters to `TenantStatus.Active` tenants — a side effect of this work
that also, for the first time, actually enforces the pre-existing `Suspended` status (previously
defined but never checked anywhere in the codebase, a latent gap this fix closes incidentally). A
user who belongs to several tenants simply falls through to their next active membership rather
than being blocked outright.

**The purge job deletes generically, by reflection over `ITenantOwned`, not a hand-maintained
list.** `TenantDataPurgeService` walks `AppDbContext.Model.GetEntityTypes()` for every type
implementing `ITenantOwned` and runs `ExecuteDeleteAsync` scoped to the due tenant's id — the same
technique `AppDbContext.OnModelCreating` already uses to apply the tenant query filter to every
such type. A future phase's new tenant-owned entity is purged automatically; nothing to remember
to add here. `AuditLog` is the one deliberate exclusion — kept so the tenant's own deletion event
(and everything that led to it) survives in the audit trail, rather than the purge erasing the
record of itself. The `Tenant` row itself is kept too (marked `TenantStatus.Deleted`, not
removed), so those audit entries still resolve to a real tenant id instead of dangling.

**A user's own account is anonymized, not hard-deleted — but their credential record is.**
`Domain.Identity.User` (the business-facing profile referenced by `InternalNote.AuthorUserId`,
`Notification.UserId`, `AuditLog.ActorUserId`, `Conversation.AssignedUserId`) is scrubbed
(`User.Anonymize`: email → a per-user-unique `deleted-{id}@deleted.invalid`, display name →
"Deleted user") rather than removed, since those other tables reference its id and must keep
resolving to *something* — the same reasoning the purge job applies to the `Tenant` row. The
separate Identity/credential record (email, password hash — Infrastructure's `ApplicationUser`,
via `IIdentityService.DeleteUserAsync`) IS actually deleted: nothing about a login credential needs
to survive account deletion, and deleting it is what makes the account genuinely unable to log in
again, immediately, not just "renamed."

**Self-deletion is blocked only for a sole Owner of a tenant that still has other members** —
validated fully before anything is mutated, so a blocked attempt has zero side effects. There'd be
no one left able to manage that business otherwise. If they're the sole Owner of a tenant with
*no* other members at all, deleting their account also schedules that now-ownerless tenant for
deletion (same 14-day grace period) — leaving an unreachable, ownerless-but-still-active tenant
behind would be worse than cleaning it up.

**`tenant.delete` is the first genuinely Owner-exclusive permission in the catalog.** Owner and
Admin previously shared every permission (`RoleSeeder`'s own comment noted this was "until one
exists"). Scheduling or cancelling deletion of the whole business account is irreversible-by-
anyone-else in a way no other `tenant.update` action is, so it's the natural first split.

**A real, separate bug found and fixed while building this: `RoleSeeder` only ever inserted roles
into an empty table, never reconciled an already-seeded one.** Adding `tenant.delete` to Owner's
permission set in code did nothing to the shared dev/test database's already-existing `Owner` row
— caught immediately by a real, non-flaky test failure (a registered Owner getting 403 on their
own tenant-deletion request), not by inspection. This would have hit Render's already-seeded
production database identically on this same deploy. Fixed by making `RoleSeeder` reconcile every
system role's permission set to the current code-defined catalog on every startup, not just insert
missing roles — so a future permission catalog change reaches an already-running deployment
automatically, the same "everything dynamic, no manual DB fix required" principle this session
has applied throughout.

## Consequences

- Verified live end-to-end in the browser against the real backend, not just via the automated
  suite: registered a tenant, scheduled its deletion (badge and countdown date rendered
  correctly), cancelled it (reverted to Active), then deleted the owning user's own account —
  which correctly cascaded into scheduling the now-ownerless tenant for deletion too — and
  confirmed the deleted account's credentials are immediately rejected on a fresh login attempt
  ("Incorrect email or password").
- 296/296 backend tests green (16 new: domain unit tests for `Tenant`'s deletion state machine and
  `User.Anonymize`, and end-to-end tests covering scheduling/cancelling/permission-gating,
  login-blocked-while-pending, the purge job actually removing operational data while preserving
  the audit trail and tenant row, and both self-deletion paths — blocked-sole-owner and
  cascading-solo-owner).
- A minimal "Account" settings screen (`/settings/account`) makes both flows actually reachable by
  a real user, not just callable via the API directly — consistent with every other feature this
  session shipped with a working UI, not just an endpoint.
- No automated backup/archival of purged data — this is a genuine, intentional erasure, consistent
  with what "delete my data" is supposed to mean. `docs/disaster-recovery.md`'s own backup/restore
  procedure is the only path back, and only for however recently a backup was taken relative to
  the purge.
- The purge job polls hourly (`TenantDataPurgeService`, a `BackgroundService`) — a tenant's data
  is removed sometime within an hour of its grace period elapsing, not to the second. Nothing
  about this feature is time-sensitive enough to need finer granularity.
