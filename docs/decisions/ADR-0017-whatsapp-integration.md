# ADR-0017: WhatsApp Business Platform (Cloud API) integration

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §66 (Phase 7) is the first real consumer of Phase 6's `IChannelAdapter` framework
(ADR-0016). Per PRD's own instruction, official documentation was read before writing any code —
findings below, cited from Meta's own developer docs (`developers.facebook.com/docs/whatsapp/...`
and `developers.facebook.com/docs/graph-api/webhooks/...`), not assumed from prior training data.

### Research findings

- **Account model**: a business needs a verified Meta Business Account, a WhatsApp Business
  Account (WABA), and a phone number registered to it. Sending requires a permanent access token
  (System User or long-lived user token) scoped with `whatsapp_business_messaging` (and
  `whatsapp_business_management` for account-level operations).
- **Webhook verification is two-part**: (1) a one-time GET handshake at subscription setup —
  `hub.mode=subscribe`, `hub.verify_token` (a string the *platform operator* configures once in
  the Meta App Dashboard, shared across all tenants — not tenant-specific), `hub.challenge` (must
  be echoed back verbatim). (2) Every POST delivery is HMAC-SHA256 signed over the raw body using
  the Meta **App's** own App Secret, header `X-Hub-Signature-256: sha256=<hex>`. Both are
  **App-level**, not per-WABA/per-tenant — Meta signs with the one secret belonging to whichever
  Meta App owns the webhook subscription.
- **Inbound payload shape**: `entry[].changes[].value.metadata.phone_number_id` identifies the
  receiving number; `messages[]` (customer→business) carry `from`/`id`/`timestamp`/`type` plus
  type-specific content (`text.body` for text); `statuses[]` (delivery receipts) carry
  `id`/`status` (`sent`/`delivered`/`read`/`failed`)/`timestamp`.
- **Outbound**: `POST https://graph.facebook.com/{version}/{phone_number_id}/messages`, Bearer
  token = the WABA's own access token, JSON body `{messaging_product, recipient_type, to, type,
  text: {body}}`. Success returns `{messages: [{id}]}`; failure returns `{error: {message, code,
  ...}}`.
- **24-hour customer service window**: free-form (non-template) messages are only deliverable
  within 24 hours of the customer's last message; outside that, Meta requires a pre-approved
  message *template*, returned as error code `131047` ("re-engagement") otherwise.
- **Rate limits**: tiered by business verification/quality (250 → unlimited unique recipients/day
  as a WABA's messaging tier increases); throughput-specific errors are `4`, `80007`, `130429`.
  `190` = expired/invalid access token. `131026` = recipient unreachable/not on WhatsApp.

## Decision

**Platform-level App Secret + Verify Token** (`WhatsAppOptions.AppSecret`/`VerifyToken`, deployment
config/secrets — not per-tenant, not in `IChannelCredentialStore`) drive `VerifyWebhookAsync`. This
matches Meta's own model (one App, many connected WABAs) and required no change to
`IChannelAdapter`'s interface — `VerifyWebhookAsync(WebhookRequest)` never took a channel-account
parameter in the first place (ADR-0016), because verification must run *before* the tenant is even
resolved (parsing/routing happens after).

**Per-tenant connection stays manual entry**, using Phase 6's existing generic admin endpoints
(`PUT /api/v1/channels/whatsapp/account` for the `phone_number_id`, `PUT .../credentials` for the
WABA's access token) rather than building Meta's Embedded Signup (OAuth) flow. Embedded Signup is
a substantial feature in its own right (JS SDK embed, token exchange, WABA discovery) that PRD §66
doesn't explicitly mandate — it lists "OAuth/business account connection **where applicable**,"
and manual entry is a legitimate, honest way to connect an account without inventing UI the PRD
didn't ask for this phase. Documented as a known limitation, not silently skipped.

**Outbound is text-only this phase.** `ChannelCapabilities.SupportsMedia = false` — the Angular
composer has no media-upload UI to drive it, and building one plus WhatsApp's media-upload flow
(a separate two-step upload-then-reference API) is its own scope, consistent with Phase 5/6's own
deferred-attachments precedent. Inbound non-text messages are still accepted and normalized (their
`ContentType` is set correctly) but recorded with a placeholder text and no binary download — media
storage/SSRF-safe fetching is real future work, not assumed away.

**Error classification** (`ClassifyError`) maps Meta's codes to `ChannelSendErrorKind`: `190` →
`AuthFailed`; `4`/`80007`/`130429`/HTTP 429 → `RateLimited` (retried by `ChannelSendService`);
`131026` → `InvalidRecipient`; `131047` (outside 24h window) → `PermanentFailure` — retrying can
never succeed without template support, which doesn't exist yet, so failing fast and telling the
agent why is more honest than silently retrying into certain failure; 5xx/network/timeout →
`Transient` (retried).

**Signature comparison uses `CryptographicOperations.FixedTimeEquals`** (constant-time), not
`==`/`string.Equals`, for both the verify-token and HMAC comparisons — a length- or
early-exit-timing side channel on a security boundary is exactly the class of bug worth avoiding
by construction rather than by review.

## Alternatives considered

- **Per-tenant Meta Apps (each business brings their own App Secret).** Rejected: contradicts how
  Meta's own webhook model works (one App owns one webhook subscription URL) and would require
  either N webhook endpoints or an App-Secret-per-request lookup with no way to know which tenant
  a raw, unverified payload belongs to before parsing it — a chicken-and-egg problem the Tech
  Provider (shared-App) model avoids entirely.
- **Build Embedded Signup now.** Rejected for this phase — see "Decision" above; revisit if/when a
  real business needs self-service onboarding rather than the platform operator configuring
  connections directly.
- **Retry `131047` (window expired) as Transient.** Rejected: the condition never resolves via
  retry — only the customer re-messaging or a template send fixes it — so retrying wastes attempts
  and delays the real signal (agent needs to know a template is required) for no benefit.

## Consequences

- Phase 8/9 (Instagram/Messenger) can very likely reuse the same "platform App Secret,
  per-tenant-account manual entry, text-only-first" shape — Meta's Graph API webhook mechanics
  (GET handshake, `X-Hub-Signature-256`) are shared infrastructure across all three Meta channels,
  not WhatsApp-specific. Worth checking during Phase 8's own documentation-first step rather than
  assumed identical.
- No template message support exists — an agent replying outside the 24h window gets a clear
  `PermanentFailure` with Meta's own error detail, not a silent drop or a misleading retry loop.
- `Message.ContentType` is accurate for non-text inbound messages even though the content itself
  isn't downloaded yet — a later media-handling phase only needs to add the download/storage path,
  not touch the normalization logic that already classifies message types correctly.
