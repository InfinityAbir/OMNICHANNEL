# Phase Report — Phase 4: Realtime Messaging (SignalR)

**Status:** Implementation complete. Awaiting user approval to proceed to Phase 5.
**Date:** 2026-09-04

## Scope / PRD references

PRD §63 (Phase 4): SignalR for new-message, conversation-update, assignment-update, notification,
and message-status events, plus the mandated security review (tenant isolation in groups,
unauthorized subscriptions, connection authentication, reconnection, token expiry, event leakage)
and reliability review (duplicate events, reconnects, offline browser, multiple tabs, concurrent
agents). PRD §43 (Realtime Updates): use SignalR; groups tenant-aware; a user must never be able to
subscribe to another tenant's group.

## Implemented

- **Backend SignalR hub** (`src/Omnichannel.Infrastructure/Realtime/`):
  - `InboxHub` mapped at `/hubs/inbox`, guarded by `[Authorize(Policy = "RealtimeHub")]` and a
    second in-hub claim check in `OnConnectedAsync` (aborts if `tenant_id` or `sub` missing) —
    defense in depth.
  - One SignalR group per tenant (`tenant:{tenantId}`); membership is derived from the
    server-issued `tenant_id` claim, never from a client-supplied group name. There is no
    client-invokable "join arbitrary group," so cross-tenant group subscription is impossible
    without a forged token.
  - `HubAuthorization.cs`: `InboxHubAuthorizationHandler` + `HubAuthorizationRequirement`,
    registered as an `IAuthorizationHandler` and wired into the `RealtimeHub` policy in
    `Program.cs` (policy = `RequireAuthenticatedUser()` + the requirement).
- **Event DTOs** (`Omnichannel.Contracts/Realtime/InboxHubEvents.cs`): minimal —
  `NewMessageEvent`, `ConversationUpdateEvent`, `AssignmentUpdateEvent`, `MessageStatusEvent`,
  `NotificationEvent`, plus constant sets. Records with `EventId` per event.
- **Notifier abstraction**: `IRealtimeNotifier` (Application `Abstractions`) implemented by
  `SignalRNotifier` (Infrastructure), registered via `AddSignalRNotifier()` in `AddInfrastructure`.
- **`ConversationService` fully wired** to emit events from every realtime mutation:
  `CreateManualAsync`, `AddMessageAsync` (new-message + conversation-update + message-status for
  outbound + high-priority alert), `AssignAsync`/`UnassignAsync` (assignment-update +
  conversation-update, using `db.UserProfiles.DisplayName`), `ChangeStatusAsync`,
  `SetPriorityAsync`. High-priority alert only for High/Urgent (per confirmed decision).
