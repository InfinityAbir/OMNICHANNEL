# Security

Living document, updated after every phase's mandatory security review (AGENTS.md §Mandatory
security review).

## Phase 6 controls (Channel Adapter Framework)

- **Webhook spoofing.** Every inbound webhook (`POST /webhooks/{channelType}`) runs the adapter's
  own `VerifyWebhookAsync` before any parsing or persistence — an invalid signature short-circuits
  to a 403 with no event processed and nothing persisted (`ChannelWebhookSecurityTests.
  Webhook_SpoofedSignature_IsRejectedAndNeverPersisted`). No adapter is registered in production
  yet, so every real channel type currently 404s regardless — this control activates the moment
  Phase 7 registers WhatsApp's adapter, not later.
- **Replay/idempotency.** `WebhookIngestionService` checks for an existing
  `(ChannelAccountId, ExternalMessageId)` before inserting, and the DB's own unique index (already
  in place since ADR-0012, only exercised for real starting this phase) is the authoritative guard
  against a race between two concurrent deliveries of the same event — caught as a benign
  `DbUpdateException`, same pattern as `RoleSeeder`'s seed race. Verified: a provider redelivering
  the identical event never creates a duplicate message.
- **Cross-tenant/account mapping.** Inbound events resolve their `ChannelAccount` by
  `(ChannelType, ExternalAccountId)` only — a value the *provider* assigns, never client input —
  and that id is unique per channel type across all tenants (DB constraint), so one tenant's
  connected account can never resolve into another tenant's data even though the lookup itself
  runs `IgnoreQueryFilters()` (ADR-0016; the third documented exception to the global tenant
  filter). Verified: `ChannelWebhookSecurityTests.
  Webhook_InboundEvent_NeverReachesAnotherTenantsChannelAccount`.
- **Credential handling.** Provider secrets are encrypted at rest via ASP.NET Core Data Protection
  (`DataProtectionChannelCredentialStore`) and never appear in any API response — `PUT
  .../credentials` returns only `{configured: true}`, `GET` never echoes the secret back
  (`ChannelWebhookEndpointsTests.Credentials_NeverReturnedInApiResponse`). Plaintext exists only
  for the duration of a Set/Get call.
