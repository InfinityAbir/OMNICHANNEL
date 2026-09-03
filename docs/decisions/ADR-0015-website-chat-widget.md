# ADR-0015: Website chat widget (anonymous customer channel)

**Status:** Accepted
**Date:** 2026-09-04

## Context

Phase 5 (PRD §64) requires the first complete channel adapter: a website chat widget that lets an
anonymous visitor on the business's site open a conversation, send messages, and receive live
replies from agents in the inbox. This is the first true channel in the `ChannelAdapter`
abstraction (see [ADR-0012](ADR-0012-manual-channel-and-pagination.md) for the manual-channel
reference) and it must satisfy the channel-integration and multi-tenancy invariants in AGENTS.md:
tenant isolation on every request, verified-origin session creation, idempotent ingestion,
privacy-safe logging, and AI-safety boundaries (the widget is consumer-facing).

Key constraints that shape the design:

- The widget runs on **arbitrary customer-controlled origins** (any site that embeds it), yet it
  is served from the product API. Origin/document concerns differ sharply from the agent Angular
  app, which is same-origin and locked down.
- A visitor has **no login**, so there is no password/refresh-token flow. Identity is an anonymous
  per-browser `visitorId`; authorization to a specific conversation comes from a server-issued,
  short-lived session token.
- Real-time delivery (PRD §43 mandates SignalR) must work cross-origin from an arbitrary page.

## Decision

### Session token
- The widget uses the **same signing key and issuer as agent JWTs** (one issuer, one key), but a
  **distinct audience** (`WidgetToken.Audience = "omnichannel-widget"`, config `WidgetToken:*`)
  and a distinct claim set: `tenant_id`, `visitor_id`, `widget_session_id`, `conversation_id`,
  `sub` (visitor id), `jti`.
- This is the one-issuer/one-key model: a **widget token can never call agent APIs** (audience
  mismatch in the agent scheme), and an **agent token can never drive the widget** (it lacks
  visitor/session claims and fails the widget scheme's audience check). So the two token classes
  are cryptographically disjoint by audience while sharing infrastructure.
- Lifetime is short (`SessionLifetimeMinutes`, default 30), matching the manual-channel
  session-lifetime decision.

### Authentication schemes
- Two JWT bearer schemes are registered: the default `Bearer` (agent) and a named `"Widget"`
  scheme. The `"Widget"` scheme validates `aud = "omnichannel-widget"`, the agent scheme requires
  the agent audience; both share issuer + signing key.
- Widget policies (`WidgetHub`, `WidgetSession`) call `AddAuthenticationSchemes("Widget")` before
  `RequireAuthenticatedUser()`, so authorization for widget endpoints and the hub runs **only** the
  `"Widget"` scheme — not the agent scheme.
- The default agent scheme **skips widget paths** (`/widget` and `/hubs/widget`) via
  `OnMessageReceived` → `NoResult()`, so it never emits misleading audience-validation errors and
  never attempts to read a widget token. This keeps agent/worldive logs clean while the "Widget"
  scheme remains authoritative on widget paths.

### Origin validation and CORS
- Session **creation** (`POST /widget/{slug}/session`) is only accepted when the embedding page's
  `Origin` header is in the tenant's widget allowlist (stored on `WidgetChannelSettings` and
  managed by the business in the inbox). No other widget call re-checks the origin — once a session
  token is issued it is scoped to the conversation via the token claims.
- A dedicated `"WidgetEmbed"` CORS policy applies to the widget REST group and the `/hubs/widget`
  hub. It **reflects the request origin and allows credentials** (`SetIsOriginAllowed(_ => true)`,
  `AllowCredentials()`) rather than using the wildcard `*`. This is required because SignalR's
  negotiate fetch uses `credentials: 'include'`, and Chromium rejects `Access-Control-Allow-Origin:
  *` on credentialed requests. **Safe here because the widget never authenticates via cookies** —
  it uses a bearer token in the `Authorization` header / `?access_token=` query string, so
  reflecting the origin and allowing credentialed requests grants no extra privilege. Agent and
  internal APIs remain under the strict `Default` allowlist.
- Hub `WidgetHub` is named-mapped with `.RequireCors("WidgetEmbed")`.

### Realtime
- `WidgetHub` reuses the tenant-SignalR model from
  [ADR-0014](ADR-0014-realtime-architecture.md): a visitor is placed in group
  `conversation:{conversationId}` where `conversationId` is read **from the server-issued token
  claim, never from the client**. A visitor can only ever receive updates for its own conversation.
  Agents receive the same conversation's messages via the agent tenant-group; the two planes never
  overlap.

### Embed privacy / XSS
- All message text (untrusted visitor + agent content) is rendered as **escaped text**
  (`escapeHtml`) in the embed; no `innerHTML` of raw content. The widget DOM is fully escaped.

### CSP
- `SecurityHeadersMiddleware` branches on `/widget`: those paths get `default-src 'self'` (the
  embed + demo must load their own same-origin script/style assets and contain no tenant data —
  logic lives in the `/widget` API), everything else keeps `default-src 'none'; frame-ancestors
  'none'`.

## Alternatives considered

- **Third-party widget (Intercom, Crisp, etc.).** Rejected — the product must own the channel and
  the data, and the PRD calls for self-hosted widget served by the product API.
- **Full OAuth login for visitors.** Rejected — makes onboarding friction heavy and isn't what the
  webchat channel needs; anonymous sessions are the established pattern.

## Consequences

- Tenant isolation holds end-to-end for the consumer channel: tenant filter on every widget DB
  query, token-derived tenancy/conversation (never client input), and a conversation-scoped
  realtime group.
- A visitor token is bound to a single conversation and expires quickly; a malicious site without
  the visitor's token cannot read or post. Cross-tenant retrieval requires forging a widget token,
  which the signing key prevents.
- Reflecting origin + credentials on the widget CORS policy is justified only because widget auth
  is bearer-token based; this is called out explicitly so a future cookie-based widget
  authentication would require re-review before changing the embed model.
- The widget embed `embed.js`/`widget.css`/`signalr.min.js` are served statically from the API's
  `wwwroot/widget` and are the "public" surface; they are deliberately minimal and contain no
  secrets.

**Status/date:** Accepted, 2026-09-04. Associated with Phase 5 (PRD §64).
