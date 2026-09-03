# ADR-0016: Channel adapter framework (webhook pipeline, retry, credentials)

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §65 (Phase 6) asks for the generic external-channel abstraction WhatsApp/Instagram/Messenger
(Phase 7-9) will each plug into: adapter interfaces, a capability model, a provider credential
model, webhook processing (verify → parse → idempotent persist), outbound routing with retry, and
provider error normalization — explicitly *not* any one provider yet ("do not implement all
providers at once"). Manual (ADR-0012) and the website-chat widget (ADR-0015) predate this
abstraction and were built as bespoke service paths; this phase doesn't retrofit them — they have
no external provider webhook/API to isolate behind an adapter, which is the entire reason the
interface exists (AGENTS.md: "isolate each provider behind a channel adapter").

## Decision

**`IChannelAdapter`** (`Omnichannel.Application.Abstractions`) is the seam every real external
channel implements: `VerifyWebhookAsync` (signature/HMAC + handshake), `ParseWebhookAsync`
(payload → normalized events), `SendMessageAsync` (outbound). Nothing above this interface knows
any provider's wire format.

**`WebhookIngestionService`** is the one generic pipeline every channel's inbound traffic runs
through: resolve adapter → verify → parse → resolve the receiving `ChannelAccount` by
`(ChannelType, ExternalAccountId)` → idempotent persist (existing `UNIQUE(ChannelAccountId,
ExternalMessageId)` index from ADR-0012, now enforced for real) → realtime notify. One malformed
event in a batch is skipped, not fatal to the delivery.

**Account resolution runs `IgnoreQueryFilters()`.** A provider webhook call is unauthenticated
(no tenant JWT — the provider calls the server directly) and carries only its own account id, so
tenant context must be *established* by this lookup, not assumed to already exist. This is the
third documented exception to the global tenant filter (ADR-0005), alongside `AuthService`'s
login/refresh lookup and `WidgetService`'s origin/slug resolution — all three share the same
shape: a public, pre-authentication lookup that resolves tenant identity rather than trusting it.

**`ChannelSendService`** wraps outbound sends in a Polly retry (`Polly.Core`): only
`Transient`/`RateLimited` failures retry (3 attempts, exponential backoff from 200ms); `AuthFailed`,
`InvalidRecipient`, `PermanentFailure` fail immediately — retrying those can never succeed and
would only delay the agent's feedback. `ConversationService.AddMessageAsync` calls it for every
outbound send; when no adapter is registered for the conversation's channel (Manual, WebsiteChat,
or any not-yet-shipped channel), it returns `null` and the caller keeps its pre-Phase-6 behavior
(mark sent immediately) — unchanged for those two channels.

**Credentials are encrypted at rest via ASP.NET Core Data Protection** (`IChannelCredentialStore`
→ `DataProtectionChannelCredentialStore`), the same key-management machinery already backing
Identity's own tokens in this app. Plaintext exists only for the duration of a Set/Get call —
never logged, never returned by any API response (verified by
`ChannelWebhookEndpointsTests.Credentials_NeverReturnedInApiResponse`).

**Zero adapters are registered in production this phase.** `IChannelAdapterRegistry` resolves
`ChannelType → IChannelAdapter` from whatever's in DI; with nothing registered, every real
channel's webhook route 404s ("Unsupported") and every send falls back to existing behavior. The
pipeline is proven correct now via a test-only fake adapter (`FakeChannelAdapter`, standing in for
the WhatsApp slot) rather than a real provider — Phase 7 registers the first real one and replaces
nothing else.

## Alternatives considered

- **Wait until Phase 7 to build any of this, since there's no real provider yet.** Rejected: PRD
  explicitly phases this as its own step specifically so Phase 7 isn't simultaneously inventing
  the abstraction *and* learning WhatsApp's actual API — the same reasoning ADR-0012 used to
  justify Manual before any real channel existed.
- **A single provider-agnostic "channel account resolution" helper reused as-is by Widget/Manual.**
  Rejected: those two channels don't have a provider webhook to resolve *from* — WidgetService's
  origin/slug lookup and Manual's implicit single-account-per-tenant model are structurally
  different problems that happen to rhyme, not the same code path.
- **Background job queue for webhook processing** (durable retry, async work). Deferred: AGENTS.md
  asks slow/retryable work to go through "the established background-processing design," but none
  exists yet in this codebase, and nothing in Phase 6 is slow enough to need one (no media download
  yet — that lands with whichever channel first needs it, likely Phase 7's WhatsApp media handling,
  with its own SSRF-aware fetcher). Revisit when a real workload demands it rather than building
  the queue speculatively.
- **Singleton `IChannelAdapterRegistry`.** Rejected in favor of Scoped: a future adapter may itself
  need a Scoped dependency (e.g. a per-request `DbContext`), and a Singleton registry capturing it
  via `IEnumerable<IChannelAdapter>` at construction would be a captive-dependency bug baked in
  from the start.

## Consequences

- Phase 7 (WhatsApp) registers one `IChannelAdapter` in `AddInfrastructure` and connects an account
  via the new `PUT /api/v1/channels/whatsapp/account` + `/credentials` endpoints — no pipeline,
  routing, or retry code should need to change, only the adapter implementation itself.
- `ChannelAccount.ExternalAccountId` and `ChannelCredential` are the only new persistent state this
  phase adds; both are unused by Manual/WebsiteChat and stay `null`/absent for them.
- `Message.DeliveredAt`/`ReadAt` and `ApplyProviderStatus` exist now but are only exercised once a
  real adapter's status-update webhooks start calling them (Phase 7+) — the Phase 4 decision to
  define the full delivery-status enum up front (rather than migrate it in later) pays off here.
- The webhook endpoint's 403 response is identical for "unsupported channel" being unreachable
  (never happens here — that's 404) versus "signature invalid," and identical again regardless of
  *why* a signature was invalid — deliberately uninformative to anyone probing the endpoint.
