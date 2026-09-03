# Security

Living document, updated after every phase's mandatory security review (AGENTS.md §Mandatory
security review).

## Phase 1 controls (Identity + Multi-Tenancy)

- **Password policy**: min length 10, requires digit/upper/lower/non-alphanumeric (Identity
  options in `Infrastructure/DependencyInjection.cs`).
- **Brute-force protection**: account lockout after 5 failed attempts (15 min), plus a per-IP
  rate limiter on the whole `/api/v1/auth/*` group (30 req/min — see `Program.cs` for why this
  is the secondary, not primary, defense).
- **Token handling**: JWT access tokens (HMAC-SHA256, 15 min default lifetime), opaque refresh
  tokens stored only as a SHA-256 hash with rotation-on-use. See
  [ADR-0007](decisions/ADR-0007-identity-and-auth-model.md).
- **Enumeration resistance**: login with an unknown email and login with a wrong password return
  the identical 401 shape; password-reset requests always return the same response regardless of
  whether the email is registered. Regression-tested (`AuthEndpointsTests`,
  `AuthSecurityTests.RevokedRefreshToken_...`).
- **Tenant isolation**: EF Core global query filter on every `ITenantOwned` entity, tenant
  resolved server-side from JWT claims only (`ITenantContext`/`ScopedTenantContext`) — never
  trusted from client input. Regression-tested at the data-access layer
  (`TenantIsolationTests.Memberships_ForTenantA_NeverIncludeTenantBsRows`). One deliberate,
  documented exception: login/refresh's tenant-discovery query uses `IgnoreQueryFilters()`
  because it runs before a tenant context exists — see ADR-0007.
- **Two real bugs found and fixed by this phase's own testing** (not merely "found the design
  was fine on paper"): the query-filter exception above (login/refresh returned 401 for *every*
  successful login before the fix), and `JwtBearerOptions.MapInboundClaims` needing to be
  disabled (otherwise `sub` gets silently remapped, breaking `/users/me` for every authenticated
  request). Both now have regression tests.
- **Secrets**: JWT signing key and SMTP credentials live only in local `dotnet user-secrets`
  (dev) / environment variables (elsewhere) — never in `appsettings.json`, `.env.example`, or
  committed anywhere. See [ADR-0011](decisions/ADR-0011-email-delivery.md) for the email
  provider decision and its own caveat (a credential was pasted directly in chat this session;
  flagged to the user, worth rotating independent of how the app stores it).
- **Email verification**: off by default (`Identity:RequireConfirmedEmail=false`, per PRD §13's
  "if enabled" wording) — the confirmation email + endpoint work regardless, so turning it on
  later is a config change, not new code.

## Phase 0 baseline controls (still in effect)

- **Secrets**: nothing committed. `.env.example` has placeholders only, never real credentials.
  `appsettings.Development.json` contains a well-known *dev-only* Postgres password matching
  `docker-compose.yml`'s default — not a real secret, never reused outside local dev.
- **Error handling**: unhandled exceptions become RFC 7807 ProblemDetails; internal exception
  details, stack traces, and type names never reach the client. See
  `src/Omnichannel.Api/Middleware/ProblemDetailsExceptionHandler.cs`.
- **Transport/headers**: HTTPS redirection; HSTS outside Development; `X-Content-Type-Options:
  nosniff`; `X-Frame-Options: DENY`; locked-down CSP (`default-src 'none'`); no `Server` header.
  See `src/Omnichannel.Api/Middleware/SecurityHeadersMiddleware.cs`, regression-tested in
  `Omnichannel.SecurityTests/SecurityHeadersTests.cs`.
- **CORS**: deny-by-default; explicit origin allowlist from `Cors:AllowedOrigins` config, empty
  in `appsettings.json` (production must set it explicitly), `http://localhost:4200` only in
  `appsettings.Development.json`.
- **Dependencies**: `dotnet list package --vulnerable` and `npm audit` run in CI on every push
  (`.github/workflows/ci.yml`).
- **Docker exposure**: Postgres bound to `127.0.0.1:5432` only, not `0.0.0.0` — not reachable
  from outside the host.
- **Repo is public** (`github.com/InfinityAbir/OMNICHANNEL`) — extra discipline required: never
  commit `.env`, credentials, tokens, or internal-only URLs. `.gitignore` excludes `.env*`
  (except `.env.example`), `bin/`, `obj/`, `node_modules/`.

## Logging policy

Structured logging (Serilog) with correlation IDs. Do not log message content, tokens,
credentials, or full request/response bodies containing customer data — this rule is set now so
it's inherited by every later phase instead of retrofitted once real customer data exists.

## Not yet applicable (tracked for their phase)

- Object-level authorization (IDOR/BOLA) on route-parameterized resources — no such endpoint
  exists yet (`/users/me` takes no id); PRD §60's "modified object ID" attack test applies
  starting Phase 2 (Conversations/Contacts) and must be added there.
- Permission-based endpoint authorization in practice — the policy provider
  (`PermissionPolicyProvider`) is wired and ready, but no endpoint uses it yet since there's no
  business data to protect; first real use lands in Phase 2.
- Webhook signature verification, replay protection — Phase 6+.
- AI-specific threats (prompt injection, cross-tenant retrieval leakage, output validation) —
  Phase 10+; see [ai.md](ai.md).
- File upload/attachment security — Phase 5+ (website chat is the first channel with
  attachments).

## Security review log

**Phase 1** (2026-09-03) — scope: auth, tenancy, permission plumbing. Findings: 2 (see above,
both fixed with regression tests before phase close). Attack tests run: unauthenticated →
protected endpoint (pass), expired token → protected endpoint (pass), revoked refresh token →
refresh (pass), tampered JWT claim → rejected by signature check (pass). Deferred to Phase 2:
agent → admin endpoint, modified object ID → another tenant's object (no object-with-id endpoint
exists yet to attack meaningfully).

**Phase 0** (2026-09-03) — scope: config, Docker, error handling, headers, CORS, dependency
scanning. No high/critical findings.
