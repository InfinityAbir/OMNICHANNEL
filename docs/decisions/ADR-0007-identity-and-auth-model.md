# ADR-0007: Identity and auth model

**Status:** Accepted
**Date:** 2026-09-03

## Context

PRD §13 requires secure registration/login, strong password hashing, refresh-token rotation,
brute-force protection, and (later) MFA-readiness. PRD §12 requires permission-based
authorization, not scattered role-name checks. AGENTS.md's coding rules say keep Domain
independent of framework details, and avoid unnecessary abstractions.

## Decision

- **ASP.NET Core Identity** (`IdentityCore<ApplicationUser>` + `AddEntityFrameworkStores`) owns
  password hashing, lockout, and email-confirmation/reset token generation — no
  home-grown crypto. `ApplicationUser : IdentityUser<Guid>` lives in
  `Infrastructure/Identity/`, framework-coupled by design.
- **Domain stays framework-free**: `Domain.Identity.User` is a separate, plain profile entity
  (display name, email, timestamps) sharing the same `Id` as `ApplicationUser` but with zero
  reference to ASP.NET Core Identity. Application never references `ApplicationUser` or
  `UserManager` directly — it depends on `IIdentityService`, a thin facade Infrastructure
  implements over `UserManager`/`SignInManager`. This is the one place we pragmatically accept
  Application depending on a facade over a framework concern (documented here rather than
  silently done) rather than reinventing password/lockout/token logic behind a second
  abstraction layer.
- **Authorization is permission-string based**, not ASP.NET Identity's own Role/Claim tables
  (which are unused — `AppDbContext` extends `IdentityUserContext`, not `IdentityDbContext`).
  Our own `Role`/`TenantMembership` model (PRD §12/§14) is the source of truth; permissions are
  embedded as `perm` claims in the access token and checked via a dynamically-resolved
  `IAuthorizationPolicyProvider` (`PermissionPolicyProvider`), so future endpoints just write
  `.RequireAuthorization(PermissionKeys.ConversationsReply)` with no per-permission policy
  registration needed.
- **JWT access tokens** (HMAC-SHA256, 15 min default lifetime) carry `sub`, `email`, `tenant_id`,
  and `perm` claims. **Refresh tokens** are opaque random 512-bit values; only their SHA-256 hash
  is persisted (`Infrastructure.Identity.RefreshToken`), with rotation-on-use (old token revoked,
  new one issued, `ReplacedByTokenId` chains them) and a 30-day default lifetime.
- **Tenant resolution for login/refresh must ignore the tenant query filter.** These flows run
  *before* a tenant context exists — discovering which tenant a verified user belongs to is the
  point of the query. `AuthService.GetActiveTenantContextAsync` explicitly calls
  `IgnoreQueryFilters()` on `db.Memberships`, scoped by a server-verified `UserId` (never
  client-supplied), documented inline. This was found and fixed via the mandatory Phase 1
  security/functional testing — see the Phase 1 report.
- **`MapInboundClaims = false`** on the JWT bearer handler. Without it, ASP.NET Core silently
  remaps short claim names (`sub`, etc.) to legacy XML-namespace URIs when building
  `ClaimsPrincipal`, breaking any code that reads `JwtRegisteredClaimNames.Sub` directly — also
  found via Phase 1 testing.

## Alternatives considered

- **A single `User` entity implementing `IdentityUser` directly, used everywhere.** Rejected:
  would leak `Microsoft.AspNetCore.Identity` into Domain/Application, violating the
  framework-independence rule for no real benefit at this scale.
- **ASP.NET Core Identity's own Role/Claim system for authorization.** Rejected: PRD explicitly
  wants permission-based checks, not role-name checks, and Identity's role model doesn't map
  cleanly onto "role has a permission set, evaluated per-tenant-membership."
- **Storing raw refresh tokens.** Rejected outright — AGENTS.md/PRD require tokens never be
  logged or stored recoverably; only the hash is persisted.

## Consequences

- Any future entity/flow that needs "find a user's tenant before authentication exists" must
  follow the same `IgnoreQueryFilters()` + verified-identity-predicate pattern, not just add an
  ad-hoc unscoped query.
- Permission checks are a single source of truth (`PermissionKeys` + seeded `Role.Permissions`)
  — adding a new permission means updating the catalog and the relevant role's seed data, not
  hunting for scattered `if (role == "Admin")` checks.
- Password reset invalidates all of a user's refresh tokens (`RevokeAllForUserAsync`) — a
  deliberate "reset kills every session" choice, not yet configurable per PRD's silence on this.
