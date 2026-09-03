# ADR-0005: Multi-tenancy — shared database, `TenantId` discriminator, global query filters

**Status:** Accepted (strategy locked now; implementation lands in Phase 1)
**Date:** 2026-09-03

## Context

AGENTS.md and PRD §11 treat tenant isolation as a non-negotiable invariant: a request
authenticated for Tenant A must never reach Tenant B data, tenant context must be derived
server-side from authenticated identity, and client-supplied tenant IDs must never be trusted.
This decision is recorded in Phase 0 — before any entity exists — because it shapes the base
`DbContext` design that Phase 1 builds on, and an insecure default here would have to be
retrofitted across every entity added afterward.

## Decision

- **Shared database, shared schema.** Every tenant-owned table carries a `TenantId` column
  (not schema-per-tenant, not database-per-tenant).
- **EF Core global query filters** scoped to the current tenant are applied to every
  tenant-owned entity type, so a missing `.Where(x => x.TenantId == ...)` in application code
  cannot leak cross-tenant rows by omission.
- **Tenant context is resolved server-side only**, from the authenticated user's claims/session
  (via an `ITenantContext`-style abstraction introduced in Phase 1), never from a route
  parameter, header, or request body supplied by the client.
- Composite indexes lead with `TenantId` (e.g. `Conversation(TenantId, Status, LastMessageAt)`
  per PRD §47) so tenant-scoped queries stay index-friendly instead of relying on a filter alone.
- The same rule extends beyond SQL: cache keys, SignalR groups, background job payloads,
  webhook processing, search/analytics queries, and AI/RAG retrieval must all be tenant-scoped
  and re-validated server-side — not just the database layer.

## Alternatives considered

- **Database-per-tenant.** Strongest isolation, but operationally heavy for an SME SaaS MVP
  (migration fan-out, connection-pool sprawl, cost per tenant). Reconsider only if a compliance
  requirement (e.g. a specific enterprise customer demanding physical isolation) forces it —
  document that as a new ADR if it happens, don't preempt it now.
- **Schema-per-tenant.** Middle ground, still adds migration/connection complexity disproportionate
  to MVP scale; global query filters give comparable defense-in-depth against the IDOR/BOLA class
  of bug without the operational cost.
- **Row-level security (Postgres RLS) instead of/in addition to EF filters.** Not ruled out
  long-term as defense-in-depth, but adds a second place tenant logic must be kept correct with no
  current driver forcing it. Revisit if a security review after Phase 1/2 finds EF filters
  insufficient (e.g. a raw-SQL escape hatch bypassing them).

## Consequences

- Phase 1 must design `AppDbContext` so that adding a new tenant-owned entity type without
  wiring its global query filter is either impossible or immediately visible in review (e.g. a
  base `TenantOwnedEntity` marker interface + a single loop over `ModelBuilder` that applies the
  filter to every implementer).
- Every phase's mandatory security review must include an explicit tenant-isolation attack test
  per PRD §60's "Mandatory attack tests" (cross-tenant object ID, modified object ID, etc.).
- Raw SQL / `FromSqlRaw` usage bypasses EF global query filters — any such usage must add its own
  explicit `TenantId` predicate, and should be flagged in code review.
