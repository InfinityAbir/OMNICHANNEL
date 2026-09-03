# Phase Report — Phase 8: Instagram Integration

**Status:** Implementation complete. Proceeding to Phase 9 per explicit user instruction (no
approval pause).
**Date:** 2026-09-04

## Scope / PRD references

PRD §67 (Phase 8): verify current official Meta API capabilities first, implement only supported
business/professional messaging capabilities — account connection, webhook verification, incoming
DM processing, outbound replies, supported media, delivery state, error mapping. Security review:
"same webhook/credential/tenant requirements," plus explicitly test incorrect account mapping,
cross-tenant channel access, unauthorized outbound messages.

## Pre-implementation research (done first, per PRD's own instruction)

Read Meta's current Instagram Messaging API documentation independently this phase (not carried
over from Phase 7's WhatsApp research) — confirmed the same underlying Graph API webhook mechanics
apply (GET handshake, HMAC-SHA256 signature), but found real differences: a distinct connection
model ("Instagram API with Instagram Login," Meta's current recommendation over the older
Facebook-Login/Page-token approach), a different inbound payload shape, and — critically —
**millisecond** webhook timestamps versus WhatsApp's seconds. Full findings and every resulting
decision in [ADR-0018](decisions/ADR-0018-instagram-integration.md).

## Implemented

- **`InstagramChannelAdapter`** (`Omnichannel.Infrastructure.Channels`) — second production
  `IChannelAdapter`, mirroring `WhatsAppChannelAdapter`'s structure but with Instagram's own wire
  shapes throughout (not copy-pasted-and-hoped): `graph.instagram.com` base URL, `{recipient:
  {id}, message: {text}}` send body, `{object, entry: [{id, messaging: [...]}]}` webhook envelope,
  `MapContentType` for image/video/audio/file attachments, its own millisecond timestamp parser.
- **`InstagramOptions`**: own `AppSecret`/`VerifyToken`/`GraphApiVersion`/`GraphApiBaseUrl` —
  deliberately not shared with `WhatsAppOptions` (ADR-0018 explains why).
- **Error classification**: 190→AuthFailed; 4/17/32/613/HTTP 429→RateLimited; everything else
  (including any code not confirmed during research) →PermanentFailure, not guessed into the
  retry-eligible set.
- **Delivery/read receipts**: `delivery.mids[]`/`read.mid` map to `MessageDeliveryStatus.Delivered`/
  `.Read` through the same generic status-update pipeline every channel uses.
- **DI wiring**: `AddHttpClient<IChannelAdapter, InstagramChannelAdapter>()` alongside WhatsApp's —
  zero changes needed to `WebhookIngestionService`, `ChannelSendService`, or the generic admin
  endpoints; `IChannelAdapter`'s interface absorbed a second real implementation without any
  modification, which is exactly what ADR-0016 was designed to make true.

## Tests

- **Integration**: 20/20 (13 WhatsApp + 7 new `InstagramChannelAdapterTests`: GET handshake,
  tampered-signature rejection, inbound text-message parsing against Meta's own documented
  payload shape, delivery-receipt→status-update mapping, send success/auth-failure/rate-limited).
- **API**: 35/35 (31 prior + 4 new `InstagramEndToEndTests` — real adapter through the real HTTP
  pipeline).
- **Security**: 18/18 (16 prior + 2 new `InstagramWebhookSecurityTests` — forged signature
  rejected and never persisted, genuine signature routes only to the connected tenant, re-verified
  against the real adapter per PRD §67's explicit cross-tenant/account-mapping test requirement).
- **Full suite**: 105/105, rerun twice.
- **CI**: verified green via `gh run watch` after push.

## Security Review

Addressed PRD §67's focus list including its explicit "also test" additions (incorrect account
mapping, cross-tenant channel access, unauthorized outbound messages) — all three specifically
re-verified against Instagram's real adapter, not assumed covered by Phase 6's generic tests or
Phase 7's WhatsApp-specific ones. Full detail in `docs/security.md`'s "Phase 8 controls" section.
No high/critical findings.

## Performance/Reliability Review

- Same constant-time signature/token comparison discipline as WhatsApp.
- Millisecond-vs-second timestamp handling was caught by research *before* writing the parser, not
  discovered later via a wrong-by-decades `OccurredAt` value in production — worth calling out as
  exactly the kind of bug PRD §67's "verify current official documentation first" instruction
  exists to prevent.
- No new background workload; sending stays synchronous, same as every other channel.

## Migrations / Configuration Changes

- No schema change — Phase 6's generic `ChannelAccount.ExternalAccountId`/`ChannelCredential`
  cover Instagram unchanged.
- New config: `Instagram:{GraphApiVersion,GraphApiBaseUrl}` in `appsettings.json` (non-secret);
  `Instagram:{AppSecret,VerifyToken}` via secrets only; test-only values in
  `appsettings.Testing.json`.

## ADRs / Docs Updated

New [ADR-0018](decisions/ADR-0018-instagram-integration.md). `docs/architecture.md` (new
"Instagram integration" section), `docs/security.md` (new "Phase 8 controls" section + review-log
entry), `docs/integrations.md` (Phase 8 marked implemented + setup instructions).

## Known Limitations

- No media download/storage (inbound attachments are correctly typed, not fetched) — same
  cross-channel limitation as WhatsApp.
- No `HUMAN_AGENT` tag support — replies outside the 24-hour window fail with a clear error rather
  than risk a policy violation from auto-applying a tag reserved for audited human-agent use.
- No Embedded Signup — manual account/credential entry only, same as WhatsApp.
- No confirmed Instagram-specific "invalid recipient" error code — unrecognized send errors are
  conservatively treated as permanent (not retried) rather than guessed at.

## Files/Modules Changed

`src/Omnichannel.Infrastructure/Channels/{InstagramChannelAdapter,InstagramOptions}.cs`,
`src/Omnichannel.Infrastructure/DependencyInjection.cs`,
`src/Omnichannel.Api/appsettings.{json,Testing.json}`,
`tests/Omnichannel.IntegrationTests/InstagramChannelAdapterTests.cs` (new),
`tests/Omnichannel.ApiTests/Channels/InstagramEndToEndTests.cs` (new),
`tests/Omnichannel.SecurityTests/InstagramWebhookSecurityTests.cs` (new),
`docs/decisions/ADR-0018` (new), `docs/{architecture,security,integrations}.md`.

## Next Phase

Phase 9 — Facebook Messenger Integration (PRD §68): verify current official API requirements
first (same discipline as Phases 7-8), page connection, webhook handling, incoming/outgoing
messages, message status, provider errors.

**Proceeding directly to Phase 9 per explicit user instruction — no approval pause.**
