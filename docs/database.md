# Database

PostgreSQL 17 via EF Core + Npgsql. See [ADR-0002](decisions/ADR-0002-postgresql.md).

## Current state (Phase 0)

`Omnichannel.Infrastructure/Persistence/AppDbContext.cs` exists with **no entities** — it exists
only to prove the EF Core + Npgsql + `/health/ready` wiring works end-to-end. No migrations
exist yet; the first migration lands in Phase 1 with `Tenant`, `User`, `TenantMembership`.

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
