# Security

Living document, updated after every phase's mandatory security review (AGENTS.md §Mandatory
security review).

## Phase 16 (Dynamic Tenant Configuration — AI Provider + SMTP)

Not a PRD-numbered phase — a post-launch feature the user asked for directly: per-tenant AI
provider and SMTP configuration, both encrypted at rest, both optional with a working platform
default. Full design reasoning in `docs/decisions/ADR-0027-dynamic-tenant-configuration.md`.
Security-relevant points from that review:

- **Secrets never held in plaintext outside the request that needs them.** `TenantSecret`
  generalizes `ChannelCredential`'s (ADR-0016) Data Protection API encryption-at-rest to any
  per-tenant secret keyed by a `(TenantId, Purpose)` pair. `GET` endpoints for both AI provider
  and email settings return only `hasApiKey`/`hasPassword` booleans — the encrypted value, and
  any decrypted plaintext, never appears in a response body, a log line, or a test fixture (the
  test suite uses obviously-fake marker strings like `"fake-test-key"`/`"fake-app-password"`,
  never anything resembling a real credential).
- **Regression test added**: `TenantSecretsSecurityTests.TenantSecrets_StoredEncrypted_
  NeverPlaintextInDatabase` asserts a known plaintext marker never appears verbatim in the
  `tenant_secrets.encrypted_value` column after a save — not just "it's encrypted in principle."
- **Tenant isolation re-verified for the new tables**, same pattern as every prior phase's own
  cross-tenant test: tenant B's `GET` on AI provider settings / email settings after tenant A
  configures theirs returns no trace of tenant A's base URL, model, or `hasApiKey`/`hasPassword`
  state (`TenantSecretsSecurityTests`). `AiProviderResolver`, `SmtpEmailSender.ResolveConfigAsync`,
  and `DataProtectionTenantSecretStore` all use `IgnoreQueryFilters()` + an explicit `tenantId`
  parameter (the same documented exception pattern as ADR-0016/ADR-0022, required because these
  paths run from unauthenticated contexts — registration, password reset, webhook-triggered
  auto-reply — with no ambient tenant session to filter by).
- **Permission gating verified, not assumed**: `ai.read`/`tenant.read` (which the Agent role has)
  can view configuration state; only `ai.configure`/`tenant.update` (which Agent does not have)
  can write, clear, or test it. `TenantSecretsSecurityTests.AgentRole_CanReadButCannotWrite*`
  exercises this directly — a plain read-permission role gets 200 on `GET`, 403 on `PUT`/`DELETE`/
  `POST .../test`.
- **The "test connection" endpoints are self-directed, not third-party messaging.** The AI test
  sends one minimal completion request to the tenant's own resolved provider (no customer data
  involved). The email test resolves the *calling user's own* email/display name server-side
  (`ITenantContext.UserId` → `UserProfiles`) and sends to that address only — a user can never
  direct a test email at an arbitrary third party through this endpoint.
- **`AnthropicProvider` is unverified against a real Anthropic API** — no key was available in
  this environment. Its parsing logic is exercised by stubbed-response tests
  (`AnthropicProviderTests`) built from Anthropic's public documentation, not a captured real
  response the way `GroqAiProviderTests`/`OpenAiCompatibleProviderTests` are. Flagged in the
  class's own doc comment and in ADR-0027; verify against a real key before depending on this
  provider in production.
