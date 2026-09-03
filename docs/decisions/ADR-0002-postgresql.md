# ADR-0002: PostgreSQL 17 as the sole datastore

**Status:** Accepted
**Date:** 2026-09-03

## Context

PRD §9/§17/§78 mandate PostgreSQL, EF Core migrations, provider-scoped uniqueness constraints
for webhook idempotency, and (later) pgvector for semantic retrieval (Phase 11).

## Decision

Use PostgreSQL 17 (via `postgres:17-alpine` in `docker-compose.yml` for local dev) as the single
relational store for all tenant data, accessed exclusively through EF Core + Npgsql from the
Infrastructure layer. No secondary datastore (cache, search index, queue) is introduced in
Phase 0–9; pgvector is added in Phase 11 when the knowledge base needs semantic retrieval, not
before.

## Alternatives considered

- **SQL Server.** Rejected: PRD explicitly names PostgreSQL; no reason to diverge.
- **Postgres 16 (older LTS-leaning point).** Considered for perceived stability, but this is a
  greenfield project with no legacy constraint, and 17 has been out long enough (Sept 2024 GA)
  to be a safe default; pgvector supports 13–17. Revisit only if a specific extension/driver
  incompatibility surfaces.
- **Separate operational databases per module.** Rejected per AGENTS.md's modular-monolith
  constraint — one database keeps cross-module queries (e.g. inbox list joining conversations +
  contacts + assignments) simple and transactional.

## Consequences

- Every tenant-scoped table needs a `TenantId` column and an index that leads with it (see
  ADR-0005). EF Core migrations are the only sanctioned way to change schema.
- Local dev requires Docker (or a local Postgres 17 instance); `docker-compose.yml` is the
  supported path, bound to `127.0.0.1:5432` only, dev-only credentials in `.env.example`.
- pgvector is deferred; the retrieval abstraction (Phase 11 per PRD §26) must not leak a
  vector-store-specific type into the Domain layer, so switching later stays possible.
