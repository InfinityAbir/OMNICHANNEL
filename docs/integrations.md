# Channel Integrations

No channel adapters exist yet. This document is the placeholder the phase-by-phase work fills
in — see PRD §7/§18/§54/§55 for the target architecture (`IChannelAdapter`, capability model,
webhook pipeline) and PRD §64–68 for the phase order:

1. Phase 5 — Website chat (first, fully controllable, proves the abstraction).
2. Phase 6 — Generic external channel adapter framework (interfaces, capability model,
   credential model, webhook pipeline, idempotency, retry architecture).
3. Phase 7 — WhatsApp Business Platform.
4. Phase 8 — Instagram messaging (Meta).
5. Phase 9 — Facebook Messenger (Meta).

**Hard rule (AGENTS.md, PRD §7):** official platform APIs/SDKs only. No scraping, no browser
automation of consumer clients, no bypassing platform restrictions. Before implementing each
provider, verify current official documentation and record actual supported capabilities,
webhook verification method, messaging windows, template requirements, and rate limits here —
do not assume prior knowledge of provider policy is still current.