- **One incidental real SMTP send** happened during manual browser verification of the
  platform-default fallback path (curl request to a fake, nonexistent recipient address —
  harmless, no real person received anything). Disclosed to the user at the time. No automated
  test exercises the `/email-settings/test` endpoint for this reason
  (`EmailSettingsEndpointsTests`'s own doc comment records why).
- Full backend suite green after this work: 274/274 (84 unit + 53 integration + 36 security +
  101 API), including 53 new test methods added specifically for this feature (domain validation,
  provider-response parsing for both new AI provider types, key-prefix detection heuristics,
  endpoint CRUD, tenant isolation, permission gating).

## Phase 15 (Production Hardening)

Full PRD §74/§75 review — see `docs/phase-reports/phase-15.md` for the complete account,
`docs/disaster-recovery.md` for backup/restore/DR, `docs/privacy.md` for the privacy/retention
review. Summary of what changed and what was verified:

- **Dependency audit**: `dotnet list package --vulnerable --include-transitive` — zero
  vulnerable packages across all 9 projects. `npm audit` (web workspace) — zero vulnerabilities.
  (The e2e workspace's `npm audit` couldn't complete — network-restricted sandbox, not a finding.)
- **Full-depth manual security audit** (OWASP Top 10 + business-logic + C#/Angular-specific,
  using the security-auditor methodology) across every endpoint added in all 15 phases: every
  route is authorization-gated except the deliberately-public ones (auth, provider webhooks,
  widget embed session-open), each of which is protected by rate limiting and/or its own
  signature/origin verification instead. Webhook HMAC verification (WhatsApp/Instagram/
  Messenger) uses `CryptographicOperations.FixedTimeEquals` — constant-time, not vulnerable to
  timing attacks. No secrets logged or echoed in any response (channel credentials return only a
  `configured: bool`, never the value). CORS is a strict allowlist plus one narrowly-justified
  wildcard for the public widget embed (documented in `Program.cs`, not a blanket `*`).
- **Found and fixed**: two Phase 12/13 endpoints (`PUT /api/v1/ai/auto-reply-settings`,
  `PUT /api/v1/tenant/business-hours`) accepted an unbounded business-hours/holidays payload —
  the backing columns are bounded (`character varying(4000)`), so an oversized request surfaced
  as an unhandled Postgres error (500) instead of a clean 400. Added explicit size guards
  (≤7 days, ≤20 windows/day, ≤366 holidays) with regression tests.
- **Rate limiting extended**: every request now passes a global per-authenticated-user (falling
  back to per-IP) limiter (600/min) in addition to the existing tighter auth/widget/webhook
  policies — previously, authenticated endpoints like `conversations`, `ai-suggestions`, and the
  Phase 12-14 admin surfaces had no bound at all.
- **Backup/restore actually executed**, not just documented: `pg_dump` the dev database → restore
  into a fresh database → row counts on `conversations`/`messages`/`tenants`/`roles` matched
  exactly (972/936/2519/4). See `docs/disaster-recovery.md`.
- **Provider/AI outage handling**: already real from Phase 6-12 (Polly retry for channel sends,
  `AiProviderException` → "ask a human" fallback everywhere) — re-verified, not re-built.
- **Frontend**: audited `web/src` for XSS (no `innerHTML`/`bypassSecurityTrust*` usage anywhere),
  token storage (the existing documented `localStorage` trade-off, ADR-0013, re-confirmed still
  accurate), and error handling — found several mutation actions (assign, tag, status/priority
  change, create-conversation) that subscribed with no error handler at all, so a failed request
  did nothing visible rather than informing the user. Added a global toast notification system
  (`ToastService`/`ToastHostComponent`) that extracts the backend's own ProblemDetails
  `title`/`detail` for a human-readable message — verified live in the browser (a forced 404 on
  "assign" now surfaces "Conversation not found." as a dismissible toast, not silence or a raw
  error).
- **Not applicable, confirmed not built rather than assumed**: queue failure test (no message
  queue/broker exists — modular monolith), MFA (Identity is MFA-ready by architecture; enabling
  it is a product decision, not a Phase 15 gap).
- **Known, tracked gaps** (not silently absent): no automated data-retention/deletion tooling
  (`docs/privacy.md`), no automated backup job in-repo (deferred to the hosting provider's managed
  backup once one is chosen, `docs/disaster-recovery.md`), no JWT key-rotation overlap mechanism.

## Phase 14 controls (Analytics)

- **Must never aggregate across tenants**: `AnalyticsService` runs entirely through
  `IAppDbContext`'s ordinary `DbSet` properties under the ambient `ITenantContext` — no
  `IgnoreQueryFilters()` anywhere in it, unlike Phase 12/13's services, since analytics has no
  unauthenticated call site and the standard EF global tenant filter (ADR-0005) is sufficient and
  correct here. Every grouped/aggregate query (status counts, response time, channel/agent
  breakdowns) is therefore automatically scoped to the caller's own tenant by construction, not by
  an explicit predicate that could be forgotten. Verified with a query deliberately shaped to
  reveal cross-tenant leakage if the filter were missing (Tenant A has 5 conversations, Tenant B
  has 1 — Tenant B's summary must show exactly 1, not 6):
  `AnalyticsSecurityTests.Summary_NeverIncludesAnotherTenantsConversations`.
- **Authorization**: gated by the existing `analytics.read` permission key (PRD §12, defined
  since Phase 1, unused until now) — Agent and Viewer roles both have it, Owner/Admin implicitly
  via the full permission set.

## Phase 13 controls (Business Rules + Automation)

- **Rules cannot execute arbitrary code**: `AutomationRule` is a closed set of trigger types
  (keyword substring match only, no regex/scripting) and actions (apply a named tag, set a fixed
  `ConversationPriority` enum value, escalate) — entirely data, never code, matching PRD §72's
  explicit requirement.
- **Cannot access other tenants**: `AutomationRuleService.EvaluateAsync` takes an explicit
  `tenantId` and queries via `IgnoreQueryFilters()` + an explicit `TenantId ==` predicate — the
  same documented exception as `AiAutoReplyService` (ADR-0016/0022), required for the same reason
  (invoked from an unauthenticated webhook context). Verified:
  `AutomationSecurityTests.AutomationRules_TenantACannotSeeTenantBsRules`,
  `SavedReplies_TenantACannotSeeTenantBsReplies`, `Notifications_TenantACannotSeeTenantBsNotifications`.
- **Cannot bypass authorization**: rule/business-hours configuration requires `tenant.update`
  (Owner/Admin only — an existing PRD §12 permission key, none invented); saved replies require
  `conversations.reply` (every Agent+ role) — a deliberately different bar, since saved replies are
  an agent tool, not tenant-wide configuration. Verified:
  `AutomationSecurityTests.AgentRole_CannotManageAutomationRulesOrBusinessHours_ButCanManageSavedReplies`.
- **Cannot send unlimited messages**: automation rules never send a message themselves — their
  only actions are tagging, prioritizing, and escalating a conversation. Sending remains exclusively
  the agent-approval path (Suggest mode) or the already-limited `AiAutoReplyService` (Phase 12,
  its own daily cap); this phase adds no new send path at all.
- **Cannot disable safety controls**: no automation-rule action can change `ConversationAiMode`,
  `AiAutoReplySettings`, or any other Phase 12 safety gate — the action set (tag/priority/escalate)
  has no path to any AI configuration.

## Phase 12 controls (AI Auto-Reply)

- **Unauthorized AI actions**: enabling auto-reply requires two independent, explicit opt-ins —
  the tenant-wide `AiAutoReplySettings.Enabled` switch (gated by the `ai.configure` permission,
  Owner/Admin only) and the individual conversation's own `ConversationAiMode` (also
  `ai.configure`). Both default to off/Disabled; a fresh tenant can never auto-reply until a
  business owner deliberately turns both on. Verified:
  `AiAutoReplySecurityTests.AgentRole_CannotConfigureAutoReplySettings`.
- **Prompt injection / hallucination**: no new prompt-construction path — auto-reply reuses
  `GroqAiProvider`'s existing structural defenses (history as separate role-tagged messages,
  knowledge snippets labeled untrusted) unchanged from Phase 10/11, plus a new hard behavioral
  gate: the model's own `requiresHuman` self-assessment and a configurable confidence threshold
  are enforced in code, not just requested in the prompt — a low-confidence or self-flagged
  response is never auto-sent regardless of what the model claims. Verified live against the real
  Groq API for both branches (a refund request correctly sets `requiresHuman: true` with a reason;
  a known-FAQ question correctly sets it `false`) — see `docs/phase-reports/phase-12.md`.
