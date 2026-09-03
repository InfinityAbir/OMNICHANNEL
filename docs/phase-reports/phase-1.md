# Phase Report — Phase 1: Identity + Multi-Tenancy

**Status:** Implementation complete. Awaiting user approval to proceed to Phase 2.
**Date:** 2026-09-03

## Scope / PRD references

PRD §60 (Phase 1), §11–13 (multi-tenancy, roles/permissions, identity/auth). AGENTS.md's phase
work sequence and mandatory security review.

## Implemented

- **Domain**: `Tenant`, `User` (framework-free profile, linked 1:1 to the Identity credential
  record by shared `Id`), `TenantMembership` (tenant-owned, many-to-many-capable), `Role` +
  `PermissionKeys` (the 16-key catalog from PRD §12, seeded into 4 fixed system roles).
- **Identity/auth**: ASP.NET Core Identity for credentials only (password hashing, lockout);
  JWT access tokens (15 min) + rotating, hashed-only refresh tokens (30 days); permission-string
  authorization resolved dynamically (`PermissionPolicyProvider`) so future endpoints just
  declare `.RequireAuthorization(PermissionKeys.X)`. Full rationale in
  [ADR-0007](../decisions/ADR-0007-identity-and-auth-model.md).
- **Endpoints**: `POST /api/v1/auth/{register,login,refresh,logout}`, `GET
  /api/v1/auth/confirm-email`, `POST /api/v1/auth/password-reset/{request,confirm}` +
  form-based link-follow variant, `GET /api/v1/users/me`.
- **Self-service tenant signup**: registration creates User + Tenant + Owner membership in one
  transaction, per the user's explicit choice over invite-only provisioning.
- **Real email delivery**: MailKit → Gmail SMTP, designed HTML templates (monochromatic, single
  accent, plain-text fallback) for confirmation and password reset — upgraded mid-phase from the
  originally-planned logging stub once the user supplied working credentials. See
  [ADR-0011](../decisions/ADR-0011-email-delivery.md).
- **Rate limiting** (30 req/min/IP on the auth route group) + Identity account lockout (5 failed
  attempts / 15 min) as brute-force defenses.
- **Migration** `InitialIdentityAndTenancy`, applied to local dev Postgres.
- Query optimizations: `/users/me` and the login/refresh tenant-discovery path each collapsed
  from 3–4 sequential round-trips to a single joined query (standing "optimize every endpoint"
  instruction).

## Tests

- **Unit**: 9/9 (domain invariants — `Tenant`, `TenantMembership`, `PermissionKeys` catalog).
- **Integration**: 1/1 (Postgres connectivity, carried from Phase 0).
- **API**: 11/11 (`AuthEndpointsTests`, `HealthEndpointTests`) — register, duplicate email, weak
  password, login (correct/wrong password, unknown email — enumeration-safe), refresh (valid/
  garbage token), `/users/me` (valid token).
- **Security**: 6/6 — baseline headers (Phase 0), plus PRD §60's mandatory attack tests scoped to
  what exists this phase: unauthenticated → protected endpoint, expired token → protected
  endpoint, revoked refresh token → refresh, tampered JWT claim → rejected (signature check), and
  a direct data-layer proof that the tenant global query filter actually isolates rows
  (`TenantIsolationTests`). **Deferred to Phase 2** (documented, not skipped): "agent → admin
  endpoint" and "modified object ID → another tenant's object" — no object-with-id endpoint
  exists yet to attack meaningfully; Phase 2's Conversations/Contacts must add these.

All 27 tests pass. `dotnet build`: 0 warnings, 0 errors, strict analyzers on
(`TreatWarningsAsErrors`). `dotnet list package --vulnerable`: 0 findings.

## Security Review

Performed against AGENTS.md's checklist for what exists this phase.

**Findings — both confirmed, both fixed, both regression-tested:**

1. **(High, functional+security) EF Core global tenant query filter silently broke every
   login/refresh.** The filter correctly scopes tenant-owned queries to the authenticated
   caller's tenant — but login/refresh run *before* authentication establishes a tenant, so the
   filter defaulted to `Guid.Empty` and matched nothing. Every successful login/refresh returned
   a generic "invalid" error. Fixed with a scoped, documented `IgnoreQueryFilters()` call in
   `AuthService.GetActiveTenantContextAsync`, safe because the query is bounded by a
   server-verified `UserId` (a password that already passed Identity's check, or a refresh token
   already found by its hash) — never client-supplied. Regression test:
   `AuthEndpointsTests.Login_WithCorrectPassword_ReturnsTokens` /
   `Refresh_WithValidToken_ReturnsNewTokenPair`.
