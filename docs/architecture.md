# Architecture

Status as of Phase 0 (foundation scaffold — no business logic yet).

## Shape

Modular monolith, Clean Architecture. See [ADR-0001](decisions/ADR-0001-modular-monolith.md).

```
Omnichannel.sln
src/
  Omnichannel.Domain/            no dependencies — business rules live here (Phase 1+)
  Omnichannel.Application/       -> Domain — use cases / orchestration
  Omnichannel.Infrastructure/    -> Application — EF Core, Npgsql, provider adapters
  Omnichannel.Api/               -> Application, Infrastructure, Contracts — ASP.NET Core host
  Omnichannel.Contracts/         shared DTOs, no Domain internals (future Android reuse)
tests/
  Omnichannel.UnitTests/         -> Domain, Application
  Omnichannel.IntegrationTests/  -> Infrastructure, Application (real Postgres via docker-compose)
  Omnichannel.ApiTests/          -> Api (WebApplicationFactory)
  Omnichannel.SecurityTests/     -> Api (WebApplicationFactory, adversarial checks)
web/                             Angular workspace (routing, SCSS, strict TS, Vitest)
```

Dependency direction is inward-only and enforced by project references. Domain must never
reference ASP.NET Core, EF Core, or any provider SDK.

## Request pipeline (Phase 0 baseline)

```
Request
  -> HTTPS redirection
  -> Security headers (nosniff, deny-frame, locked-down CSP, no Server header)
  -> CORS (explicit allowlist from config; deny by default)
  -> Exception handler -> RFC 7807 ProblemDetails (no internals leaked)
  -> [future: authn/authz, tenant resolution]
  -> Endpoint
```

## Data

PostgreSQL 17, EF Core + Npgsql, one `AppDbContext` (currently empty — Phase 1 adds
Tenant/User/Membership). See [ADR-0002](decisions/ADR-0002-postgresql.md) and
[database.md](database.md).

## Multi-tenancy

Shared database, `TenantId` discriminator + EF Core global query filters, tenant resolved
server-side only. See [ADR-0005](decisions/ADR-0005-multi-tenancy-strategy.md). No tenant
entities exist yet — this is the locked strategy Phase 1 builds against.

## Observability

Serilog (structured console logging) + OpenTelemetry (traces + metrics; OTLP export opt-in via
`OTEL_EXPORTER_OTLP_ENDPOINT`). Health checks at `/health/live` (process up) and `/health/ready`
(Postgres reachable).

## What's deliberately not here yet

No authentication, no domain entities, no channel adapters, no AI provider abstraction, no
realtime hub. Each is scoped to its own phase per `OMNICHANNEL_PRD.md` §90 — see `PLAN.md`
(local, not committed) for current phase status.
