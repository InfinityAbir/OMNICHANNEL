# Phase Report — Phase 2: Core Conversations + Contacts

**Status:** Implementation complete. Awaiting user approval to proceed to Phase 3.
**Date:** 2026-09-03

## Scope / PRD references

PRD §61 (Phase 2): Contacts, Conversations, Messages, Assignments, Tags, Internal notes, status
transitions, pagination, search foundations, audit logging. PRD §14-21 (entity model), §47
(indexing/pagination guidance), §85 (simulated channel testing).

## Implemented

- **Domain**: `Contact`/`ContactIdentifier`, `ChannelAccount` (+ `ChannelType` naming PRD §7's
  full taxonomy, only `Manual` behaviorally wired this phase), `Conversation` (Status/Priority/
  AiMode enums), `Message` (Direction/SenderType/ContentType/DeliveryStatus enums,
  `ChannelAccountId` denormalized for PRD §17's exact idempotency shape), `Tag`/`ConversationTag`,
  `InternalNote`, `AuditLog`.
- **Manual channel**: every tenant gets one auto-created at registration
  (`AuthService.RegisterAsync`), letting conversations exist before any real channel adapter
  does (Phase 5+) — see [ADR-0012](../decisions/ADR-0012-manual-channel-and-pagination.md).
- **Pagination**: keyset (cursor) on the conversation list (`LastMessageAt`) and message history
  (`CreatedAt`) — avoids offset pagination's page-drift on a frequently-reordering sort key, per
  PRD §47's explicit warning. Offset pagination for contacts and audit logs.
- **Endpoints**: `/api/v1/contacts` (list/get/create), `/api/v1/conversations` (list/get/create/
  messages/assign/unassign/status/priority/tags/notes), `/api/v1/tags`, `/api/v1/audit` — full
  list with permissions in `docs/api.md`.
- **Audit logging**: every mutating action writes an `AuditLog` row in the same transaction as
  the business change (`AuditService.Record` + the calling service's single `SaveChangesAsync`).
- **Real permission enforcement**: Phase 1 built the permission-policy plumbing but nothing used
  it; every Phase 2 endpoint is gated by a real `PermissionKeys` policy now.
- Query optimizations: conversation list and detail collapse the contact-name join + tag lookup
  into 2 queries per page (not N+1 per conversation), consistent with the standing "optimize
  every endpoint" instruction.

## Tests

- **Unit**: 18/18 (9 carried from Phase 1 + 9 new: `Conversation` status/assignment transitions,
  `Message` factory validation).
- **Integration**: 1/1 (Postgres connectivity, carried from Phase 1).
- **API**: 16/16 (11 carried from Phase 1 + 5 new: full conversation lifecycle end-to-end,
  validation-problem on missing contact info, assign/unassign, 404 on unknown id, contact
  create+search).
- **Security**: 8/8 (6 carried from Phase 1 + 2 new — **both are the PRD §60 mandatory attack
  tests Phase 1's report explicitly deferred to this phase**: modified object ID → another
  tenant's conversation (`ModifiedObjectId_CannotReachAnotherTenantsConversation`, passes — 404,
  not 403, so existence isn't confirmed to a tenant that can't see it), and agent role → an
  admin-only endpoint (`AgentRole_CannotReachAuditLogEndpoint`, passes — 403). No team-invite
  endpoint exists yet, so the second test seeds an Agent-role user directly via `IIdentityService`
  + `IAppDbContext` in the test, then authenticates through the *real* login endpoint — what's
  under test is real authorization enforcement, not the test's own setup shortcut.

All 43 tests pass. `dotnet build`: 0 warnings, 0 errors. `dotnet list package --vulnerable`: 0
findings.

## Security Review

Performed against AGENTS.md's checklist for what exists this phase.

**Finding — confirmed, fixed, regression-tested:**

1. **(Medium, functional — surfaced as a 500 rather than a security hole, but flagged because
   an unhandled-exception path is itself worth closing) `[FromQuery] int` parameters without a
   C# default value are treated as *required* by ASP.NET Core minimal API binding**, not
   optional. Every list endpoint's `page`/`pageSize` threw `BadHttpRequestException` (caught by
   the global handler and returned as a generic 500) when the query string omitted them — which
   every test client naturally does when it wants defaults. Fixed by switching to nullable
   `int?` across all 4 list endpoints. Regression-tested implicitly by every list-endpoint test
   in this phase (`ConversationsEndpointsTests`, `ContactsEndpoints` coverage) — none of them
   pass query params explicitly, so a regression would fail immediately.

**Other controls verified**: IDOR/BOLA closed (see attack tests above); every endpoint requires
authentication and a specific permission (no endpoint relies on "just authenticated"); audit
entries are transactionally consistent with the change they record; message text is length-capped
at the DB layer; internal notes have no customer-facing exposure path in this phase's design.

**Residual/accepted for this phase**: `conversations.reply`/`.assign`/`.close` are reused for
tag/note/contact actions rather than adding dedicated permission keys, since PRD's fixed 16-key
catalog doesn't define ones for them — documented in ADR-0012, not silently assumed. No
new/removed permission keys, so Phase 1's `PermissionKeysTests.All_MatchesPrdCatalogSize` still
holds.

## Performance Review

Conversation list/detail collapsed to 2 queries per page (contact join + one batched tag lookup)
rather than one extra tag query per row. No N+1 identified elsewhere — all list/detail queries
use explicit joins or a single batched follow-up query. Indexes added match PRD §47's named list
exactly (`Conversation(TenantId, Status, LastMessageAt)`,
`Conversation(TenantId, AssignedUserId, Status)`, `Message(ConversationId, CreatedAt)`,
`Message(TenantId, CreatedAt)`, `AuditLog(TenantId, Timestamp)`).

## Architecture Review

Matches ADR-0001/0005/0012. New entities follow the same `ITenantOwned` + private-setter +
factory-method pattern established in Phase 1; the global tenant query filter applies to all of
them automatically (no per-entity opt-in needed, per ADR-0005's original design goal).

## Migrations / Configuration Changes

- Migration: `ConversationsContactsAndAudit` (10 new tables/indexes, see `docs/database.md`).
- No new configuration sections.

## ADRs / Docs Updated

ADR-0012 (manual channel + pagination). `docs/architecture.md`, `docs/security.md`,
`docs/database.md`, `docs/api.md`, root `README.md` — all updated for Phase 2 state.

## Known Limitations

- Only the Manual channel has working behavior — WhatsApp/Instagram/Messenger/website chat are
  Phase 5-9.
- No team-invite/member-management endpoint yet (out of Phase 1/2 scope) — the agent-role
  security test's setup reflects this honestly rather than working around it silently.
- No Playwright E2E — Phase 2 has no UI to test (confirmed backend-only with the user).

## Files/Modules Changed

`src/Omnichannel.Domain/{Channels,Contacts,Conversations,Audit}/*`,
`src/Omnichannel.Application/{Contacts,Conversations,Audit,Common}/*`,
`src/Omnichannel.Infrastructure/Persistence/{Configurations,Migrations}/*` (new entities +
migration), `src/Omnichannel.Api/Endpoints/{Contacts,Conversations,Tags,Audit}Endpoints.cs`,
`src/Omnichannel.Contracts/{Conversations,Audit}/*`, 3 test projects, `docs/decisions/ADR-0012`,
`docs/{architecture,security,database,api}.md`, `README.md`.

## Next Phase

Phase 3 — Unified Inbox UI (PRD §62): Angular conversation list/view/composer, assignment, tags,
notes, search, filters, status controls, responsive design. First phase requiring Playwright E2E
per the standing instruction. Frontend design direction already on file (monochromatic, modern,
skeleton loaders, pagination, search/filter — see repo-local `FEEDBACK.md`).

**Requesting approval to commit/push this phase and begin Phase 3.**
