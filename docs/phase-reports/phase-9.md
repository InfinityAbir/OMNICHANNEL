# Phase Report — Phase 9: Facebook Messenger Integration

**Status:** Implementation complete. Proceeding to Phase 10 per explicit user instruction (no
approval pause).
**Date:** 2026-09-04

## Scope / PRD references

PRD §68 (Phase 9): verify current official API requirements first (same discipline as Phases
7-8), page connection, webhook handling, incoming/outgoing messages, message status, provider
errors. Security review: "repeat external integration security checklist."

## Pre-implementation research (done first, per PRD's own instruction)

Read Meta's Messenger Platform documentation independently — confirmed the webhook envelope
matches Instagram's shape (`{object: "page", entry: [{id, messaging: [...]}]}`, millisecond
timestamps, same GET handshake/HMAC-SHA256 mechanics), but found a genuine difference the other
two adapters don't share: the Send API passes the access token as a **query-string parameter**,
not a Bearer header. Also found delivery/read receipts have different shapes from each other
(delivery has optional per-message `mids`; read has only a watermark, no message id at all).
Full findings and decisions in [ADR-0019](decisions/ADR-0019-messenger-integration.md).

## Implemented

- **`MessengerChannelAdapter`** (`Omnichannel.Infrastructure.Channels`) — third production
  `IChannelAdapter`. `SendMessageAsync` builds the URL with `?access_token=...` rather than
  setting an `Authorization` header (the one place its HTTP call genuinely differs from
  WhatsApp/Instagram's). Send body includes Messenger-specific `messaging_type: "RESPONSE"`.
- **`MessengerOptions`**: own `AppSecret`/`VerifyToken`/`GraphApiVersion`/`GraphApiBaseUrl`.
- **Delivery/read receipt handling matches what's actually documented**: `delivery.mids[]` maps to
  `Delivered` status updates when present (Meta's own docs say it isn't always); `read` events
  (watermark-only, no message id) intentionally produce no event — verified by a test that asserts
  this rather than silently letting it happen unnoticed.
- **DI wiring**: `AddHttpClient<IChannelAdapter, MessengerChannelAdapter>()` — zero changes to the
  generic pipeline, same as WhatsApp/Instagram before it.

## Tests

- **Integration**: 28/28 (20 prior + 8 new `MessengerChannelAdapterTests`, including one that
  specifically asserts the request uses `?access_token=` and *not* an `Authorization` header, and
  one confirming a watermark-only read event produces no status update).
- **API**: 39/39 (35 prior + 4 new `MessengerEndToEndTests`).
- **Security**: 20/20 (18 prior + 2 new `MessengerWebhookSecurityTests` — forged signature
  rejected, genuine signature routes only to the connected tenant).
- **Full suite**: 119/119, rerun twice.
- **CI**: verified green via `gh run watch` after push.

## Security Review

Repeated the external-integration security checklist per PRD §68's explicit instruction (not
assumed carried over from Phase 7/8) — full detail in `docs/security.md`'s "Phase 9 controls"
section. Specifically checked that the query-string access-token mechanism doesn't introduce a new
logging/exposure risk beyond what the existing logging policy already forbids. No high/critical
findings.

## Performance/Reliability Review

- Same constant-time signature/token comparison discipline as WhatsApp/Instagram.
- No new background workload.
- The "don't guess an unsupported mapping" discipline from Phase 8 (unconfirmed error codes stay
  `PermanentFailure`) applied again here for read receipts: rather than approximate them from the
  watermark against stored message timestamps, that's left undone and documented, since it's a
  genuinely different capability (range-based status update) from what this pipeline models.

## Migrations / Configuration Changes

- No schema change.
- New config: `Messenger:{GraphApiVersion,GraphApiBaseUrl}` (non-secret, `appsettings.json`);
  `Messenger:{AppSecret,VerifyToken}` via secrets only; test-only values in
  `appsettings.Testing.json`.

## ADRs / Docs Updated

New [ADR-0019](decisions/ADR-0019-messenger-integration.md). `docs/architecture.md` (new
"Messenger integration" section, "what's not here yet" updated — all three PRD-scoped Meta
channels now real), `docs/security.md` (new "Phase 9 controls" section + review-log entry),
`docs/integrations.md` (Phase 9 marked implemented + setup instructions).

## Known Limitations

- No read-receipt support — Messenger's `read` webhook event has no per-message id under the
  current status-update model; would need a deliberate model extension (ADR-0019), not a
  per-channel hack.
- No media download/storage, no Embedded Signup — same cross-channel limitations as WhatsApp/
  Instagram.
- Delivery receipts only map when the provider includes explicit `mids` (documented by Meta as not
  guaranteed for backward-compatibility reasons).

## Files/Modules Changed

`src/Omnichannel.Infrastructure/Channels/{MessengerChannelAdapter,MessengerOptions}.cs`,
`src/Omnichannel.Infrastructure/DependencyInjection.cs`,
`src/Omnichannel.Api/appsettings.{json,Testing.json}`,
`tests/Omnichannel.IntegrationTests/MessengerChannelAdapterTests.cs` (new),
`tests/Omnichannel.ApiTests/Channels/MessengerEndToEndTests.cs` (new),
`tests/Omnichannel.SecurityTests/MessengerWebhookSecurityTests.cs` (new),
`docs/decisions/ADR-0019` (new), `docs/{architecture,security,integrations}.md`.

## Next Phase

Phase 10 — AI Suggestion Mode (PRD §69): AI provider abstraction, prompt/context builder,
conversation summarization, knowledge retrieval abstraction, suggested-reply endpoint, AI
confidence, human approval workflow, AI interaction logging. First use of the AI provider
credential the user has already supplied (Groq) — stored via `dotnet user-secrets`, never
committed; the specific model gets chosen deliberately as part of this phase's design, not
guessed ahead of time.

**Proceeding directly to Phase 10 per explicit user instruction — no approval pause.**
