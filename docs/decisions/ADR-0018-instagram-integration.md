# ADR-0018: Instagram Messaging integration

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §67 (Phase 8), same instruction as Phase 7: verify current official Meta API capabilities
before implementing. Research below is from Meta's own current developer documentation
(`developers.facebook.com/documentation/instagram-platform/...`), read this phase rather than
assumed from ADR-0017's WhatsApp findings.

### Research findings

- **Two connection models exist**; Meta's current recommendation for new integrations is
  **"Instagram API with Instagram Login"** — `graph.instagram.com`, IG-scoped ids directly, an
  Instagram User access token — rather than the older Facebook-Login-plus-linked-Page model. This
  ADR adopts the current-recommended model, per PRD's "verify current official Meta API
  capabilities" instruction, not the legacy one.
- **Account requirements**: an Instagram **Business or Creator** account (personal accounts
  unsupported), admin access to complete setup. Permissions:
  `instagram_business_manage_messages` (naming has shifted across Meta's docs revisions —
  recorded as found; re-verify at implementation time for any future channel copying this
  pattern, don't assume permission names are stable long-term).
- **Webhook mechanics are the same Graph API family as WhatsApp** (confirmed, not assumed): GET
  handshake (`hub.mode`/`hub.verify_token`/`hub.challenge`), POST deliveries HMAC-SHA256 signed
  over the raw body via `X-Hub-Signature-256`, at the **App level** (same reasoning as ADR-0017 —
  platform-operator-configured, not per-tenant).
- **Inbound payload shape** differs from WhatsApp's: `{object: "instagram", entry: [{id, time,
  messaging: [{sender: {id}, recipient: {id}, timestamp, message: {mid, text, attachments}}]}]}`.
  `entry[].id` is the receiving account's own IG-scoped id (→ `ProviderAccountExternalId`);
  `messaging[].sender.id` is the visitor; `message.mid`/`.text` map directly.
  **Timestamps are milliseconds**, not seconds like WhatsApp's — a real, easy-to-miss difference,
  handled with a separate `ParseUnixTimestampMilliseconds` rather than reusing WhatsApp's
  seconds-based parsing.
- **Outbound**: `POST {IG_ID}/messages`, body `{recipient: {id}, message: {text}}` — a distinct
  shape from WhatsApp's `{messaging_product, recipient_type, to, type, text}`, response
  `{recipient_id, message_id}`.
- **24-hour window**, same concept as WhatsApp's, extendable to 7 days only via a `HUMAN_AGENT` tag
  reserved strictly for genuine human-agent replies — automated use is a documented policy
  violation. Not implemented this phase (see Decision).
- **Media**: images/video/audio/file attachments supported inbound and outbound, with documented
  size limits. Not implemented outbound this phase (see Decision).
- **Rate limits**: Business Use Case limit ~200 calls/user/hour; throttling surfaces as HTTP 429
  or error codes 4/17/32/613. No Instagram-specific "invalid recipient" error code was found
  during this research (unlike WhatsApp's documented 131026) — recorded as a gap, not guessed at.

## Decision

**Own `InstagramOptions` (App Secret/Verify Token), not shared with `WhatsAppOptions`.** Meta's
Instagram Login apps are commonly configured as separate Apps from WhatsApp/Business apps in the
developer dashboard; assuming they're the same app without confirming it would be an unverified
assumption baked into the config shape. A deployer who does use one shared App can simply set both
sections to the same values — the code doesn't force separation, it just doesn't assume unification
either.

**Text-only outbound**, same reasoning as ADR-0017 — no media-upload UI in the composer to drive
it. Inbound attachments are typed correctly via `MessageContentType` but not downloaded.

**No `HUMAN_AGENT` tag support.** Extending the response window is a real, distinct feature with
its own compliance requirement (only for genuine human-authored replies, which — since every
outbound send in this product IS an agent typing in the inbox — this system could arguably satisfy
honestly). Deferred anyway: implementing it half-way (without the tag-usage auditing Meta's policy
implies) is worse than not implementing it and documenting why, consistent with AGENTS.md's
"default behavior should be conservative."

**Unrecognized send error codes classify as `PermanentFailure`, not retried.** Unlike WhatsApp
(ADR-0017), no confirmed "invalid recipient" code exists in what this research found — rather than
guess an unconfirmed code into the retry-eligible set (`Transient`/`RateLimited`) and risk masking
a real permanent failure behind pointless retries, only the codes actually confirmed during
research (190, 4/17/32/613, HTTP 429/5xx) get special classification; everything else fails fast.

## Alternatives considered

- **Legacy Facebook-Login/Page-token model.** Rejected: not Meta's current recommendation for new
  integrations; would add a second, older connection shape for no benefit to a platform with no
  existing Page-model integrations to preserve compatibility with.
- **Reuse WhatsApp's `ParseUnixTimestamp` (seconds) for Instagram.** Rejected once the
  documentation confirmed Instagram's webhook timestamps are milliseconds — would have silently
  produced wrong `OccurredAt` values (year ~57000) had it been copied without checking.
- **Guess an "invalid recipient" code by analogy to WhatsApp's 131026.** Rejected — see Decision.
  Wrong is worse than absent for a security/reliability classification.

## Consequences

- Phase 9 (Messenger) is the third Meta channel — likely the *fourth* confirmation, not
  assumption, that the same Graph API webhook mechanics apply; still worth its own research pass
  per PRD §68's own instruction, since Messenger's send/receive payload shapes are expected to
  differ from both WhatsApp's and Instagram's even if the webhook envelope doesn't.
- If a future phase needs cross-channel HUMAN_AGENT-style window extension or media send, it's an
  addition to each adapter individually — no shared abstraction was built prematurely for a
  capability that isn't implemented anywhere yet.
