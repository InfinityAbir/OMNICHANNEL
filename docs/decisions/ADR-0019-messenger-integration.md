# ADR-0019: Facebook Messenger integration

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §68 (Phase 9), same documentation-first instruction as Phases 7-8. Research this phase from
Meta's own Messenger Platform documentation and current developer references, checked
independently rather than assumed identical to WhatsApp (ADR-0017) or Instagram (ADR-0018).

### Research findings

- **Webhook envelope is the same Graph API shape as Instagram**: `{object: "page", entry:
  [{id, time, messaging: [{sender, recipient, timestamp, message}]}]}`, millisecond timestamps,
  same GET handshake + `X-Hub-Signature-256` HMAC-SHA256 POST verification.
- **Send API authentication genuinely differs**: the Page access token is passed as an
  **`access_token` query-string parameter** on the request URL
  (`POST /{PAGE_ID}/messages?access_token=...`), not a `Bearer` Authorization header like
  WhatsApp/Instagram. Confirmed by checking rather than assumed identical to the other two — a
  real difference this phase's research caught before writing the send call.
- **Send body**: `{recipient: {id}, messaging_type: "RESPONSE", message: {text}}` —
  `messaging_type` is Messenger-specific (no WhatsApp/Instagram equivalent in what either of those
  adapters send).
- **Delivery vs. read receipts differ in shape**: a `delivery` event carries a `mids` array
  (per-message ids) *and* a `watermark` — but Meta's own docs note `mids` isn't always present
  ("backward compatibility" with older clients). A `read` event carries **only** a `watermark`
  timestamp ("all messages before this time were read") — no per-message id at all.
- **Rate limiting**: same Graph API error family as Instagram (190 auth, 4/17/32/613 + HTTP 429
  rate-limit), re-confirmed rather than assumed carried over.
- **Requirements**: `pages_messaging` permission, a Page access token requested by someone who can
  perform the `MESSAGE` task on the Page.

## Decision

**Query-string `access_token`, not a Bearer header**, in `SendMessageAsync` — the one place this
adapter's HTTP call genuinely differs in shape from WhatsApp/Instagram's, implemented as found
rather than copy-pasted from either.

**Delivery receipts map when `mids` is present; read receipts don't map at all.** A `read` event's
watermark-only shape has no message id to attach a `NormalizedInboundEvent.StatusUpdate` to under
this pipeline's id-based model (ADR-0016) — extending the model to support a watermark-range
update (mark every message before timestamp X as read) is a real, separate feature, not something
to bolt on as a special case for one channel. Documented as a known limitation, not silently
dropped without explanation.

**Own `MessengerOptions`**, same reasoning as Instagram's separate options (ADR-0018) — not
assumed to share a Meta App with WhatsApp or Instagram Login apps.

**Same error-code classification as Instagram** (190/4/17/32/613/429), since this phase's own
research reconfirmed the same Graph API error family applies — not blindly copied, verified again.

## Alternatives considered

- **Assume Bearer-header auth like the other two Meta channels.** Rejected once research showed
  the Send API's own documented examples consistently use the query-string form — building it the
  "consistent-looking" way instead of the documented way would have shipped a broken integration.
- **Approximate read receipts using the watermark against stored message timestamps** (mark every
  message with `SentAt <= watermark` as read). Rejected for this phase: works, but is a genuinely
  different capability (batch/range status update vs. this pipeline's per-event id-based model)
  that deserves its own deliberate design, not an ad-hoc special case slipped into one adapter.

## Consequences

- `IChannelAdapter`/`WebhookIngestionService`/`ChannelSendService` absorbed a third real
  implementation with zero interface changes across three phases — the strongest evidence yet that
  ADR-0016's abstraction boundary was drawn in the right place.
- If a future phase wants Messenger (or Instagram) read receipts, the fix is a deliberate addition
  to `NormalizedInboundEvent`/the status pipeline (e.g. a watermark-range status kind), not a
  Messenger-specific hack — captured here so that work starts from an accurate account of why it
  doesn't exist yet, not "someone forgot."
