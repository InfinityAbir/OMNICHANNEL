# Channel Integrations

Phase 5 (Website chat) and Phase 6 (generic channel adapter framework) are implemented — see
[ADR-0015](decisions/ADR-0015-website-chat-widget.md) and
[ADR-0016](decisions/ADR-0016-channel-adapter-framework.md). PRD §64–68 phase order:

1. Phase 5 — Website chat (**implemented**; self-hosted embed served by the product API, origin
   allowlist, conversation-scoped SignalR realtime). Not built on `IChannelAdapter` — it predates
   the framework and has no external provider webhook to isolate behind one.
2. Phase 6 — Generic external channel adapter framework (**implemented**; `IChannelAdapter`,
   capability model, encrypted credential model, webhook verify/parse/idempotent-persist pipeline,
   Polly-based retry with provider-error classification). Zero adapters registered in production —
   every real channel type currently 404s at `/webhooks/{channelType}` until Phase 7+ registers
   one. See "Connecting a new channel" below.
3. Phase 7 — WhatsApp Business Platform (**implemented**; text-only send, all inbound message
   types accepted and normalized, media download not yet implemented). See
   [ADR-0017](decisions/ADR-0017-whatsapp-integration.md).
4. Phase 8 — Instagram messaging (Meta) (**implemented**; "Instagram API with Instagram Login"
   model, text-only send). See [ADR-0018](decisions/ADR-0018-instagram-integration.md).
5. Phase 9 — Facebook Messenger (Meta).

**Instagram (Phase 8) setup**: connect via `PUT /api/v1/channels/instagram/account`
(`{"externalAccountId": "<IG-scoped account id>"}`) and `PUT /api/v1/channels/instagram/credentials`
(`{"secret": "<Instagram User access token>"}`). Webhook URL `https://YOUR-API/webhooks/instagram`,
Verify Token = `Instagram:VerifyToken`. `Instagram:AppSecret`/`VerifyToken` are secrets (same
pattern as WhatsApp's) — never committed. No `HUMAN_AGENT` tag support — replies outside the
24-hour window fail with a clear error rather than being silently dropped or auto-tagged (which
would risk a policy violation — that tag is reserved for genuine human-agent use with its own
compliance expectations).

**WhatsApp (Phase 7) setup**: connect via `PUT /api/v1/channels/whatsapp/account`
(`{"externalAccountId": "<phone_number_id>"}`) and `PUT /api/v1/channels/whatsapp/credentials`
(`{"secret": "<permanent access token>"}`) — both `ChannelsManage`-permission, agent-facing.
Webhook subscription (configured once, in the Meta App Dashboard, by whoever operates this
platform's Meta App — not per-tenant): URL `https://YOUR-API/webhooks/whatsapp`, Verify Token =
`WhatsApp:VerifyToken` config. Both `WhatsApp:AppSecret` and `WhatsApp:VerifyToken` are secrets —
set via `dotnet user-secrets`/environment/deployment secret store, never committed (same pattern
as `Jwt:SigningKey`). No template-message support — an agent reply outside the 24-hour customer
service window fails with a clear error rather than silently dropping or retrying forever.

**Connecting a new channel (Phase 7+ implementer checklist):**

1. Implement `IChannelAdapter` (`Omnichannel.Application.Abstractions`) in Infrastructure —
   `VerifyWebhookAsync`, `ParseWebhookAsync`, `SendMessageAsync` — using the provider's own
   documented signature scheme, payload schema, and send API. Register it:
   `services.AddScoped<IChannelAdapter, YourProviderAdapter>()` in `AddInfrastructure`.
2. Nothing else changes — `/webhooks/{yourChannelType}`, the credential/account admin endpoints
   under `/api/v1/channels/{yourChannelType}`, idempotency, retry, and tenant/account resolution
   are already generic (ADR-0016).
3. Business connects the account: `PUT /api/v1/channels/{type}/account` (provider's external
   account id) and `PUT /api/v1/channels/{type}/credentials` (API token/secret — encrypted at
   rest, never returned by any response).
4. Before writing any provider-specific code, follow PRD §66's checklist: read current official
   documentation, identify account/permission requirements, webhook verification process,
   messaging window/template restrictions, supported media, and rate limits — record findings
   here rather than assuming prior knowledge of provider policy is still current.

**Website chat (Phase 5) notes:** the visitor-facing surface served from `wwwroot/widget`
(`embed.js`, `widget.css`, `signalr.min.js`, plus the demo assets). A site embeds it with
`<script src="https://YOUR-API/widget/embed.js" data-slug="SLUG" defer></script>`. Session
creation requires the embedding page's `Origin` to be in the tenant's widget allowlist (set via
the inbox/business-facing settings or `PUT /api/v1/channels/widget/origins`). Message text is
HTML-escaped in the embed; realtime uses the `"Widget"` scheme + `WidgetHub`.

**Hard rule (AGENTS.md, PRD §7):** official platform APIs/SDKs only. No scraping, no browser
automation of consumer clients, no bypassing platform restrictions. Before implementing each
provider, verify current official documentation and record actual supported capabilities,
webhook verification method, messaging windows, template requirements, and rate limits here —
do not assume prior knowledge of provider policy is still current.
