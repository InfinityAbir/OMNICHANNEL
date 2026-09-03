# ADR-0014: Realtime messaging architecture (SignalR)

**Status:** Accepted
**Date:** 2026-09-04

## Context

Phase 4 (PRD §63) requires real-time updates for the inbox: new messages, conversation updates,
assignment changes, notifications, and message-status changes. PRD §43 mandates SignalR for
real-time updates and requires that groups be tenant-aware — *"a user must never be able to
subscribe to another tenant's realtime group."* PRD §63 lists security (tenant isolation in
groups, unauthorized subscriptions, connection authentication, reconnection, token expiry, event
leakage) and reliability (duplicate events, reconnects, offline browser, multiple tabs, concurrent
agents) reviews.

## Decision

- **SignalR over a single tenant-scoped group.** One hub (`InboxHub`, mapped at `/hubs/inbox`)
  with exactly one SignalR group per tenant (`tenant:{tenantId}`). A connection is admitted to its
  group only after the hub verifies the authenticated principal carries both `tenant_id` and `sub`
  claims; otherwise the connection is aborted. There is **no per-conversation client-join from the
  client**, so a caller can never request an arbitrary group — group membership is derived entirely
  from the server-issued token's tenant claim. All push notifications fan out to the tenant group.
- **Events are minimal DTOs (IDs + changed fields only), not full entities.** Clients treat
  received events as invalidations / patches: they re-fetch full detail when needed, or patch an
  existing row in place when the event carries the necessary field. Broadcast from every realtime
  mutation in `ConversationService` (`CreateManualAsync`, `AddMessageAsync`, `AssignAsync`,
  `UnassignAsync`, `ChangeStatusAsync`, `SetPriorityAsync`).
- **WebSocket auth via query-string token, scoped to hub paths only.** A WebSocket cannot set an
  `Authorization` header, so the SignalR JS client sends the bearer token as `?access_token=...`
  through `accessTokenFactory`. `JwtBearerEvents.OnMessageReceived` reads it **only** for
  `/hubs` paths; regular REST endpoints never accept a query-string token. Combined with
  `[Authorize(Policy = "RealtimeHub")]` on the hub plus the in-hub claim checks as defense in
  depth.
- **`RealtimeService` reads the token directly from `localStorage`.** It does not depend on
  `AuthService` — the earlier draft had `RealtimeService`↔`AuthService` circular dependency that
  broke Angular DI (and every Playwright test). Each tab establishes its own connection; the client
  de-duplicates by event id on a per-event-type basis.
- **Frontend renders by patching signals, with id-based de-duplication.** List and detail components
  subscribe to the event streams and patch their own `signal()` state. The composer no longer
  blindly prepends the POST response — it de-dupes by message id, because the sender's own
  connection also receives the pushed `new_message`; without the de-dupe an agent saw their own
  message twice.

## Alternatives considered

- **Client-join per conversation group** (`JoinConversation`/`LeaveConversation` on the hub.
  Rejected: it requires trusting a client-supplied conversation id for group admission and adds
  join/leave bookkeeping; the tenant-fanout model is simpler and the tenant boundary is the real
  isolation invariant. Per-conversation fanout can be layered on later if volume demands it.
- **Server-Sent Events / polling.** Rejected for chat: SSE is one-way, and polling adds latency and
  load; SignalR gives bidirectional transport and automatic reconnection.
- **Frontend relies solely on reload after the in-flight response.** Rejected: the sender test
  caught the duplicate-append race; the fix is explicit de-dupe rather than removing optimistic
  UI, because a dropped push on the sending connection (e.g. during reconnect) should still show
  the message.

## Consequences

- Tenant isolation is enforced at the data path (server-issued tenant claim → group), not by client
  choice; cross-tenant event leakage requires forging a token, which the signing key already
  prevents.
- The hub authorization policy (`RealtimeHub`) is registered in `Program.cs` alongside the
  existing permission-string policy provider. The in-hub `OnConnectedAsync` claim check remains as
  a second layer (defense in depth).
- Minimal-DTO events keep payloads small but push complexity to clients (they must handle partial
  data and refresh). This is documented in the event DTOs and the client service.
- Multiple tabs are supported (each tab connects independently); client-side per-type event-id
  de-duplication prevents double-rendering across duplicated events.

**Status/date:** Accepted, 2026-09-04. Associated with Phase 4 (PRD §63).
