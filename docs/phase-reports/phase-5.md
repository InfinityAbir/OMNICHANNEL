# Phase Report — Phase 5: Website Chat Channel (Widget)

**Status:** Implementation complete. Awaiting user approval to proceed to Phase 6.
**Date:** 2026-09-04

## Scope / PRD references

PRD §64 (Phase 5): the first complete channel adapter — a website chat widget the business embeds
on its site. Anonymous/customer identity, secure session, inbound/outbound messages, conversation
creation, and realtime communication between a visitor and agents in the inbox. This phase also
exercises the `ChannelAdapter` abstraction from
[ADR-0012](decisions/ADR-0012-manual-channel-and-pagination.md) and re-applies the AGENTS.md
channel-integration, multi-tenancy, privacy, and AI-boundary invariants to a consumer-facing,
cross-origin surface.

## Implemented

### Backend (foundation, commit `0afc1e7`)
- `WidgetChannelSettings` aggregate + migration: per-tenant embed `slug`, enabled flag, and
  server-side **origin allowlist** (`AllowedOriginsJson`).
- Widget session token generator: short-lived JWT (audience `"omnichannel-widget"`, claims
  `tenant_id`/`visitor_id`/`widget_session_id`/`conversation_id`) sharing the agent issuer/key.
- `WidgetEndpoints` (`/widget/...`): session creation (origin-validated), thread load, message
  send — all in a `"Widget"`-authed group with `RequireCors("WidgetEmbed")`.
- `WidgetHub` at `/hubs/widget`: conversation-scoped SignalR group, `[Authorize(Policy =
  "WidgetHub")]` (pins the `"Widget"` scheme), idempotent group add from token claim.
- Inbox-side widget settings/API: read settings, update allowed origins (business-facing).
- `WidgetTokenOptions` / `WidgetReader`/writer infrastructure, contracts.

### This session (embed, realtime delivery, E2E, hardening)
- Self-hosted embed assets served from `wwwroot/widget`: `embed.js` (auto-locates the API base
  from its own `<script src>`, HTML-escapes all message text, optimistic send), `widget.css`,
  `signalr.min.js` (vendored SignalR 10 browser bundle).
- Cross-origin demo site (`customer-demo.html`, `demo.js`, `demo.css`) + a Node static server
  (`e2e/static-server.js`) served on `:5173` so the E2E can exercise true cross-origin origin
  validation and CORS.
- **Realtime delivery fixed and verified**: visitor message → agent inbox live; agent reply →
  visitor live via SignalR (`WidgetHub`), no page refresh.
- **CORS fix**: the `WidgetEmbed` policy now reflects the request origin and allows credentials
  (required because SignalR's negotiate fetch uses `credentials: 'include'`, which Chromium
  rejects against `Access-Control-Allow-Origin: *`). Safe because widget auth is bearer-token
  based, never cookie-based.
- **Agent scheme isolation**: the default agent `Bearer` scheme short-circuits (`NoResult()`) on
  `/widget` and `/hubs/widget`, so it never emits misleading audience-validation errors and widget
  tokens are only ever evaluated by the `"Widget"` scheme.
- **Hub CORS**: `MapHub<WidgetHub>(...).RequireCors("WidgetEmbed")`.
- **Embed robustness**: a visitor who sends a message before the session POST completes now waits
  for the in-flight session instead of silently dropping the message.
- **CSP**: `SecurityHeadersMiddleware` relaxes to `default-src 'self'` only on `/widget` paths.

### Tests
- `e2e/tests/widget.spec.ts` (new): full cross-origin journey — business allows its site origin,
  visitor opens the widget and sends a message, agent sees it live and replies, visitor receives
  the reply live (line 66/84 assertions cover both directions).

## Root-causes found and fixed during the phase

- **Doubled `apiBase` path** in `embed.js` (`/widget/widget/...` → 404s + CORS preflight failures).
- **SignalR event-name mismatch**: `newMessage` → `new_message` (matches
  `InboxHubEventTypes.NewMessage`).
- **Wrong global for SignalR**: `window.omnichannelSignalR` doesn't exist; the vendored bundle
  assigns `window.signalR` (`vm()` fixed).
- **`Class constructor tt cannot be invoked without 'new'`** in `connect()`: constructing via the
  `vm()` indirection failed while direct `window.signalR.HubConnectionBuilder` worked; using the
  direct reference fixed construction.
