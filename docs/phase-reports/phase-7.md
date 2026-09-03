# Phase Report — Phase 7: WhatsApp Integration

**Status:** Implementation complete. Proceeding to Phase 8 per explicit user instruction (no
approval pause).
**Date:** 2026-09-04

## Scope / PRD references

PRD §66 (Phase 7): the first real channel using the Phase 6 framework. Explicitly requires
reading current official documentation *before* coding — account requirements, permissions,
webhook verification, messaging restrictions/windows, template requirements, supported media,
rate limits, documented assumptions — then implementing OAuth/connection, webhook verification,
incoming/outgoing messages, delivery/read states, media handling, provider error handling.

## Pre-implementation research (done first, per PRD's own instruction)

Read Meta's own developer documentation (not assumed from prior knowledge) — webhook setup, Graph
API webhooks getting-started (signature/handshake mechanics), the messages API reference, error
codes reference, and account-setup requirements. Full findings and every resulting design decision
recorded in [ADR-0017](decisions/ADR-0017-whatsapp-integration.md), including the exact webhook
verification handshake, HMAC-SHA256 signature scheme, inbound/status payload shapes, the 24-hour
customer-service-window rule, rate-limit tiers, and the specific error codes mapped to
`ChannelSendErrorKind`.

## Implemented

- **`WhatsAppChannelAdapter`** (`Omnichannel.Infrastructure.Channels`) — the first production
  `IChannelAdapter`, registered via `AddHttpClient<IChannelAdapter, WhatsAppChannelAdapter>()`.
  - `VerifyWebhookAsync`: GET handshake (`hub.mode`/`hub.verify_token`/`hub.challenge`,
    constant-time token comparison) and POST signature verification (`X-Hub-Signature-256`
    HMAC-SHA256 over the raw body, constant-time comparison) against a **platform-level** App
    Secret/Verify Token (ADR-0017 — Meta signs at the App level, not per tenant).
  - `ParseWebhookAsync`: maps `entry[].changes[].value.{messages,statuses}` to
    `NormalizedInboundEvent` — `phone_number_id` → `ProviderAccountExternalId`, `wamid...` →
    `ExternalMessageId`, `from`/contact profile name → visitor identity, text body or a typed
    placeholder for non-text content, status strings → `MessageDeliveryStatus`. Malformed JSON
    returns an empty list rather than throwing.
  - `SendMessageAsync`: `POST {phone_number_id}/messages` with the tenant's own stored access
    token; classifies every Meta error code into `ChannelSendErrorKind` (190→AuthFailed;
    4/80007/130429/HTTP 429→RateLimited; 131026→InvalidRecipient; 131047 [outside 24h
    window]→PermanentFailure, not retried; 5xx/network/timeout→Transient).
- **`WhatsAppOptions`**: platform config (`AppSecret`, `VerifyToken`, `GraphApiVersion`,
  `GraphApiBaseUrl`) — secrets set via `dotnet user-secrets`/deployment secret store, never
  committed; non-secret defaults (`GraphApiVersion`, `GraphApiBaseUrl`) in `appsettings.json`.
- **Per-tenant connection**: manual entry through Phase 6's existing generic admin endpoints — no
  new endpoints needed, `IChannelAdapter`'s interface required zero changes.
- **`ChannelAdapterRegistry` fix**: changed from first-registration-wins to last-registration-wins,
  so a test host can override the now-real production WhatsApp adapter with a fake by registering
  one later in `ConfigureTestServices` (standard ASP.NET Core testing override pattern) — needed
  once a real adapter existed for tests to override, not before.

## Root-causes found and fixed during the phase

- **`ChannelAdapterRegistry` selection bug, caught before it could hide any test behind the real
  adapter**: the original Phase 6 registry picked the *first* registered `IChannelAdapter` per
  type via `GroupBy(...).First()`. That was inert while zero real adapters existed, but the moment
  `WhatsAppChannelAdapter` was registered in production DI, every existing Phase 6 test's
  `ConfigureTestServices`-registered fake adapter would have silently lost to the real one (DI
  registration order, not test intent) — a real adapter making real (mocked-away, but still
  attempted) HTTP calls instead of the test's controlled fake. Fixed to `Last()` and reran the
  full suite to confirm the fakes still win.
- Verified end-to-end with a real HTTP round-trip: signed a test payload with the exact HMAC
  scheme Meta documents, using the test-only `WhatsApp:AppSecret` in `appsettings.Testing.json`
  (committed, non-secret, same pattern as the JWT test signing key) — proves the adapter's
  signature logic and Program.cs's DI wiring are correct together, not just in isolation.

## Tests

