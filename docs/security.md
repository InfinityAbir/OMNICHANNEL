# Security

Living document, updated after every phase's mandatory security review (AGENTS.md §Mandatory
security review). This is the Phase 0 baseline.

## Phase 0 baseline controls

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

- Authentication/authorization, token handling, refresh rotation — Phase 1.
- Tenant isolation enforcement in queries/commands — Phase 1 onward (strategy locked in
  [ADR-0005](decisions/ADR-0005-multi-tenancy-strategy.md); PRD §60's mandatory attack tests
  apply starting Phase 1).
- Webhook signature verification, replay protection — Phase 6+.
- AI-specific threats (prompt injection, cross-tenant retrieval leakage, output validation) —
  Phase 10+; see [ai.md](ai.md).
- File upload/attachment security — Phase 5+ (website chat is the first channel with
  attachments).

## Phase 0 security review

Performed 2026-09-03 against AGENTS.md's checklist, scoped to what exists in this phase
(config, Docker, error handling, headers, CORS, dependency scanning — no auth/tenant/AI/webhook
surface exists yet to review). No high/critical findings. See
`docs/phase-reports/phase-0.md` for the full review record.
