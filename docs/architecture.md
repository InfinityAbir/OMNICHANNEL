# Architecture

Status as of Phase 3 (Unified Inbox UI).

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
  src/app/core/                  services, models, auth interceptor/guard
  src/app/features/auth/         login, register
  src/app/features/inbox/        conversation list/detail, inbox page shell
  src/app/shared/                skeleton loader, empty state
e2e/                             Playwright — drives the real API + Angular dev server together
```

Dependency direction is inward-only and enforced by project references. Domain must never
reference ASP.NET Core, EF Core, or any provider SDK.

## Request pipeline

```
Request
  -> HTTPS redirection
  -> Security headers (nosniff, deny-frame, locked-down CSP, no Server header)
  -> CORS (explicit allowlist from config; deny by default)
  -> Rate limiter (per-IP, "auth" policy on /api/v1/auth/*)
  -> Authentication (JWT bearer)
  -> Authorization (permission-string policies, dynamically resolved)
  -> Exception handler -> RFC 7807 ProblemDetails (no internals leaked)
  -> Endpoint
```

## Data

PostgreSQL 17, EF Core + Npgsql, `AppDbContext` (Identity + Tenant/User/Membership/Role/
RefreshToken). See [ADR-0002](decisions/ADR-0002-postgresql.md), [ADR-0007](decisions/ADR-0007-identity-and-auth-model.md),
and [database.md](database.md).

## Multi-tenancy

Shared database, `TenantId` discriminator + EF Core global query filters, tenant resolved
server-side only from JWT claims (`ITenantContext`) — never from client input. See
[ADR-0005](decisions/ADR-0005-multi-tenancy-strategy.md). One deliberate, documented exception:
login/refresh's tenant-discovery query bypasses the filter (see ADR-0007) since it runs before a
tenant context exists.

## Identity and authorization

ASP.NET Core Identity (credentials only) + a separate framework-free `Domain.Identity.User`
profile, JWT access tokens + rotating hashed refresh tokens, permission-string authorization
resolved dynamically per `PermissionKeys`. See
[ADR-0007](decisions/ADR-0007-identity-and-auth-model.md).

## Conversation engine

`Contact`/`ContactIdentifier`, `ChannelAccount` (only `Manual` has working behavior — see
[ADR-0012](decisions/ADR-0012-manual-channel-and-pagination.md)), `Conversation`, `Message`,
`Tag`/`ConversationTag`, `InternalNote`, `AuditLog`. Every mutating action writes an audit row in
the same transaction as the business change (`AuditService.Record`, committed by the calling
service's own `SaveChangesAsync`). Conversation list and message history use keyset (cursor)
pagination, not offset — see ADR-0012.

## Frontend

Angular 21, signals-based state (no NgRx), monochromatic design system, keyset cursors passed
through opaquely. Bearer tokens in `localStorage` with a documented XSS trade-off. See
[ADR-0013](decisions/ADR-0013-frontend-architecture.md).

## Realtime

SignalR hub at `/hubs/inbox`. One hub, one group per tenant (`tenant:{tenantId}`); group membership
is derived from the server-issued `tenant_id` JWT claim, never from a client-supplied group name.
`[Authorize(Policy = "RealtimeHub")]` on the hub plus an in-hub claim check (defense in depth);
WebSocket auth uses the token in the query string, read only for `/hubs` paths. Events are minimal
DTOs (IDs + changed fields); the Angular client de-duplicates per event type and patches its signal
state, or re-fetches full detail when the event can't describe the change. See
[ADR-0014](decisions/ADR-0014-realtime-architecture.md).

## Observability

Serilog (structured console logging) + OpenTelemetry (traces + metrics; OTLP export opt-in via
`OTEL_EXPORTER_OTLP_ENDPOINT`). Health checks at `/health/live` (process up) and `/health/ready`
(Postgres reachable).

## What's deliberately not here yet

No channel adapters (WhatsApp/Instagram/Messenger/website chat), no AI provider abstraction, no
background-processing engine. Each is scoped to its own phase per `OMNICHANNEL_PRD.md` §90 — see
`PLAN.md` (local, not committed) for current phase status.
