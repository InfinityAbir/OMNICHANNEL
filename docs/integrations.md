# Channel Integrations

Phase 5 (Website chat) is implemented as the first channel — see
[ADR-0015](decisions/ADR-0015-website-chat-widget.md). The remaining roadmap is target
architecture (`IChannelAdapter`, capability model, webhook pipeline) and PRD §64–68 phase order:

1. Phase 5 — Website chat (**implemented**; self-hosted embed served by the product API, origin
   allowlist, conversation-scoped SignalR realtime).
2. Phase 6 — Generic external channel adapter framework (interfaces, capability model,
   credential model, webhook pipeline, idempotency, retry architecture).
3. Phase 7 — WhatsApp Business Platform.
4. Phase 8 — Instagram messaging (Meta).
5. Phase 9 — Facebook Messenger (Meta).

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