- **Data leakage**: identical tenant-scoped, notes-excluded context assembly as Suggest mode
  (Phase 10) — no new data enters the AI's context in auto-reply mode.
- **Infinite reply loops**: `AiAutoReplyService.EvaluateAsync` is only ever invoked from a genuine
  inbound *customer* message — never from an agent's or the AI's own outbound send. The AI's own
  auto-sent message is `MessageDirection.Outbound`/`MessageSenderType.Ai`, which structurally
  cannot itself become a future inbound-customer trigger.
- **Duplicate replies**: webhook idempotency (the existing `UNIQUE(ChannelAccountId,
  ExternalMessageId)` constraint, PRD §17) rejects a redelivered provider event before
  `AiAutoReplyService` is ever invoked for it — a retried webhook can't cause two auto-replies to
  the same inbound message.
- **Human takeover race conditions**: the AI provider call is a real network round-trip; right
  before sending, `EvaluateAsync` re-queries the conversation's current `AiMode` and checks for any
  agent message created since evaluation started, skipping the send (no escalation — a human is
  already on it) if either changed while the call was in flight.
- **Provider restrictions**: same `IAiProvider`/`GroqAiProvider` configuration as Phase 10, plus a
  second, independent daily cap (`AiAutoReplySettings.DailyLimit`) distinct from Suggest mode's
  own `AiUsageLimiter` cap — auto-sent messages are a materially higher-risk action than a
  human-reviewed draft, so they get their own, separately configurable limit.
