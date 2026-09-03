# Phase Report — Phase 6: External Channel Adapter Framework

**Status:** Implementation complete. Proceeding to Phase 7 per explicit user instruction (no
approval pause between Phase 6 and Phase 7 for this handoff).
**Date:** 2026-09-04

## Scope / PRD references

PRD §65 (Phase 6): the generic channel abstraction — adapter interfaces, capability model,
provider credential model, webhook processing pipeline, idempotency, outbound routing, provider
error normalization, retry architecture. Explicitly: "do not implement all providers at once."

## Implemented

- **`IChannelAdapter`** (`Omnichannel.Application.Abstractions`): `VerifyWebhookAsync`,
  `ParseWebhookAsync`, `SendMessageAsync`, plus `ChannelCapabilities`. Supporting types:
  `WebhookRequest`/`WebhookVerificationResult`, `NormalizedInboundEvent` (Message/StatusUpdate),
  `ChannelSendRequest`/`ChannelSendResult`, `ChannelSendErrorKind` (Transient/RateLimited/
  AuthFailed/InvalidRecipient/PermanentFailure), `ChannelSendException`.
- **`IChannelAdapterRegistry`** / `ChannelAdapterRegistry` (Scoped — avoids a captive-dependency
  bug if a future adapter needs Scoped deps): resolves `ChannelType → IChannelAdapter` from DI.
  Zero adapters registered in production this phase.