2. **(High, functional+security) `JwtBearerOptions.MapInboundClaims` default silently remapped
   the `sub` claim**, breaking every authenticated request that reads
   `JwtRegisteredClaimNames.Sub` directly (`/users/me`). Fixed by setting
   `MapInboundClaims = false`. Regression test: `AuthEndpointsTests.Me_WithValidToken_...`.

Both were caught by this phase's own test-writing, not by manual review — the initial test suite
had a coverage gap (no test exercised "login succeeds" or "authenticated request succeeds"; only
error paths were covered) that let both ship undetected until deliberately closing that gap. No
other high/critical findings.

**Other controls verified**: enumeration resistance (login/register/password-reset all
constant-shape regardless of whether the account exists), refresh tokens never stored raw (SHA-256
hash only), password reset revokes all outstanding sessions, rate limiting + lockout as layered
brute-force defense, secrets (JWT signing key, SMTP credentials) in `dotnet user-secrets` only,
never committed.

**Residual/accepted for this phase**: Owner and Admin share identical permissions (no
owner-exclusive permission exists in the catalog yet — documented in `RoleSeeder`, not a defect).
IDOR/object-level-authorization attack tests deferred to Phase 2 per above.

## Performance Review

Two multi-round-trip query patterns identified and collapsed to single joined queries
(`/users/me`: 4→1; login/refresh tenant discovery: 3→1 sequential DB calls), per the standing
"optimize every endpoint" instruction. No other endpoints exist yet to review.

## Architecture Review

Matches ADR-0001/0005/0007. Domain stays framework-free (`Domain.Identity.User` has zero
ASP.NET Core Identity references); Application depends on `IIdentityService`/`IAppDbContext`
abstractions, not concrete Infrastructure types, with the one documented pragmatic exception
(Application → `Microsoft.EntityFrameworkCore` for `DbSet<T>` typing, per ADR's "don't hide EF
Core capabilities behind a generic repository" guidance).

## Migrations / Configuration Changes

- Migration: `InitialIdentityAndTenancy`.
- New config sections: `Jwt` (Issuer/Audience/lifetimes — non-secret; `SigningKey` via
  user-secrets/env only), `Smtp` (all via user-secrets/env only), `Identity:RequireConfirmedEmail`
  (default `false`).
- `.env.example` updated with the new (empty-by-default) secret variable names.

## ADRs / Docs Updated

ADR-0007 (identity/auth model), ADR-0011 (email delivery). `docs/architecture.md`,
`docs/security.md`, `docs/database.md`, `docs/api.md`, root `README.md` — all updated for Phase 1
state.

## Known Limitations

- API versioning middleware registered but not yet driving actual route resolution (hard-coded
  `/api/v1/` prefix) — fine with a single version; revisit at v2.
- Password-reset web form is plain HTML (no Angular yet) — Phase 3 replaces it.
- No Playwright E2E — Phase 1 has no UI to test (backend-only phase, confirmed with user).
- Gmail SMTP is fine at current volume; revisit before real production volume (ADR-0011).

## Files/Modules Changed

`src/Omnichannel.Domain/{Common,Tenancy,Identity,Authorization}/*`,
`src/Omnichannel.Application/{Abstractions,Auth}/*`,
`src/Omnichannel.Infrastructure/{Identity,Email,Persistence}/*` (incl. migration),
`src/Omnichannel.Api/{Endpoints,Authorization,Validation}/*`, `Program.cs`, `appsettings*.json`,
`src/Omnichannel.Contracts/Auth/*`, 4 test projects, `docs/decisions/ADR-{0007,0011}`,
`docs/{architecture,security,database,api}.md`, `README.md`.

## Next Phase

Phase 2 — Core Conversations + Contacts (PRD §61): Contacts, Conversations, Messages,
Assignments, Tags, Internal notes, status transitions, pagination, search foundations, audit
logging. This is also where the deferred IDOR/object-level-authorization attack tests land, since
it's the first phase with object-with-id endpoints to attack.

**Requesting approval to commit/push this phase and begin Phase 2.**