- **Unit/Integration**: 13/13 new (`WhatsAppChannelAdapterTests`, in `Omnichannel.IntegrationTests`
  since the adapter lives in Infrastructure) — GET handshake valid/invalid, POST signature
  valid/tampered/missing-header, inbound message + status-update parsing against real Meta payload
  shapes, malformed-JSON doesn't throw, send success/auth-failure/window-expired/rate-limited
  (retryable exception) against a stubbed `HttpMessageHandler`.
- **API**: 4/4 new (`WhatsAppEndToEndTests`) — real adapter through the real HTTP pipeline: GET
  handshake with the configured verify token, wrong token rejected, unsigned POST rejected and
  nothing persisted, correctly-signed POST accepted.
- **Security**: 2/2 new (`WhatsAppWebhookSecurityTests`) — forged signature rejected and never
  persisted, genuine signature routes only to the tenant that connected the matching
  `phone_number_id` (re-verified against the real adapter, not only Phase 6's generic fake-adapter
  coverage).
- **Full suite**: 92/92 (32 unit + 13 integration + 16 security + 31 API), rerun twice to rule out
  the kind of test-isolation flakiness Phase 6 itself hit once already.
- **CI**: verified green via `gh run watch` after push, not just local output.

## Security Review

Addressed PRD §66's explicit focus list: webhook signature verification, credential encryption,
token lifecycle, replay protection, tenant/account mapping, outbound authorization. Full detail in
`docs/security.md`'s "Phase 7 controls" section and review-log entry. No high/critical findings.

## Performance/Reliability Review

- Signature/token comparisons use `CryptographicOperations.FixedTimeEquals` (constant-time) rather
  than `==`/`string.Equals` — a length- or early-exit-timing side channel on a security boundary,
  avoided by construction.
- `131047` (outside the 24-hour window) fails fast as `PermanentFailure` rather than being retried
  — retrying a condition that can never resolve via retry would waste attempts and delay the real
  signal (agent needs to send a template, which isn't supported yet) for no benefit.
- No new background workload — sending stays synchronous within the agent's own request, same as
  every other channel.

## Migrations / Configuration Changes

- No schema change — Phase 6 already added everything WhatsApp needed (`ExternalAccountId`,
  `ChannelCredential`).
- New config: `WhatsApp:{GraphApiVersion,GraphApiBaseUrl}` in `appsettings.json` (non-secret);
  `WhatsApp:{AppSecret,VerifyToken}` via secrets only (not committed); test-only values added to
  `appsettings.Testing.json` (committed, matches the existing JWT-signing-key pattern).

## ADRs / Docs Updated

New [ADR-0017](decisions/ADR-0017-whatsapp-integration.md). `docs/architecture.md` (new "WhatsApp
integration" section), `docs/security.md` (new "Phase 7 controls" section + review-log entry),
`docs/integrations.md` (Phase 7 marked implemented + setup instructions).

## Known Limitations

- No template-message support — outbound is free-form text only, which only works inside the
  24-hour customer service window (Meta's own restriction, not a gap in this implementation).
- No media download/storage — inbound non-text messages are correctly typed but their content
  isn't fetched (ADR-0017); a future phase adds this with an SSRF-aware fetcher, not a bare
  `HttpClient.GetAsync` on a provider-supplied URL.
- No Embedded Signup (OAuth self-service connection) — businesses connect by entering their
  `phone_number_id` and access token directly via the admin API (no UI yet, same limitation Phase
  6 already noted for the general case).
- Rate-limit tiers (250/1,000/10,000/100,000/unlimited unique recipients per day, per Meta's own
  business-verification-based tiers) aren't tracked or pre-emptively throttled client-side — sends
  that exceed them surface as `RateLimited` (retried) from Meta's own response, not prevented in
  advance.

## Files/Modules Changed

`src/Omnichannel.Infrastructure/Channels/{WhatsAppChannelAdapter,WhatsAppOptions,
ChannelAdapterRegistry}.cs`, `src/Omnichannel.Infrastructure/DependencyInjection.cs`,
`src/Omnichannel.Api/appsettings.{json,Testing.json}`,
`tests/Omnichannel.IntegrationTests/WhatsAppChannelAdapterTests.cs` (new),
`tests/Omnichannel.ApiTests/Channels/WhatsAppEndToEndTests.cs` (new),
`tests/Omnichannel.SecurityTests/WhatsAppWebhookSecurityTests.cs` (new),
`docs/decisions/ADR-0017` (new), `docs/{architecture,security,integrations}.md`.

## Next Phase

Phase 8 — Instagram Integration (PRD §67): verify current official Meta API capabilities first
(per PRD's own instruction, same as this phase), account connection, webhook verification,
incoming DM processing, outbound replies, supported media, delivery state, error mapping. Likely
shares the same Graph API webhook mechanics as WhatsApp (App-level signature, GET handshake) —
to be confirmed during Phase 8's own research step, not assumed identical.

**Proceeding directly to Phase 8 per explicit user instruction — no approval pause.**
