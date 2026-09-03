# Phase Report — Phase 0: Engineering Foundation

**Status:** Implementation complete. Awaiting user approval to proceed to Phase 1.
**Date:** 2026-09-03

## Implemented

- Clean Architecture solution: `Omnichannel.Domain` (no deps) → `Omnichannel.Application` →
  `Omnichannel.Infrastructure` (EF Core + Npgsql) → `Omnichannel.Api` (ASP.NET Core host), plus
  `Omnichannel.Contracts` for shared DTOs. One-way dependency direction enforced by project
  references.
- Central package management (`Directory.Packages.props`), `.editorconfig`, .NET analyzers
  (`AnalysisMode=Recommended`, `TreatWarningsAsErrors=true`).
- 4 test projects: `Omnichannel.UnitTests`, `IntegrationTests`, `ApiTests`, `SecurityTests`
  (xUnit), wired into the solution with correct project references.
- Angular 21 workspace (`web/`): routing, SCSS, strict TypeScript, Vitest, `@angular-eslint`.
  Default marketing template replaced with a minimal Phase 0 placeholder.
- `docker-compose.yml`: PostgreSQL 17, bound to `127.0.0.1:5432`, dev-only credentials, healthcheck.
- Configuration: `appsettings.json` / `appsettings.Development.json` / `.env.example`; no secrets committed.
- `Omnichannel.Api` host: `/health/live`, `/health/ready` (Postgres-backed); RFC 7807
  ProblemDetails error handling with no internal-detail leakage; secure headers middleware
  (nosniff, deny-frame, locked-down CSP, no `Server` header); CORS deny-by-default with
  configurable allowlist; API versioning scaffold (`Asp.Versioning`, no versioned endpoints yet).
- Structured logging (Serilog, console sink, correlation ID enrichment) + OpenTelemetry
  foundation (traces + metrics, ASP.NET Core/HttpClient/Runtime instrumentation, OTLP export
  opt-in via `OTEL_EXPORTER_OTLP_ENDPOINT`).
- GitHub Actions CI (`.github/workflows/ci.yml`): backend (restore/build/test/vulnerability
  scan) and frontend (install/lint/build/test/audit) jobs, backend job runs a real Postgres
  service container.
- 6 ADRs (`docs/decisions/`): 0001 modular monolith, 0002 PostgreSQL 17, 0005 multi-tenancy
  strategy, 0008 API versioning, 0009 background processing, 0010 local dev environment.
- `docs/` reference set: architecture, security, database, api, integrations, ai, deployment,
  troubleshooting.
- Root `README.md`: product narrative (what/why/who) plus setup instructions.
- Repository indexed into `codebase-memory-mcp` (422 nodes, 468 edges); persistent artifact at
  `.codebase-memory/graph.db.zst` committed for team sharing.

## Tests

- **Unit:** 1/1 passing (`Omnichannel.UnitTests` — scaffold smoke test; no domain rules exist yet).
- **Integration:** 1/1 passing (`Omnichannel.IntegrationTests` — `AppDbContext` connects to a
  real PostgreSQL 17 instance via Docker Compose).
- **API:** 2/2 passing (`Omnichannel.ApiTests` — `/health/live` returns 200; unknown route
  returns ProblemDetails, not a stack trace, via `WebApplicationFactory`).
- **Security:** 1/1 passing (`Omnichannel.SecurityTests` — baseline security headers present and
  correct on every response).
- **E2E (Playwright):** not applicable this phase — no UI exists beyond a placeholder page.
  Applies starting Phase 3 (Unified Inbox UI).

All 5 backend tests pass. `dotnet build`: 0 warnings, 0 errors. Angular: `ng build`, `ng lint`,
`ng test` (2/2) all pass.

## Security Review

Performed against AGENTS.md's checklist, scoped to what exists in this phase (no
auth/tenant/webhook/AI surface exists yet — those reviews apply starting Phase 1, 5/6, and 10
respectively).

- **Secrets:** none committed. `.env.example` has placeholders only. The dev-only Postgres
  password in `appsettings.Development.json` matches `docker-compose.yml`'s well-known local
  default — not a real credential, documented as such.
- **Configuration:** Swagger/dev-only surfaces gated to Development; production CORS allowlist
  starts empty (deny-by-default) and must be set explicitly per environment.
- **Docker exposure:** Postgres bound to `127.0.0.1:5432` only, not reachable externally.
- **Error handling:** unhandled exceptions never leak internals — verified by
  `ApiTests.UnknownRoute_ReturnsProblemDetails_NotStackTrace`.
- **Headers/CORS:** verified by `SecurityTests.Response_IncludesBaselineSecurityHeaders`.
- **Dependencies:** `dotnet list package --vulnerable --include-transitive` → 0 findings.
  `npm audit` → 0 vulnerabilities.
- **Public repo discipline:** `.gitignore` excludes `.env*` (except `.env.example`), build
  output, `node_modules`; verified via `git status` before staging — no unexpected files staged.

**Findings:** none high/critical. **Remaining:** none within Phase 0 scope.

## Performance Review

No business logic, queries, or endpoints exist yet beyond health checks — nothing to measure.
Index and query-shape decisions are locked in ADR-0005/ADR-0002 for Phase 1 to build against
correctly from the start (composite indexes leading with `TenantId`).

## Architecture Review

Matches PRD §10 project layout and AGENTS.md's Clean Architecture / modular-monolith
requirement. Dependency direction verified by inspection of `.csproj` `ProjectReference`
entries — no inward layer references an outer one.

## Known Limitations

- No domain entities, no authentication, no channel adapters, no AI code — intentional, each is
  scoped to its own later phase.
- No versioned/public API endpoints yet (only operational health checks).
- `angular-eslint` v22 installed against Angular v21 (CLI's current default) — cosmetic
  version-mismatch warning at `ng add` time only; linting itself runs correctly.
- OpenAPI document generation deliberately not wired up yet — see ADR-0008's consequences.

## Files/Modules Changed

Initial commit — see repository tree. Full solution scaffold, Angular workspace, Docker Compose,
CI workflow, `docs/` set, ADRs, this report.

## Next Phase

Phase 1 — Identity + Multi-Tenancy (registration/login, password security, token/refresh-token
handling, tenant creation, memberships, roles/permissions, tenant context, authorization
policies, basic user profile), per PRD §60. Requires the mandatory attack tests (cross-tenant
access, permission escalation, expired/revoked tokens) to all fail correctly before that phase
can close.

**Requesting approval to commit/push this phase and begin Phase 1.**
