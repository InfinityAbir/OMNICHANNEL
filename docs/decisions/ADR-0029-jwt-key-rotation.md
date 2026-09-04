# ADR-0029: JWT Signing Key Rotation with an Overlap Window

**Status:** Accepted
**Date:** 2026-09-04

## Context

The single remaining gap explicitly tracked since Phase 15 (`docs/security.md`, `docs/phase-reports/phase-15.md`): "no JWT signing-key rotation overlap mechanism (single key, no `kid`-based multi-key validation) — rotating today invalidates every session immediately." The user asked for this fixed directly, as one of two remaining post-launch tasks (alongside data retention/account deletion).

## Decision

**A database-backed key ring, not a config-file key list.** Following the same pattern this
session already established for Data Protection keys (ADR-0028) and tenant secrets (ADR-0027),
`JwtSigningKey` rows live in Postgres (`jwt_signing_keys`), encrypted at rest via the same Data
Protection mechanism, rather than an env-var-edited list of keys. This makes rotation an
*operational action against the running system*, not a redeploy — consistent with "everything
dynamic, nothing hardcoded" from earlier in this session, now applied to the one piece of config
that was still a single static value.

**Exactly one primary key at a time, enforced by a unique filtered index.** `IsPrimary` has a
Postgres partial unique index (`WHERE "IsPrimary" = true`) — the database itself, not just
application logic, guarantees there's never an ambiguous "which key signs new tokens" state.

**Retiring is scheduling a future expiry, not deleting.** `JwtSigningKey.Retire(retiredAt)` sets a
*future* timestamp; the key keeps validating (though no longer signing) until that moment. The
overlap window only needs to exceed the access-token lifetime (15 minutes by default) plus clock
skew — refresh tokens are unaffected entirely, since they're opaque, DB-hashed, unrelated to JWT
signing (Phase 1's design). `Jwt:KeyRotationOverlapHours` (default 1h) is generous relative to that
15-minute floor.

**No HTTP endpoint — a CLI command mode instead.** Rotation is a platform-wide action affecting
every tenant's active sessions simultaneously; this codebase has no "platform superadmin" role
distinct from a tenant's own Owner/Admin (every role is tenant-scoped), so exposing rotation over
HTTP would mean either inventing a new cross-tenant admin role (real scope creep for a narrow
feature) or, worse, letting any tenant Owner rotate the *entire platform's* signing key. Neither is
acceptable. Instead, `dotnet Omnichannel.Api.dll --rotate-jwt-key` runs inside the already-built
`WebApplication` host (reusing its full DI container — DB, Data Protection, everything already
wired) and exits before `app.Run()`, never starting Kestrel. An operator runs it against the real
connection string — locally, or via Render's Shell into the running container.

**Signing and validation read the same in-memory snapshot — not the database independently.**
`IssuerSigningKeyResolver` (used for validating every incoming token) has no DI/async access, so
per-request DB reads aren't an option; `JwtSigningKeyCache` — kept warm by
`JwtSigningKeyRefreshService` on a 60-second interval — holds both the currently-valid key set
*and* the current primary. The two token generators (`JwtAccessTokenGenerator`,
`WidgetSessionTokenGenerator`) read the primary from this same cache rather than querying the
database live on every issuance. This was a real bug caught during testing, not a hypothetical:
an earlier version had signing read the database fresh on every call while validation read the
(slower-to-update) cache — a token signed immediately after a rotation, before the *signing
host's own* validation cache had refreshed, could fail to validate against its own very next
request. Sharing one cache snapshot makes that ordering bug impossible by construction, and
happened to also be exactly what the automated tests needed to stop being flaky under
parallel execution (see Consequences).

**Bootstrap seeds from the legacy `Jwt:SigningKey` config value if present, otherwise generates a
random key.** `Jwt:SigningKey` becomes fully optional — a brand-new deployment needs no signing
key config at all now; an upgrading one keeps validating tokens already issued under its old
static key by seeding the first DB row from that same value. `Jwt:SigningKey` being merely empty
(not missing) used to pass Program.cs's old null-check then fail confusingly on the first request
(`SymmetricSecurityKey` rejects a zero-length key only when lazily resolved) — tightened to
`string.IsNullOrWhiteSpace` earlier this session during Render deploy prep, and now moot for new
deployments since the value is optional entirely.

**Bootstrap uses the same Postgres advisory-lock pattern as `RoleSeeder`, not a plain
"check-then-insert."** A bare check-then-insert race across concurrently-starting processes (e.g.
several `WebApplicationFactory` test hosts starting at once against the shared test database)
produced the exact duplicate-key/deadlock failure mode `RoleSeeder` already hit and fixed with a
session-level `pg_advisory_lock` (a different fixed key than RoleSeeder's, so the two never
contend with each other). Bootstrapping a key ring is a rare startup-time operation; serializing
it costs nothing.

## Consequences

- `IAccessTokenGenerator`/`IWidgetSessionTokenGenerator` changed from synchronous to async
  (`GenerateAsync`) and their DI registration changed from Singleton to Scoped — a Singleton
  directly constructor-injecting a Scoped `IJwtSigningKeyStore` (used as the cache's fallback path)
  is a captive-dependency bug the DI container's own build-time validation catches in the
  Development environment (though not Testing/Production, which don't validate by default) — a
  disposed `AppDbContext` would otherwise be silently reused forever from the first resolution
  onward. Caught by running the new `--rotate-jwt-key` command locally in Development before
  considering this shippable, not by the test suite itself.
- Verified live, in order: unit tests for the domain entity's validity-window logic; an
  end-to-end test suite proving (a) a token issued before rotation keeps authenticating within the
  overlap window, (b) a token issued *after* rotation uses the new key immediately, and (c) a
  zero-overlap rotation immediately rejects the pre-rotation token (the boundary case proving the
  overlap window, not something else, is what keeps tokens alive above); and the CLI command
  itself run against real local Postgres with DI validation enabled.
- 281/281 backend tests green (7 new: 4 domain unit tests, 3 end-to-end rotation tests).
- No change to refresh-token behavior or lifetime — a rotation never forces a full re-login, only
  (with zero overlap, the pathological case) an access-token refresh, which the client already
  handles transparently via the existing refresh flow.
- `Jwt:KeyRotationOverlapHours` is the one new config knob, defaulted generously (1h) relative to
  the 15-minute access-token lifetime it needs to outlive; an operator can pass a different value
  by setting that config before running the rotate command.
