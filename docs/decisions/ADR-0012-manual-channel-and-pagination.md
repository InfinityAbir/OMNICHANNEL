# ADR-0012: Manual channel account + keyset pagination

**Status:** Accepted
**Date:** 2026-09-03

## Context

PRD §15 requires every `Conversation` to reference a `ChannelAccountId`, but no real channel
adapter exists until Phase 5 (website chat) / Phase 6 (adapter framework) / Phase 7-9
(WhatsApp/Instagram/Messenger). Phase 2 (Conversations + Contacts) comes first in PRD's own
phase order (§90), so it needs a way to create and test conversations before any of that exists.
PRD §85 separately calls for a "developer-only simulated channel" for exactly this reason.

Separately, PRD §47 calls for paginating conversation lists and message history, and explicitly
warns against loading full histories.

## Decision

**Manual channel.** Every tenant gets one `ChannelAccount` of type `Manual`, created
automatically at registration (`AuthService.RegisterAsync`). `Message.ChannelAccountId` is
denormalized from the owning conversation so PRD §17's exact idempotency shape
(`UNIQUE(ChannelAccountId, ExternalMessageId)`) is available without a join, ready for Phase 6+
adapters to use the same column. `ChannelType` names the full PRD §7 channel taxonomy now (as an
enum) so the schema doesn't change shape when each channel's adapter actually lands — only
`Manual` has working behavior this phase.

**Keyset (cursor) pagination**, not offset, for the two lists that sort by a value that changes
constantly (conversation list by `LastMessageAt`, message history by `CreatedAt`). Offset
pagination on a frequently-reordering list causes visible page drift (a new message arrives,
everything shifts, page 2 repeats or skips rows) — a well-known anti-pattern for exactly this
shape of list. Contacts and audit logs use simple offset pagination instead: they don't reorder
under the user while paging.

## Alternatives considered

- **Defer Conversation/Message to after a real channel exists (reorder the phases).** Rejected:
  contradicts PRD §90's explicit phase order, and the PRD itself anticipates this exact gap via
  §85's simulated-channel guidance.
- **A generic "simulated channel" implemented as fake webhook payloads instead of a first-class
  Manual ChannelAccount.** More faithful to "real channel" mechanics, but meaningfully more code
  for no Phase 2 benefit — Phase 2 doesn't have a webhook pipeline yet (that's Phase 6). A
  first-class Manual channel is simpler and is still useful *after* real channels exist, for
  agent-logged phone calls / walk-ins — not throwaway scaffolding.
- **Offset pagination everywhere, revisit if it becomes a problem.** Rejected specifically for
  conversations/messages because PRD §47 already names this exact concern up front; the fix is
  barely more code than offset pagination and avoids ever having the bug in production.

## Consequences

- Phase 6's real channel adapters must populate the same `Message.ChannelAccountId` +
  `ExternalMessageId` columns Manual already uses — no schema change expected, just population by
  a different code path (webhook ingestion instead of `ConversationService.AddMessageAsync`).
- Cursor tokens (`KeysetCursor`) are opaque and unversioned; a future breaking change to their
  encoding would invalidate outstanding cursors mid-session. Acceptable for now — revisit if it
  becomes a real problem (URLs/bookmarks with stale cursors just get "start of list" behavior,
  not an error).
- Owner/Admin/Agent/Viewer permission mapping for the new endpoints reuses the Phase 1 catalog
  (`conversations.*`) rather than adding tag/contact-specific permission keys, since PRD's fixed
  16-key catalog has none — documented in code, not silently assumed.
