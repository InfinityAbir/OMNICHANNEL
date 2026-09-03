# API

API-first design (PRD §41/§42): the same backend must serve Angular now and a future
Android/iOS client later without special-casing either.

## Versioning

URL-segment: `/api/v{version}/...`, default `1.0`. See
[ADR-0008](decisions/ADR-0008-api-versioning.md).

## Phase 1 endpoints (auth)

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/health/live` | — | Process is up (no dependency checks) |
| GET | `/health/ready` | — | Postgres reachable |
| POST | `/api/v1/auth/register` | — | Self-service signup: creates User + Tenant + Owner membership |
| POST | `/api/v1/auth/login` | — | Returns access + refresh token pair |
| POST | `/api/v1/auth/refresh` | — | Rotates a refresh token, returns a new pair |
| POST | `/api/v1/auth/logout` | — | Revokes a refresh token |
| GET | `/api/v1/auth/confirm-email` | — | Link-follow flow, server-rendered result page |
| POST | `/api/v1/auth/password-reset/request` | — | Always same response — no email enumeration |
| POST/GET | `/api/v1/auth/password-reset/form`, `/confirm` | — | Link-follow reset flow (plain HTML form; Phase 3 replaces it with a real page) |
| GET | `/api/v1/users/me` | Bearer JWT | Current user + active tenant + role + permissions |

All `/api/v1/auth/*` routes share a per-IP rate limit (30 req/min) — see `docs/security.md`.

## Phase 2 endpoints (conversations)

| Method | Route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/contacts` | `conversations.read` | Paged, `?search=` (case-insensitive display-name match) |
| GET | `/api/v1/contacts/{id}` | `conversations.read` | |
| POST | `/api/v1/contacts` | `conversations.reply` | |
| GET | `/api/v1/conversations` | `conversations.read` | Keyset-paginated (`?cursor=`), `?status=`, `?assignedUserId=` |
| GET | `/api/v1/conversations/{id}` | `conversations.read` | |
| POST | `/api/v1/conversations` | `conversations.reply` | `contactId` (existing) or `newContactDisplayName`; optional `initialMessageText` |
| GET | `/api/v1/conversations/{id}/messages` | `conversations.read` | Keyset-paginated |
| POST | `/api/v1/conversations/{id}/messages` | `conversations.reply` | `direction`/`senderType`/`text` |
| POST | `/api/v1/conversations/{id}/assign`, `/unassign` | `conversations.assign` | |
| POST | `/api/v1/conversations/{id}/status` | `conversations.close` | Any `ConversationStatus` value |
| POST | `/api/v1/conversations/{id}/priority` | `conversations.reply` | |
| GET/POST | `/api/v1/conversations/{id}/notes` | `conversations.read` / `conversations.reply` | |
| POST | `/api/v1/conversations/{id}/tags` | `conversations.reply` | Find-or-create by name |
| DELETE | `/api/v1/conversations/{id}/tags/{tagId}` | `conversations.reply` | |
| GET/POST | `/api/v1/tags` | `conversations.read` / `conversations.reply` | |
| GET | `/api/v1/audit` | `audit.read` | Paged; Agent role cannot reach this (regression-tested) |

Cross-tenant object access returns `404`, never `403` — never confirms an object exists to a
tenant that can't see it (regression-tested, `ConversationSecurityTests`).

`conversations.reply`/`.assign`/`.close` reuse the Phase 1 permission catalog rather than adding
tag/contact/note-specific keys, since PRD's fixed 16-key catalog has none — see
[ADR-0012](decisions/ADR-0012-manual-channel-and-pagination.md).

**Known limitation**: these routes use a hard-coded `/api/v1/` prefix, not yet wired through
`Asp.Versioning`'s actual version-resolution machinery (which is registered but unused so far —
see [ADR-0008](decisions/ADR-0008-api-versioning.md)). Fine while there's only one version;
revisit when a `v2` of any endpoint is actually needed.

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