- **`WebhookIngestionService`**: verify → parse → resolve `ChannelAccount` by
  `(Type, ExternalAccountId)` (`IgnoreQueryFilters()` — the third documented tenant-filter
  exception, ADR-0016) → idempotent persist (checks first, DB unique index is the race guard,
  `DbUpdateException` handled like `RoleSeeder`'s) → realtime notify. Handles both new-message and
  delivery-status-update events; one malformed event in a batch doesn't fail the rest.
- **`ChannelSendService`**: Polly-based retry (3 attempts, exponential backoff) — only for
  Transient/RateLimited; everything else fails fast. Wired into
  `ConversationService.AddMessageAsync`'s outbound path — falls back to the exact pre-Phase-6
  behavior (mark sent immediately) when no adapter is registered for the channel (Manual,
  WebsiteChat unchanged).
- **`IChannelCredentialStore`** / `DataProtectionChannelCredentialStore`: encrypts provider
  secrets at rest via ASP.NET Core Data Protection; plaintext exists only for the duration of a
  Set/Get call, never logged or returned by any response.
- **New persistent state**: `ChannelAccount.ExternalAccountId` (unique per `(Type,
  ExternalAccountId)` — Postgres treats each NULL as distinct, so Manual/WebsiteChat/unconnected
  accounts never collide), `ChannelCredential` entity + `channel_credentials` table,
  `Message.DeliveredAt`/`ReadAt` + `ApplyProviderStatus` (the Phase 4 decision to define the full
  delivery-status enum up front now has somewhere to attach).
- **API surface**: `GET/POST /webhooks/{channelType}` (public, provider-called — GET for
  handshake-style verification, POST for delivery); `GET/PUT /api/v1/channels/{channelType}`,
  `PUT .../account`, `PUT`/`DELETE .../credentials` (agent-facing, `ChannelsRead`/`ChannelsManage`
  permissions, same pattern as the widget's own settings endpoints).
- **`webhook` rate-limit policy**: 300/min per IP — generous enough for a connected provider's
  normal burst, still bounds gross abuse.
- **Proven via a test-only fake adapter**, not a real provider (PRD: "do not implement all
  providers at once") — `FakeChannelAdapter` stands in for the WhatsApp slot in test DI only,
  exercising the pipeline end-to-end without any real WhatsApp code existing yet.

## Root-causes found and fixed during the phase

- **New test flakiness, self-caught before it reached CI**: the first draft of the new webhook
  tests used fixed literal `ExternalAccountId` strings (e.g. `"whatsapp-account-123"`). Since
  `(Type, ExternalAccountId)` is a real unique DB index and this project's tests run against a
  persistent local Postgres (not reset per run), a second local run collided with rows a prior run
  had already committed — `409 Conflict` instead of `200 OK`, cascading into `ArgumentOutOfRangeException`
  reading an empty conversation list. Fixed by generating a fresh GUID per test invocation, the
  same pattern `TestAuth` already uses for emails — not a framework or production bug, a test
  design mistake caught by actually re-running the suite twice before moving on.

## Tests

- **Unit**: 32/32 (unchanged — no new pure-domain logic beyond `Message.ApplyProviderStatus`,
  covered indirectly via the API-level webhook tests).
- **Integration**: 1/1.
- **API**: 27/27 (20 prior + 7 new: unsupported channel → 404, GET handshake valid/invalid,
  inbound message creates conversation + is idempotent under redelivery, credentials never
  returned, outbound retry-then-succeed, outbound permanent-failure-no-retry).
- **Security**: 14/14 (12 prior + 2 new: spoofed signature rejected and never persisted,
  cross-tenant channel-account isolation on the webhook resolution path).
- **Frontend**: unaffected this phase (no new UI — Phase 6 is backend framework only; PRD §65 has
  no UI deliverable, and there's no real provider yet for a "connect channel" screen to configure
  against).
- **CI**: verified green via `gh run watch` after push (see git history) — not just local output,
  per the standing rule.

All counts green. `dotnet build`: 0 warnings/errors.

## Security Review

Addressed PRD §65's explicit focus list — webhook spoofing, replay attacks, credential handling,
external payload validation, SSRF risks, provider response validation. Full detail in
`docs/security.md`'s "Phase 6 controls" section and review-log entry. No high/critical findings.
SSRF is explicitly not-yet-applicable (no media-fetching code exists until a real channel needs
it) rather than defended against nothing — documented, not silently skipped.

## Performance/Reliability Review

- Webhook ack path stays fast: verify + parse + persist, no synchronous external calls (outbound
  sends are a separate, agent-triggered path, not part of inbound ack).
- Retry is bounded (3 attempts, exponential backoff) and scoped to one outbound send — no
  amplification risk, and inbound webhook processing never retries internally (relies on the
  provider's own retry behavior instead, which is the correct place for that responsibility).
- `ChannelAdapterRegistry` is Scoped, not Singleton, specifically to avoid a captive-dependency
  bug for a future Scoped adapter (see ADR-0016's alternatives) — a deliberate choice made before
  it could become a real bug, not found by trial and error.

## Migrations / Configuration Changes

- Migration `20260903202307_AddChannelFramework`: `channel_accounts.ExternalAccountId` (+ unique
  index on `(Type, ExternalAccountId)`), new `channel_credentials` table, `messages.DeliveredAt`/
  `ReadAt`.
- New package: `Polly.Core` (Application layer — retry policy is a business-level concern, not
  infrastructure-specific).

## ADRs / Docs Updated

New [ADR-0016](decisions/ADR-0016-channel-adapter-framework.md). `docs/architecture.md` (new
"Channel adapter framework" section, updated "not here yet" list), `docs/security.md` (new "Phase
6 controls" section + review-log entry), `docs/integrations.md` (Phase 6 marked implemented, new
"Connecting a new channel" implementer checklist for Phase 7+).

## Known Limitations

- No business-facing "connect a channel" UI — there's no real provider to connect to yet; the
  admin API exists and is fully tested, Phase 7+ can build the UI once WhatsApp's actual connect
  flow (OAuth vs. manual token entry — PRD §66 asks to verify current provider docs first) is
  known.
- No background job queue — everything in this phase runs synchronously within the request/
  webhook that triggered it. Revisit only if a real channel's workload demands it (see ADR-0016).
- Media/attachment handling (and its SSRF-aware fetcher) doesn't exist yet — tracked for whichever
  channel first needs it.

## Files/Modules Changed

`src/Omnichannel.Domain/Channels/{ChannelAccount,ChannelCredential}.cs`,
`src/Omnichannel.Domain/Conversations/{Message,MessageEnums}.cs`,
`src/Omnichannel.Application/Abstractions/{IChannelAdapter,IChannelAdapterRegistry,
IChannelCredentialStore,IAppDbContext,IRealtimeNotifier}.cs`,
`src/Omnichannel.Application/Channels/{WebhookIngestionService,ChannelSendService}.cs`,
`src/Omnichannel.Application/Conversations/ConversationService.cs`,
`src/Omnichannel.Application/DependencyInjection.cs`,
`src/Omnichannel.Infrastructure/Channels/{ChannelAdapterRegistry,
DataProtectionChannelCredentialStore}.cs`,
`src/Omnichannel.Infrastructure/Persistence/{AppDbContext,Configurations/
ChannelAccountConfiguration,Configurations/ChannelCredentialConfiguration}.cs`,
`src/Omnichannel.Infrastructure/Persistence/Migrations/20260903202307_AddChannelFramework*`,
`src/Omnichannel.Infrastructure/DependencyInjection.cs`,
`src/Omnichannel.Api/Endpoints/ChannelWebhookEndpoints.cs`, `src/Omnichannel.Api/Program.cs`,
`src/Omnichannel.Contracts/Channels/ChannelContracts.cs`,
`tests/Omnichannel.ApiTests/Channels/{FakeChannelAdapter,ChannelWebhookEndpointsTests}.cs` (new),
`tests/Omnichannel.SecurityTests/ChannelWebhookSecurityTests.cs` (new),
`Directory.Packages.props` (Polly.Core, Microsoft.Extensions.Logging.Abstractions),
`docs/decisions/ADR-0016` (new), `docs/{architecture,security,integrations}.md`.

## Next Phase

Phase 7 — WhatsApp Integration (PRD §66): the first real `IChannelAdapter`. Per PRD's own
checklist, starts with reading current official WhatsApp Business Platform documentation before
any implementation — account requirements, permissions, webhook verification, messaging windows,
templates, supported media, rate limits — documented before coding, not assumed from prior
training data.

**Proceeding directly to Phase 7 per explicit user instruction — no approval pause.**