- **SSRF.** Not yet applicable — Phase 6 has no code path that fetches a URL found inside webhook
  payload content (no media handling exists yet). Tracked for whichever channel first adds media
  download (likely Phase 7's WhatsApp media handling), which must fetch through a host/scheme
  allowlist and size/timeout bounds, not a bare `HttpClient.GetAsync` on provider-supplied URLs.
- **Provider response validation / error normalization.** `ChannelSendResult`/
  `ChannelSendErrorKind` force every adapter to classify its own failures rather than letting a raw
  provider exception surface; only `Transient`/`RateLimited` retry, everything else fails fast
  (see ADR-0016).
- **Retry architecture** doesn't create an amplification vector: retries are bounded (3 attempts,
  exponential backoff), scoped to one outbound send, and never triggered by inbound webhook
  processing (which acks fast and relies on the *provider's* retry, not its own).

## Phase 5 controls (Website Chat Channel / Widget)

- **Token classes are audience-disjoint.** The widget uses a short-lived session JWT with a
  distinct audience (`omnichannel-widget`) and claim set (`tenant_id`, `visitor_id`,
  `widget_session_id`, `conversation_id`, `sub`), sharing the agent issuer + signing key. The
  agent scheme rejects a widget token (audience mismatch) and the widget scheme rejects an agent
  token (wrong audience / missing visitor claims) — so a widget token can never call agent APIs
  and an agent token can never drive the widget. See
  [ADR-0015](decisions/ADR-0015-website-chat-widget.md).
- **Origin validation is server-side at session creation.** `POST /widget/{slug}/session` is only
  accepted when the embedding page's `Origin` is in the tenant's widget allowlist
  (`WidgetChannelSettings.AllowedOrigins`). Once a token is issued, all access is scoped to the
  conversation via token claims; the origin isn't re-checked per call because the token itself is
  the boundary.
- **Tenant isolation end-to-end.** The widget token carries `tenant_id`, so the EF global query
  filter and `ScopedTenantContext` scope every widget query. `conversation_id` on the token (never
  client input) bounds both the realtime group (`conversation:{id}`) and, since the Phase 6 review
  below, the REST message endpoints — there is no client-join path and no route-id-only path, so a
  visitor cannot reach another conversation (even one in the same tenant) without forging a token.
  **2026-09-04 finding (fixed before Phase 6 started):** `POST/GET
  /widget/conversations/{conversationId}/messages` originally trusted the route's `conversationId`
  once the widget token proved tenant membership, without also checking the token's own
  `conversation_id` claim — a real cross-visitor BOLA within one tenant (any visitor with a valid
  session could read/write any other visitor's conversation by obtaining its GUID). The realtime
  hub path was never affected (it always scoped by the token claim). Fixed in
  `WidgetEndpoints.TokenConversationMatches`: both endpoints now 404 (not 403, same
  don't-confirm-existence convention as `ConversationSecurityTests`) when the route id doesn't
  match the token's own conversation. Regression test:
  `WidgetEndpointsTests.WidgetSession_CannotReachAnotherVisitorsConversation`.
- **CORS on the widget surface reflects origin + credentials** (`SetIsOriginAllowed`, 
  `AllowCredentials`) because SignalR's negotiate fetch is `credentials: 'include'`. Safe because
  widget auth is bearer-token based and **never cookie-based** — the server never trusts cookies,
  so echoing the origin grants no extra privilege. Agent/internal APIs stay under the strict
  `Default` allowlist. Re-verify before any future cookie-based widget authentication.
- **Mouse/agent scheme isolation.** The default agent bearer scheme short-circuits
  (`OnMessageReceived → NoResult()`) on `/widget` and `/hubs/widget`, so it never attempts to
  validate a widget token and never emits the misleading `IDX10214 Audience validation failed`
  logs; the dedicated `"Widget"` scheme is authoritative on those paths.
- **XSS in the embed.** All message text (untrusted visitor and agent content) is rendered as
  HTML-escaped text (`embed.js` `escapeHtml`); no raw content is ever injected into the widget
  DOM. CSP is relaxed to `default-src 'self'` **only** on `/widget` paths (the embed must load its
  own same-origin assets and contains no tenant data); everywhere else stays
  `default-src 'none'; frame-ancestors 'none'`.
- **No secrets / tenant data in the client bundle.** The embed, demo, and vendored SignalR bundle
  are static and contain no credentials; all logic and data live behind `/widget` API calls.

## Phase 4 controls (Realtime Messaging / SignalR)

- **Connection authentication**: hub connections require a valid JWT (Policy
  `RealtimeHub`: `RequireAuthenticatedUser()` + a `tenant_id` claim, evaluated by
  `InboxHubAuthorizationHandler`). The hub additionally verifies both `tenant_id` and `sub`
  claims in `OnConnectedAsync` and aborts the connection if either is missing — defense in depth,
  so the authorization attribute and the manual check agree.
- **WebSocket token transport**: a WebSocket can't send an `Authorization` header, so the client
  supplies the token as `?access_token=...` via SignalR's `accessTokenFactory`.
  `JwtBearerEvents.OnMessageReceived` reads it **only** when the request path starts with `/hubs` —
  querystring tokens are never accepted on regular REST endpoints, so this doesn't widen the
  REST attack surface (which would otherwise allow tokens to leak into server/access logs).
- **Tenant isolation in groups**: exactly one SignalR group per tenant (`tenant:{tenantId}`);
  group membership is derived from the server-issued token's `tenant_id` claim, never from a
  client-supplied group name. The hub exposes no client-invokable "join arbitrary group" — there
  is no way to subscribe to another tenant's group without a forged token, which the JWT signing
  key already prevents. The notifier only ever targets `tenant:{tenantId}` derived from the
  caller's own tenancy.
- **Event leakage**: push payloads are minimal DTOs (ids + changed fields); no credentials, no
  hidden prompts, no other-tenant data. Delivered only to the caller's tenant group.
- **Reconnection / token expiry**: `withAutomaticReconnect([0,2,5,10,15,30])` reconnects with
  backoff; on expiry the server rejects the request, the connection drops, and the client signals
  disconnected (a later phase will route to login/refresh on top of `onclose`).
- **Duplicate-event handling (reliability)**: client de-duplicates by event id **per event type**
  (a shared id set would have swallowed `message_status` after the matching `new_message`, since
  both carry the same `MessageId`); the composer de-dupes by message id so an agent doesn't see
  their own message twice (its own connection receives the push too). Multi-tab is safe — each tab
  connects independently and de-dupes locally.

## Phase 3 controls (Unified Inbox UI)

- **Route guards**: `/inbox*` requires authentication (`authGuard`); unauthenticated visits
  redirect to `/login`. Backend authorization remains authoritative regardless — the guard is
  UX only, per AGENTS.md's "frontend permissions are for UX, backend is authoritative."
- **XSS**: audited — no component uses `[innerHTML]` or `DomSanitizer.bypassSecurityTrust*`
  anywhere. Message text, notes, and all user content render via plain Angular interpolation,
  which HTML-escapes by default.
- **Token storage**: JWT access/refresh tokens in `localStorage`, a documented trade-off (not an
  oversight) — see [ADR-0013](decisions/ADR-0013-frontend-architecture.md). Depends on the XSS
  audit above holding for every future component that touches user-supplied content.
- **Sensitive data exposure**: no attachment previews exist yet (no attachment feature until
  Phase 5+); nothing renders raw HTML from the server.
- **Process finding, not a product vulnerability**: Phase 1 and Phase 2 were both reported
  complete with "all tests pass," verified only via local `dotnet test` — neither phase's actual
  GitHub Actions CI run was checked. Both had been failing since push (see
  `docs/decisions` commit `5cf8f39` / the dedicated CI-fix commit in this phase's history) due to
  environment differences invisible locally: no `Jwt:SigningKey` outside local `dotnet
  user-secrets`, and CI's Postgres never receiving migrations. Root-caused and fixed as part of
  this phase (`appsettings.Testing.json`, `TestWebApplicationFactory`, an explicit CI migration
  step); user is now checking CI status after every push as a standing rule. No production
  security impact — this was a CI/test-infrastructure gap, not a runtime vulnerability — but
  it's exactly the kind of "looked done, wasn't verified" gap AGENTS.md's phase-gate process
  exists to catch, so it's recorded here rather than quietly folded in.

## Phase 2 controls (Conversations + Contacts)

- **Object-level authorization (IDOR/BOLA)**: every conversation/contact/message lookup goes
  through the tenant-filtered `IAppDbContext`, so a foreign tenant's object id resolves to
  nothing. Cross-tenant access returns `404`, never `403` — doesn't confirm the object exists to
  a tenant that can't see it. Regression-tested
  (`ConversationSecurityTests.ModifiedObjectId_CannotReachAnotherTenantsConversation`), closing
  the PRD §60 attack test Phase 1 explicitly deferred.
- **Permission enforcement in practice**: every Phase 2 endpoint is gated by a real
  `PermissionKeys` policy (not just wired-and-unused as in Phase 1). Regression-tested that the
  Agent role — which has `conversations.*` but not `audit.read` — is rejected from `/api/v1/audit`
  (`ConversationSecurityTests.AgentRole_CannotReachAuditLogEndpoint`), closing Phase 1's other
  deferred attack test.
- **Audit integrity**: every mutating action (create/assign/status/priority/tag/note/message)
  writes an `AuditLog` row in the *same transaction* as the change (one `SaveChangesAsync` call)
  — an audit entry can't be silently lost if the write itself succeeds. Metadata stays minimal
  (e.g. `{ "tag": "billing" }`, `{ "direction": "Outbound" }`) — never full message text or
  secrets.
- **Message content handling**: `Message.Text` capped at 8000 chars at the DB layer; internal
  notes are staff-only and never exposed on any customer-facing surface (none exists yet, but the
  entity/endpoint design keeps it that way going in).

## Phase 1 controls (Identity + Multi-Tenancy)

- **Password policy**: min length 10, requires digit/upper/lower/non-alphanumeric (Identity
  options in `Infrastructure/DependencyInjection.cs`).
- **Brute-force protection**: account lockout after 5 failed attempts (15 min), plus a per-IP
  rate limiter on the whole `/api/v1/auth/*` group (30 req/min — see `Program.cs` for why this
  is the secondary, not primary, defense).
- **Token handling**: JWT access tokens (HMAC-SHA256, 15 min default lifetime), opaque refresh
  tokens stored only as a SHA-256 hash with rotation-on-use. See
  [ADR-0007](decisions/ADR-0007-identity-and-auth-model.md).
- **Enumeration resistance**: login with an unknown email and login with a wrong password return
  the identical 401 shape; password-reset requests always return the same response regardless of
  whether the email is registered. Regression-tested (`AuthEndpointsTests`,
  `AuthSecurityTests.RevokedRefreshToken_...`).
- **Tenant isolation**: EF Core global query filter on every `ITenantOwned` entity, tenant
  resolved server-side from JWT claims only (`ITenantContext`/`ScopedTenantContext`) — never
  trusted from client input. Regression-tested at the data-access layer
  (`TenantIsolationTests.Memberships_ForTenantA_NeverIncludeTenantBsRows`). One deliberate,
  documented exception: login/refresh's tenant-discovery query uses `IgnoreQueryFilters()`
  because it runs before a tenant context exists — see ADR-0007.
- **Two real bugs found and fixed by this phase's own testing** (not merely "found the design
  was fine on paper"): the query-filter exception above (login/refresh returned 401 for *every*
  successful login before the fix), and `JwtBearerOptions.MapInboundClaims` needing to be
  disabled (otherwise `sub` gets silently remapped, breaking `/users/me` for every authenticated
  request). Both now have regression tests.
- **Secrets**: JWT signing key and SMTP credentials live only in local `dotnet user-secrets`
  (dev) / environment variables (elsewhere) — never in `appsettings.json`, `.env.example`, or
  committed anywhere. See [ADR-0011](decisions/ADR-0011-email-delivery.md) for the email
  provider decision and its own caveat (a credential was pasted directly in chat this session;
  flagged to the user, worth rotating independent of how the app stores it).
- **Email verification**: off by default (`Identity:RequireConfirmedEmail=false`, per PRD §13's
  "if enabled" wording) — the confirmation email + endpoint work regardless, so turning it on
  later is a config change, not new code.

## Phase 0 baseline controls (still in effect)

- **Secrets**: nothing committed. `.env.example` has placeholders only, never real credentials.
  `appsettings.Development.json` contains a well-known *dev-only* Postgres password matching
  `docker-compose.yml`'s default — not a real secret, never reused outside local dev.
- **Error handling**: unhandled exceptions become RFC 7807 ProblemDetails; internal exception
  details, stack traces, and type names never reach the client. See
  `src/Omnichannel.Api/Middleware/ProblemDetailsExceptionHandler.cs`.
- **Transport/headers**: HTTPS redirection; HSTS outside Development; `X-Content-Type-Options:
  nosniff`; `X-Frame-Options: DENY`; locked-down CSP (`default-src 'none'`); no `Server` header.
  See `src/Omnichannel.Api/Middleware/SecurityHeadersMiddleware.cs`, regression-tested in
  `Omnichannel.SecurityTests/SecurityHeadersTests.cs`.
- **CORS**: deny-by-default; explicit origin allowlist from `Cors:AllowedOrigins` config, empty
  in `appsettings.json` (production must set it explicitly), `http://localhost:4200` only in
  `appsettings.Development.json`.
- **Dependencies**: `dotnet list package --vulnerable` and `npm audit` run in CI on every push
  (`.github/workflows/ci.yml`).
- **Docker exposure**: Postgres bound to `127.0.0.1:5432` only, not `0.0.0.0` — not reachable
  from outside the host.
- **Repo is public** (`github.com/InfinityAbir/OMNICHANNEL`) — extra discipline required: never
  commit `.env`, credentials, tokens, or internal-only URLs. `.gitignore` excludes `.env*`
  (except `.env.example`), `bin/`, `obj/`, `node_modules/`.

## Logging policy

Structured logging (Serilog) with correlation IDs. Do not log message content, tokens,
credentials, or full request/response bodies containing customer data — this rule is set now so
it's inherited by every later phase instead of retrofitted once real customer data exists.

## Not yet applicable (tracked for their phase)

- Provider-specific webhook signature schemes (WhatsApp/Instagram/Messenger's actual HMAC
  formats), OAuth/credential lifecycle, media download SSRF hardening — Phase 7+, once a real
  adapter exists to implement them. The generic mechanism (verify-before-parse, idempotency,
  encrypted credential storage) is in place as of Phase 6 — see "Phase 6 controls" above.
- AI-specific threats (prompt injection, cross-tenant retrieval leakage, output validation) —
  Phase 10+; see [ai.md](ai.md).
- File upload/attachment security — Phase 5+ (website chat is the first channel with
  attachments).

## Security review log

**Phase 6** (2026-09-04) — scope: channel adapter framework. Reviewed against PRD §65's explicit
focus list: webhook spoofing (adapter-verified before any processing, rejected deliveries
persist nothing), replay attacks (idempotent on `(ChannelAccountId, ExternalMessageId)`, DB-level
guarantee, not just an application check), credential handling (Data Protection encryption at
rest, never returned by any API response), external payload validation (malformed events in a
batch are skipped individually, never fatal to the whole delivery), SSRF risks (not yet
applicable — no media-fetching code exists yet, tracked for Phase 7), provider response validation
(adapters must classify every failure into a fixed error-kind enum, no raw exception leaks past
the adapter boundary). No high/critical findings. Verified end-to-end against a test-only fake
adapter (no real provider exists until Phase 7): spoofed-signature rejection, cross-tenant/account
isolation, credential non-disclosure, retry/no-retry classification — full backend suite green
(74/74) and the real GitHub Actions run checked, not just local output.

**Pre-Phase-6 verification** (2026-09-04) — before starting Phase 6, independently re-verified
Phases 4 and 5 (implemented in a separate session): full local build/test/E2E rerun, live GitHub
Actions run re-checked green, and a fresh read of the widget's security-sensitive code rather than
trusting the phase report's claims at face value. Found and fixed one real medium-severity gap:
a cross-visitor BOLA on the widget message REST endpoints (see the Phase 5 controls section above,
"2026-09-04 finding") — the SignalR realtime path was already correctly scoped by the token's
`conversation_id` claim, but the REST endpoints were not. No other findings; both phases otherwise
hold up under review.

**Phase 5** (2026-09-04) — scope: website chat channel. Reviewed the consumer-facing surface: the
widget session token (audience-disjoint from agent tokens, short-lived, conversation-scoped),
server-side origin allowlist at session creation, tenant isolation on every widget query and on the
conversation-scoped realtime group, agent scheme short-circuit so widget tokens are only evaluated
by the "Widget" scheme (and never mis-logged), XSS defense in the embed (all text escaped), CSP
relaxation restricted to `/widget`, and the widget CORS policy (reflect-origin + credentials, safe
because widget auth never trusts cookies). No high/critical application findings. Verified
end-to-end: cross-origin visitor → agent → visitor live reply through `WidgetHub`, plus the full
backend + E2E suites green.

**Phase 4** (2026-09-04) — scope: SignalR hub, tenant-scoped groups, WebSocket auth, frontend
realtime ingestion. No high/critical application findings. Addressed the realtime-specific threat
model per PRD §63: connection authentication (Policy `RealtimeHub` + in-hub claim check), tenant
isolation in groups (server-derived group membership, no client-join, no cross-tenant group
subscribe path), query-string token accepted only on `/hubs` paths, minimal-DTO event leakage, and
a reliability/security fix where an over-broad de-dupe id set would have silently dropped
`message_status` events. Also fixed (in this phase's later commits): a benign role-seed race in
parallel test hosts that surfaced in CI — `RoleSeeder` now clears the EF change tracker on the
caught `DbUpdateException` so failed inserts aren't re-sent on the next save (no runtime
vulnerability; a test-infrastructure robustness fix). Verified end-to-end with 2 new realtime
Playwright tests plus existing suites, all green in CI.

**Phase 3** (2026-09-03) — scope: Angular auth screens, inbox UI, route guards, CI process
finding. No new application-level findings — XSS audit clean (no `[innerHTML]`/
`bypassSecurityTrust*` anywhere), route guards verified (unauthenticated → `/login` redirect,
regression-tested via Playwright), backend authorization re-confirmed authoritative. One
process-level finding fixed: CI's backend job had been failing since Phase 1's push, undetected
because only local test runs were checked — see the Phase 3 controls section above and this
phase's dedicated CI-fix commit. No high/critical application findings.

**Phase 2** (2026-09-03) — scope: conversations, contacts, messages, tags, notes, audit. No new
findings beyond what's documented above (which were the two attack tests explicitly deferred
from Phase 1, both now closed). Attack tests run this phase: modified object ID → another
tenant's conversation (pass, 404), agent role → audit-log endpoint (pass, 403). No high/critical
findings.

**Phase 1** (2026-09-03) — scope: auth, tenancy, permission plumbing. Findings: 2 (see above,
both fixed with regression tests before phase close). Attack tests run: unauthenticated →
protected endpoint (pass), expired token → protected endpoint (pass), revoked refresh token →
refresh (pass), tampered JWT claim → rejected by signature check (pass). Deferred to Phase 2:
agent → admin endpoint, modified object ID → another tenant's object (no object-with-id endpoint
exists yet to attack meaningfully).

**Phase 0** (2026-09-03) — scope: config, Docker, error handling, headers, CORS, dependency
scanning. No high/critical findings.