- **CORS negotiate rejection** for credentialed requests against `AllowAnyOrigin()` (see above).
- **Silent message drop**: `sendMessage` early-returned when `session` was null; now awaits the
  in-flight session (a flake the E2E exposed once debug waits were removed).
- **Misleading auth logs**: `IDX10214 Audience validation failed` emitted by the default agent
  scheme on widget requests; suppressed via the scheme-isolation change (logs stay clean).

## Tests

- **Unit**: 32/32.
- **Integration**: 1/1.
- **API**: 19/19.
- **Security**: 12/12.
- **Frontend**: unchanged this session (`ng lint`/`ng build` unaffected by backend/E2E-only work).
- **E2E (Playwright)**: 5/5 (4 prior + 1 new widget), all green locally.
- **CI**: verified green via `gh run watch` after push (see git history).

## Security Review

Reviewed consumer-channel surface per AGENTS.md. See `docs/security.md` (Phase 5 controls) for
full detail. Highlights:
- Tenant isolation on every widget query/token/realtime group through the token-derived tenancy;
  conversation id is never client-supplied (no BOLA path into another visitor's conversation).
- Origin allowlist enforced server-side at session creation.
- Widget vs agent token classes are audience-disjoint (a widget token can't call agent APIs and
  vice-versa).
- Full XSS defense in the embed (all message text HTML-escaped); CSP `default-src 'self'` only on
  `/widget` paths.
- Widget CORS reflect-origin + credentials is safe because widget auth never trusts cookies (called
  out in ADR-0015 for any future cookie-based change).
- Widget token never accepted on agent endpoints; agent token is meaningless to the widget.
- No secrets in the client bundle; static embed assets contain no tenant data.

No high/critical application findings.

## Performance / Reliability / Accessibility Review

- Cross-origin realtime works with the standard SignalR reconnect/backoff; optimistic UI keeps the
  visitor responsive even if a push is missed.
- Embed is small and loads its CSS/SignalR asynchronously; degrades gracefully (falls back to a
  non-realtime widget) if the vendored SignalR bundle fails to load — a deliberate resilience
  trade-off.
- Accessibility: widget uses semantic buttons/dialog, `aria-live="polite"`, labelled inputs, and
  keyboard-enter submission (Enter sends, Shift+Enter newline).

## Migrations / Configuration Changes

- Migration `20260903184852_AddWidgetChannelSettings` (from the foundation commit; widget settings
  table).
- No new migration this session.
- Config: `WidgetToken:*` (`Audience`, `SessionLifetimeMinutes`) in `appsettings.json`; embed/demo
  served from `wwwroot/widget`; `Cors:AllowedOrigins` unchanged (strict default for agent APIs).

## ADRs / Docs Updated

- New [ADR-0015](decisions/ADR-0015-website-chat-widget.md) (website chat widget architecture).
- `docs/security.md` — Phase 5 widget controls + security review log entry.
- `docs/architecture.md` / `docs/integrations.md` — website-chat channel notes (see commit).

## Known Limitations

- A business-facing widget-origins **config UI** in the Angular inbox is not yet built; origins are
  currently managed via the API/Playwright. The backend endpoint exists and is verified by the E2E.
- Per-conversation (vs tenant) websocket fanout remains future work (unchanged from ADR-0014).
- No attachments on the widget yet (tracked for the appropriate later phase).

## Files/Modules Changed

`src/Omnichannel.Api/wwwroot/widget/{embed.js,widget.css,signalr.min.js,customer-demo.html,demo.js,demo.css}`,
`src/Omnichannel.Api/Program.cs` (CORS policy, scheme isolation, hub CORS),
`src/Omnichannel.Api/Middleware/SecurityHeadersMiddleware.cs` (widget CSP),
`e2e/static-server.js`, `e2e/playwright.config.ts` (cross-origin webServer),
`e2e/tests/widget.spec.ts` (new), plus the Phase 5 backend foundation files from `0afc1e7`
(widget settings aggregate, session generator, endpoints, hub, contracts, migration, infra tests),
`docs/decisions/ADR-0015` (new), `docs/{security,architecture,integrations}.md`.

## Next Phase

Phase 6 — full channel adapter framework + additional channels (per PRD). The widget serves as the
reference "first channel," and the `ChannelAdapter` abstraction it proved can be generalized.

**Requesting approval to proceed to Phase 6.**