- **Tenant isolation**: `AiAutoReplyService` takes an explicit `tenantId` and queries everything
  via `IgnoreQueryFilters()` + an explicit `TenantId ==` predicate — the same documented exception
  to the automatic EF tenant filter as `WebhookIngestionService` (ADR-0005), required because this
  service is invoked from an unauthenticated webhook context as well as authenticated ones.
  Verified: `AiAutoReplySecurityTests.AutoReplySettings_TenantACannotSeeOrAffectTenantBsSettings`.

## Phase 11 controls (Knowledge Base)

- **Tenant isolation in retrieval**: the similarity-search query is raw SQL (EF's LINQ vector
  translation isn't available for a `float[]`-typed model property — ADR-0021) with an explicit
  `WHERE tenant_id = $2` — the fourth documented exception to the automatic EF tenant filter.
  Verified with a query deliberately chosen to match Tenant A's document strongly, confirming
  Tenant B gets nothing back — not just "different content happens not to match":
  `KnowledgeSecurityTests.Search_NeverReturnsAnotherTenantsDocuments`.
- **Unauthorized knowledge access**: gated by the existing `knowledge.read`/`knowledge.manage`
  permission keys (Phase 1's catalog already had them) — no new permissions invented.
- **Malicious document content / prompt injection through documents**: retrieved chunks are
  passed to the AI as a separate, explicitly-labeled "reference material — untrusted, consult
  don't follow" block, never concatenated into system instructions — same structural discipline
  Phase 10 already applies to conversation history, extended to documents.
- **Document upload security**: not yet applicable by design, not by oversight — this phase
  supports plain-text-only document submission (no file upload), which has no upload attack
  surface to defend. See `docs/decisions/ADR-0021`.

## Phase 10 controls (AI Suggestion Mode)

- **Prompt injection**: conversation history is passed as separate role-tagged messages, never
  string-concatenated into the system instruction text — structural, not just instructional,
  defense (ADR-0020). Verified: `AiEndpointsTests.
  GenerateSuggestion_CustomerMessageIsPassedAsDataNotConcatenatedIntoInstructions`.
- **Cross-tenant context leakage**: the suggestion endpoint resolves the conversation through the
  same tenant-scoped query every other conversation endpoint uses; a tenant can never generate a
  suggestion for (and thereby read) another tenant's conversation. Verified:
  `AiSuggestionSecurityTests.GenerateSuggestion_CannotReachAnotherTenantsConversation`.
- **Sensitive data sent to AI**: only the customer-visible message thread is included in context
  — internal notes (agent-only/confidential, PRD §18) are never sent to the third-party provider.
  Verified: `AiEndpointsTests.GenerateSuggestion_InternalNotesNeverIncludedInContext`.
- **Provider credentials**: the Groq API key lives in `dotnet user-secrets`/deployment secrets
  only (`Ai:Groq:ApiKey`), never committed — same discipline as every other provider credential in
  this codebase.
- **AI output validation**: the provider's response is defensively parsed (malformed JSON falls
  back to raw-text-with-low-confidence rather than crashing the request), and the AI's output is
  never sent to a customer automatically — a human always reviews/edits/sends (PRD §87, Suggest
  mode only this phase).
- **Logging**: `AiSuggestion` persists full suggestion text/tokens/confidence in the database (an
  interaction log, not a structured application log) — the "don't log message content" policy
  below governs Serilog output, not this table, consistent with how every other message/note is
  already persisted in full.

## Phase 9 controls (Facebook Messenger Integration)

- **Repeat of the external-integration security checklist** (PRD §68): webhook signature
  verification (same HMAC-SHA256 + constant-time comparison as WhatsApp/Instagram, re-confirmed
  for Messenger's own webhook subscription — `MessengerWebhookSecurityTests.
  Webhook_ForgedSignature_IsRejectedAndNeverPersisted`), tenant/account mapping (`entry[].id` →
  `ChannelAccount.ExternalAccountId`, re-verified: `Webhook_GenuineSignature_
  RoutesOnlyToConnectedTenant`), credential handling (`IChannelCredentialStore`, unchanged),
  unauthorized outbound (always scoped through the conversation's own tenant/channel account).
- **Access token exposure**: Messenger's Send API passes the access token in the request **URL**
  (`?access_token=...`), not a header — verified this doesn't leak it anywhere logs would capture
  (the logging policy below already forbids logging full request/response bodies or tokens;
  `MessengerChannelAdapterTests.SendMessageAsync_UsesQueryStringAccessTokenNotBearerHeader` proves
  the mechanism works, and no code path logs the constructed URL).

## Phase 8 controls (Instagram Integration)

- **Incorrect account mapping / cross-tenant channel access** (PRD §67's explicit "also test"
  items): `entry[].id` (the receiving IG account) resolves through the same
  `(ChannelType, ExternalAccountId)` mechanism as every channel (ADR-0016), re-verified against
  Instagram's own real adapter: `InstagramWebhookSecurityTests.
  Webhook_GenuineSignature_RoutesOnlyToConnectedTenant` proves a webhook for Tenant A's connected
  account never reaches Tenant B, even when both tenants have connected different Instagram
  accounts of the same channel type.
- **Unauthorized outbound messages**: sending is always scoped through the conversation's own
  `ChannelAccount`/tenant, same as WhatsApp — no code path lets one tenant's send use another
  tenant's stored credential or reach another tenant's connected account.
- **Webhook signature verification / forgery**: same HMAC-SHA256 + constant-time comparison as
  WhatsApp (ADR-0017's mechanism, confirmed to apply to Instagram too by this phase's own
  research, not assumed) — `InstagramWebhookSecurityTests.
  Webhook_ForgedSignature_IsRejectedAndNeverPersisted`.
- **Credential handling**: reuses `IChannelCredentialStore` unchanged — no Instagram-specific
  exception to Data Protection encryption at rest.

## Phase 7 controls (WhatsApp Integration)

- **Webhook signature verification** (PRD §66 focus): `X-Hub-Signature-256` HMAC-SHA256 over the
  raw body using the platform's App Secret, compared with `CryptographicOperations.FixedTimeEquals`
  (constant-time — not `==`, to avoid a timing side channel on the comparison itself). The
  verify-token comparison for the GET handshake uses the same constant-time helper. Verified:
  `WhatsAppChannelAdapterTests` (tampered body, missing header) and end-to-end
  `WhatsAppWebhookSecurityTests.Webhook_ForgedSignature_IsRejectedAndNeverPersisted` against the
  real production adapter, not just a fake.
- **Credential encryption / token lifecycle**: the WABA access token is stored via the same
  `IChannelCredentialStore` (Data Protection encryption) as every other channel (ADR-0016) — no
  WhatsApp-specific exception. Token expiry (`error code 190`) surfaces as `AuthFailed`, fails
  fast, never retried — an expired token retried automatically would just fail the same way
  repeatedly while masking the real problem from whoever needs to rotate it.
- **Replay protection**: inherited unchanged from the generic pipeline's idempotency guarantee
  (ADR-0016) — WhatsApp's own `id`/`wamid...` becomes `ExternalMessageId`, so Meta's documented
  retry behavior (same event redelivered for up to 36 hours) can never create a duplicate message.
- **Tenant/account mapping**: `phone_number_id` is the provider-assigned external account id,
  resolved the same way every channel resolves one (ADR-0016) — verified specifically against the
  real adapter in `WhatsAppWebhookSecurityTests.Webhook_GenuineSignature_RoutesOnlyToConnectedTenant`,
  not just the generic fake-adapter test from Phase 6.
- **Outbound authorization**: sending is always scoped to the conversation's own
  `ChannelAccount`/tenant (`ConversationService.AddMessageAsync` → `ChannelSendService`) — there is
  no code path that lets one tenant's outbound send use another tenant's stored credential.

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

- Media download SSRF hardening, Embedded Signup / self-service OAuth connection — deferred by
  ADR-0017/0018/0019 across every Meta channel so far; none has media-fetching code, and
  connection is manual entry everywhere.
- Watermark-range read-receipt support (Messenger's `read` event has no per-message id — ADR-0019)
  — would need a deliberate extension to the status-update model, not yet built.
- File upload/attachment security — no channel has attachment handling yet (tracked per-channel:
  ADR-0017/0018/0019 for messaging channels, original website-chat scope for that channel);
  knowledge documents are also plain-text-only for the same reason (ADR-0021).
- Neural embedding provider credentials/security — not applicable yet, since no neural embedding
  provider is registered (ADR-0021's lexical fallback needs no API key at all).

## Security review log

**Phase 11** (2026-09-04) — scope: Knowledge Base (RAG). Reviewed against PRD §70's explicit
focus: tenant isolation in retrieval (re-verified via raw-SQL query with an explicit tenant
filter — the fourth documented exception to the automatic EF filter, ADR-0021), malicious
document content / prompt injection through documents (retrieved chunks passed as labeled
untrusted reference material, never concatenated into instructions), unauthorized knowledge
access (existing permission keys, no new surface), document upload security (not applicable —
plain-text-only submission, no upload path exists). No high/critical findings. 151/151 backend
tests green — CI checked. End-to-end retrieval verified against the real Groq API: a knowledge
document's exact figures (not invented ones) appeared correctly in a live AI suggestion (see
`docs/phase-reports/phase-11.md`).

**Phase 10** (2026-09-04) — scope: AI Suggestion Mode, first AI feature. Reviewed against
AGENTS.md's AI safety focus: prompt injection (structural defense, not just instructional),
cross-tenant context leakage (re-verified against the real endpoint, not assumed from the generic
tenant-isolation pattern), sensitive data sent to AI (internal notes explicitly excluded from
context), provider credentials (secrets-only, never committed), AI output validation (defensive
parsing, human-approval-only mode). No high/critical findings. 136/136 backend tests green (8 new
AI-specific tests: successful suggestion + interaction log, unknown conversation, provider
failure fallback, internal-notes exclusion, prompt-injection structural check, non-Latin-script
round-trip integrity, daily limit enforcement, cross-tenant isolation) — CI checked. Language/
script matching (Bangla, Banglish) verified against the real Groq API with real text, not assumed
from documentation (see `docs/phase-reports/phase-10.md` for the actual exchanges).

**Phase 9** (2026-09-04) — scope: Facebook Messenger integration, third real `IChannelAdapter`.
Repeated the external-integration security checklist per PRD §68's explicit instruction rather
than assuming Phase 7/8's coverage carried over automatically — webhook signature verification,
credential handling, tenant/account mapping, and unauthorized-outbound protection all re-verified
against Messenger's own real adapter. Also checked that the query-string access-token mechanism
(genuinely different from WhatsApp/Instagram's Bearer header) doesn't introduce a new exposure
path. No high/critical findings. 119/119 backend tests green (8 new isolated adapter-logic
tests, 4 new end-to-end wiring tests, 2 new security tests) — CI checked.

**Phase 8** (2026-09-04) — scope: Instagram Messaging integration, second real `IChannelAdapter`.
Reviewed against PRD §67's explicit focus list plus its "also test" additions: incorrect account
mapping, cross-tenant channel access, unauthorized outbound messages — all re-verified against
Instagram's own real adapter rather than relying on Phase 6/7's generic/WhatsApp coverage.
Webhook signature verification confirmed to use the same mechanism as WhatsApp (this phase's own
research, not assumed). No high/critical findings. 105/105 backend tests green (13 new isolated
adapter-logic tests, 4 new end-to-end wiring tests, 2 new security tests) — CI checked.

**Phase 7** (2026-09-04) — scope: WhatsApp Business Platform integration, the first real
`IChannelAdapter`. Reviewed against PRD §66's explicit focus list: webhook signature verification
(constant-time HMAC comparison, verified against both a hand-computed test signature and the real
production adapter — not just the Phase 6 fake), credential encryption (reuses Phase 6's
Data-Protection-backed store, no exception carved out), token lifecycle (expired token fails fast
as `AuthFailed`, never silently retried), replay protection (inherited from the generic pipeline,
unchanged), tenant/account mapping (re-verified specifically against the real adapter, not only
the generic mechanism), outbound authorization (always scoped through the conversation's own
channel account, no cross-tenant credential-use path). No high/critical findings. 92/92 backend
tests green (13 new isolated adapter-logic tests, 4 new end-to-end wiring tests through the real
HTTP pipeline, 2 new security tests against the real adapter) — CI checked, not just local output.

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
