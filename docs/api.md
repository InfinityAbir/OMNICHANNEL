# API

API-first design (PRD §41/§42): the same backend must serve Angular now and a future
Android/iOS client later without special-casing either.

## Versioning

URL-segment: `/api/v{version}/...`, default `1.0`. See
[ADR-0008](decisions/ADR-0008-api-versioning.md).

## Phase 0 endpoints

No public API endpoints yet — only operational health checks, intentionally unversioned:

| Method | Route | Purpose |
|---|---|---|
| GET | `/health/live` | Process is up (no dependency checks) |
| GET | `/health/ready` | Postgres reachable |

## Error contract

All errors — including unhandled exceptions — return RFC 7807 `application/problem+json`.
Never a raw stack trace or exception message. Every response includes a `traceId` extension for
support correlation without exposing internals.

## Conventions (binding from Phase 1 onward)

- Pagination, filtering, sorting on all list endpoints.
- Cancellation tokens propagated end-to-end; no sync-over-async.
- Idempotency keys for commands where retries could duplicate an effect.
- OpenAPI documentation added once real versioned endpoints exist (Phase 1) — deliberately not
  wired in Phase 0, see [ADR-0008](decisions/ADR-0008-api-versioning.md) consequences.