- **Frontend realtime ingestion**:
  - `RealtimeService` (new): builds the SignalR connection via
    `accessTokenFactory` (reads `omnichannel.accessToken` from `localStorage` — deliberately NOT
    dependent on `AuthService`, avoiding the circular-DI bug that broke app boot), AutomaticReconnect
    with backoff, per-event-type id de-duplication, `Subject`s per event type, `start()`/`stop()`.
  - `AuthService` starts realtime on login/register and stops on logout.
  - `ConversationListComponent`: patches existing items on `conversationUpdate$`/`newMessage$`/
    `assignmentUpdate$` and re-sorts by date; reloads the full list when an event references an
    unknown conversation (minimal DTO can't describe a new row).
  - `ConversationDetailComponent`: appends `newMessage$` events scoped to the open conversation
    and patches status/priority/assignment; composer `send()` de-dupes by message id so the
    sender doesn't see their own message twice (its own connection receives the push too) — the
    duplicate-append race the sender test exposed.
  - `proxy.conf.json`: added `/hubs/inbox` with `"ws": true` (SignalR WebSockets were 404ing
    through the dev proxy).
- **Playwright E2E** (`e2e/tests/realtime.spec.ts`, 2 tests): a second agent sees a new
  conversation appear in real-time and sees a new message appear in real-time in an already-open
  conversation — both run against real API + Angular + Postgres.

## Root-causes found and fixed during the phase

- **SignalR connected then immediately disconnected.** WebSockets can't send an `Authorization`
  header; the JWT bearer only read the header, so the server saw an unauthenticated context and
  `InboxHub.OnConnectedAsync` aborted every connection. Fixed with
  `JwtBearerEvents.OnMessageReceived` reading `access_token` only for `/hubs` paths. A stale API
  process left listening on :5068 initially masked this (Playwright reused it); killed it and
  re-ran against the fixed build.
- **Duplicate message on the sending agent.** The composer appended the POST response *and* the
  agent's own connection delivered the pushed `new_message`. Fixed by de-duping by message id in
  `send()` and in the realtime handler.
- **Per-type de-dupe bug.** `new_message` and `message_status` share `MessageId`, so a single
  shared event-id set would silently swallow `message_status` after its `new_message`. Fixed by
  scoping the de-dupe key per event type.
- **CI backend failure (role-seed race).** A benign `DbUpdateException` during concurrent
  parallel test-host seeding left failed rows tracked; the next `SaveChangesAsync` re-sent them.
  Fixed by `db.ChangeTracker.Clear()` in the catch (see security.md).

## Tests

- **Unit**: 25/25 (incl. new `InboxHubEventTests`).
- **Integration**: 1/1.
- **API**: 16/16.
- **Security**: 12/12 (incl. new `SignalRSecurityTests`).
- **Frontend**: `ng lint` clean, `ng build` clean.
- **E2E (Playwright)**: 4/4 (2 existing inbox + 2 new realtime).
- **CI**: all jobs green (backend Test, frontend Test, e2e Playwright) on the final push
  (`6026275`), verified with `gh run watch`.

All counts green; `dotnet build` 0 warnings/errors.

## Security Review

Addressed the PRD §63 realtime threat model: connection authentication (policy + in-hub check),
tenant isolation in groups (server-derived membership, no cross-tenant subscribe path), WebSocket
token read restricted to `/hubs` paths (not REST), minimal-DTO event leakage, reconnection/token
expiry behavior, and duplicate-event handling. Full detail in `docs/security.md` (Phase 4
controls + review-log entry). No high/critical findings.

## Performance / Reliability / Accessibility Review

- Events are minimal DTOs — no full-entity broadcast, no per-row projection cost on push.
- Client de-duplicates by event id per type; multiple tabs supported (each connects independently).
- Reconnect with backoff; offline/reconnect behavior documented.
- Accessibility: no new a11y-sensitive UI beyond existing components; realtime is add-only
  (patches existing DOM). Not a full WCAG pass, consistent with Phase 3.

## Migrations / Configuration Changes

- No new EF migration this phase (events are a broadcast concern; no schema change).
- `web/proxy.conf.json`: added `/hubs/inbox` (`ws: true`).
- `web/package.json`/`package-lock.json`: added `@microsoft/signalr`.
- `Program.cs`: SignalR hub + hub authorization policy + `JwtBearerEvents.OnMessageReceived`.

## ADRs / Docs Updated

- New [ADR-0014](decisions/ADR-0014-realtime-architecture.md) (realtime architecture).
- `docs/architecture.md` (realtime section; removed realtime from "not here yet"),
  `docs/security.md` (Phase 4 controls + review log).

## Known Limitations

- No per-conversation SignalR groups yet (tenant-fanout only) — fine at current scale; can layer
  on if volume demands, per ADR-0014.
- On token expiry/forced disconnect the client surfaces disconnected state but doesn't yet
  auto-route to re-login; a small later-phase enhancement on top of the existing `onclose` signal.
- Notifications/high-priority alerts are emitted but not yet rendered in a frontend toast/banner
  (backend + event plumbing done; UI rendering is a small follow-up — not required to close PRD §63
  delivery of the event).

## Files/Modules Changed

`src/Omnichannel.Infrastructure/Realtime/{InboxHub,HubAuthorization,SignalRNotifier}.cs` (new),
`src/Omnichannel.Contracts/Realtime/InboxHubEvents.cs` (new),
`src/Omnichannel.Application/Abstractions/IRealtimeNotifier.cs` (new),
`src/Omnichannel.Application/Conversations/ConversationService.cs`,
`src/Omnichannel.Application/Omnichannel.Application.csproj`,
`src/Omnichannel.Infrastructure/Persistence/RoleSeeder.cs`,
`src/Omnichannel.Infrastructure/DependencyInjection.cs`, `src/Omnichannel.Api/Program.cs`,
`web/src/app/core/services/{realtime.service,auth.service}.ts`,
`web/src/app/core/models/realtime.models.ts` (new),
`web/src/app/features/inbox/{conversation-list,conversation-detail}/conversation-{list,detail}.ts`,
`web/proxy.conf.json`, `web/package.json`/`package-lock.json`,
`tests/Omnichannel.UnitTests/Realtime/InboxHubEventTests.cs` (new),
`tests/Omnichannel.SecurityTests/SignalRSecurityTests.cs` (new), `e2e/tests/realtime.spec.ts` (new),
`docs/decisions/ADR-0014` (new), `docs/{architecture,security}.md`.

## Next Phase

Phase 5 — Website Chat Channel (PRD §64): first complete channel adapter (website chat widget,
anonymous/customer identity, secure session, inbound/outbound messages, conversation creation,
attachments where appropriate, realtime communication).

**Requesting approval to proceed to Phase 5.**
