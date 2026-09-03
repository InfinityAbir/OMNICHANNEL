# Database

PostgreSQL 17 via EF Core + Npgsql. See [ADR-0002](decisions/ADR-0002-postgresql.md).

## Current state (Phase 1)

`AppDbContext` extends `IdentityUserContext<ApplicationUser, Guid>` (not `IdentityDbContext` —
Identity's own Role/Claim tables are unused, see [ADR-0007](decisions/ADR-0007-identity-and-auth-model.md))
plus:

| Table | Purpose |
|---|---|
| `identity_users` | ASP.NET Core Identity credential store (password hash, lockout) |
| `app_users` | Business-facing profile (`Domain.Identity.User`), same `Id` as `identity_users` |
| `tenants` | `Tenant` — not tenant-owned, it IS the boundary |
| `tenant_memberships` | `TenantMembership` — tenant-owned, unique on `(TenantId, UserId)` |
| `roles` | 4 seeded system roles (Owner/Admin/Agent/Viewer), `permissions` as native `text[]` |
| `refresh_tokens` | Hashed refresh tokens only, never the raw value |

Migration: `InitialIdentityAndTenancy` (`src/Omnichannel.Infrastructure/Persistence/Migrations/`).
Applied automatically on startup in Development/Testing only — production applies migrations
through a deliberate deploy step, not implicitly on every process start (see `Program.cs`).

## Conventions (binding from Phase 1 onward)

- All schema changes go through EF Core migrations — no hand-edited schema.
- Every tenant-owned table gets a `TenantId` column; composite indexes lead with it
  (`Conversation(TenantId, Status, LastMessageAt)`, etc. — see PRD §47 for the initial index
  list). See [ADR-0005](decisions/ADR-0005-multi-tenancy-strategy.md) for the isolation strategy.
- Timestamps stored in UTC; conversion to tenant timezone happens only at the
  presentation/business-hours boundary, never in stored data.
- Idempotency constraints follow PRD §17, e.g. `UNIQUE(ChannelAccountId, ExternalMessageId)`,
  adapted per provider.
- Transactions wrap multi-step atomic operations only — never wrapped around slow external API
  calls (webhook sends, provider calls).

## Local connection

```
Host=localhost;Port=5432;Database=omnichannel;Username=omnichannel;Password=omnichannel_dev_only
```

Matches `docker-compose.yml` defaults and `.env.example`. Start it with:

```bash
docker compose up -d postgres
```
