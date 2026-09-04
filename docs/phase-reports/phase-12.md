# Phase Report — Phase 12: AI Auto-Reply

**Status:** Implementation complete. Per explicit user instruction ("only complete phase 12, then
stop"), this session stops here — Phase 13 is not started.
**Date:** 2026-09-04

## Scope / PRD references

PRD §71 (Phase 12): only after Suggest mode (Phase 10) is stable. Business hours, AI eligibility
rules, confidence thresholds, escalation, human takeover, auto-reply limits, AI response
validation, AI action auditing. Default behavior conservative, with an explicit example table
(known FAQ/product info → reply; unknown/refund/high-risk/low-confidence → human). Security
review: unauthorized AI actions, prompt injection, hallucination, data leakage, infinite reply
loops, duplicate replies, human takeover race conditions, provider restrictions.

## Pre-implementation fix (found before starting Phase 12 proper)

Checking Phase 11's CI run (still in progress at hand-off) surfaced two real, back-to-back backend
failures — a duplicate-key violation on one run, a Postgres deadlock on the very next — both in
`RoleSeeder`'s check-then-insert role seeding, racing across concurrent `WebApplicationFactory`
test hosts. The prior fix only caught `DbUpdateException`, but the deadlock arrived wrapped in
`InvalidOperationException` and slipped through. Replaced the check-then-insert with a
session-level Postgres advisory lock (`pg_advisory_lock`) serializing the whole operation —
verified with three consecutive full local test-suite runs (151/151 each), then confirmed green in
real CI (commit `3167dcd`) before starting Phase 12's own work.

## Implemented

- **`Conversation.SetAiMode`** (Domain) — the conversation entity had `AiMode` since an earlier
  phase but no way to change it after creation; needed so an agent can actually turn auto-reply on
  for a specific conversation.
- **`AiAutoReplySettings`** (Domain, new) — one row per tenant (created at registration alongside
  the existing Widget/Manual channel setup), disabled by default, unconfigured business hours by
  default. `IsWithinBusinessHours(nowUtc, tenantTimeZoneId)` evaluates in the tenant's own IANA
  time zone, never server-local time, and fails toward "not open" for every uncertain input
  (unconfigured schedule, unresolvable time zone id, day not scheduled).
- **`IAiProvider`/`AiCompletionResult`** gained `RequiresHuman`/`EscalationReason` — the model's
  own self-assessment of whether an exchange needs a human regardless of confidence.
  `GroqAiProvider`'s system prompt and JSON schema extended accordingly; a malformed/unparseable
  response now defaults to `RequiresHuman = true` (previously just a low-confidence fallback).
- **`AiAutoReplyService`** (Application, new) — the decision pipeline: conversation mode → tenant
  settings enabled → business hours → daily limit → AI call → `requiresHuman` → confidence
  threshold → human-takeover race re-check → send. `AutoReplyWithEscalation` mode flips the
  conversation to the (previously unused) `ConversationStatus.Escalated` on any eligibility
  failure past the mode/settings gates; plain `AutoReply` takes no extra action. Full design
  reasoning in [ADR-0022](decisions/ADR-0022-ai-auto-reply.md).
- **`AiAutoReplySettingsService`** (Application, new) — thin CRUD over the settings row.
- **Wired into all three inbound-message paths**: `ConversationService.AddMessageAsync` (Manual
  channel, guarded to genuine inbound customer messages), `WebhookIngestionService.
  ApplyInboundMessageAsync` (WhatsApp/Instagram/Messenger), `WidgetService.SendInboundAsync`
  (website chat) — auto-reply applies uniformly across every channel, not just one.
- **API**: `PUT /api/v1/conversations/{id}/ai-mode` (per-conversation mode, `ai.configure`),
  `GET /api/v1/ai/auto-reply-settings` (`ai.read`), `PUT /api/v1/ai/auto-reply-settings`
  (`ai.configure`) — reused the existing `ai.read`/`ai.configure` permission keys from Phase 1's
  catalog (both already existed, unused until now).

## Tests

- **Unit** (8 new, `AiAutoReplySettingsTests`): default state, unconfigured-schedule always false,
  within/outside a configured window, day not scheduled, unresolvable time zone doesn't throw,
  confidence/limit clamping, business-hours JSON round-trip.
- **API** (13 new, `AiAutoReplyEndpointsTests`): the full decision pipeline exercised through real
  endpoints — sends when every gate passes; skips (no send) for tenant-settings-disabled and
  conversation-mode-disabled; `AutoReplyWithEscalation` escalates for outside-business-hours,
  low-confidence, and `requiresHuman`; plain `AutoReply` never escalates on `requiresHuman`; daily
  limit stops further sends after it's spent; `ai-mode` endpoint validates the enum and 404s for
  an unknown conversation; settings endpoint returns conservative defaults, round-trips business
  hours, and rejects an invalid (end-before-start) window.
- **Security** (3 new, `AiAutoReplySecurityTests`): a second tenant never sees or is affected by
  another tenant's auto-reply settings; the Agent role (has `ai.read` but not `ai.configure`)
  cannot configure settings or change a conversation's AI mode (403, not silently ignored); a
  foreign conversation id cannot have its AI mode changed cross-tenant (404).
- **Full backend suite**: 175/175 (49 unit + 37 integration + 26 security + 63 API).
- **End-to-end manual verification against the real Groq API** (not fake-provider-only): sent the
  actual production system prompt with a refund-request message — got back
  `{"requiresHuman": true, "escalationReason": "Refund request for broken item", ...}`; sent a
  known-FAQ question with reference material attached — got back `{"requiresHuman": false,
  "escalationReason": "", ...}`. Confirms PRD §71's example table isn't just documented intent but
  an observed behavior of the real model against the real system prompt.

## Security Review

Addressed PRD §71's full focus list — see `docs/security.md`'s new "Phase 12 controls" section:
unauthorized AI actions (two independent opt-in gates, both `ai.configure`-protected), prompt
injection/hallucination (reuses Phase 10/11's structural defenses plus the new hard
`requiresHuman`/confidence gate, verified live), data leakage (identical context assembly to
Suggest mode), infinite reply loops (structural — only genuine inbound-customer triggers, an
AI-sent message can't re-trigger itself), duplicate replies (existing webhook idempotency runs
before auto-reply evaluation), human takeover race conditions (re-check immediately before send),
provider restrictions (a second, independent daily cap beyond Suggest mode's own). No high/
critical findings.

## Performance/Reliability Review

- The business-hours check and daily-limit count are both simple indexed queries — no new
  expensive operation on the hot inbound-message path.
- The AI provider call (Groq) is the same network round-trip Suggest mode already makes; a
  provider failure fails safe (escalates or leaves for a human) rather than blocking message
  ingestion or retrying indefinitely.
- Outbound routing through `ChannelSendService` reuses the same Polly retry pipeline
  (WhatsApp/Instagram/Messenger) already exercised by agent-sent replies — no new failure mode
  introduced for the actual provider send.

## Migrations / Configuration Changes

- Migration `20260904032607_AddAiAutoReplySettings`: new `ai_auto_reply_settings` table
  (`TenantId` primary key, `Enabled`, `ConfidenceThreshold`, `DailyLimit`, `BusinessHoursJson`).
- `AuthService.RegisterAsync` now also creates a default (disabled) `AiAutoReplySettings` row for
  every newly registered tenant, alongside the existing Manual/WebsiteChat channel setup.

## ADRs / Docs Updated

New [ADR-0022](decisions/ADR-0022-ai-auto-reply.md). `docs/ai.md` (new Phase 12 section,
"still ahead" note updated to reflect both modes now shipped), `docs/security.md` (new "Phase 12
controls" section).

## Known Limitations

- No frontend UI for configuring auto-reply settings or per-conversation AI mode — API-only, same
  launch-state pattern as every Phase 6+ feature (channel connection, knowledge base management).
- `RouteOutboundAsync`'s send-and-mark logic is duplicated between `ConversationService` and
  `AiAutoReplyService` rather than extracted into a shared abstraction — a deliberate, small,
  contained duplication (ADR-0022's Consequences).
- Business-hours windows don't support crossing midnight (e.g. 22:00–02:00) — split into two
  windows instead; documented on `BusinessHoursWindow`.
- The full business-hours *management* feature (holidays, richer scheduling UI) is explicitly
  Phase 13 scope (PRD §72) — Phase 12 only needed enough of a schedule to gate auto-reply
  eligibility, which is what's built.

## Files/Modules Changed

`src/Omnichannel.Domain/Conversations/Conversation.cs` (`SetAiMode`),
`src/Omnichannel.Domain/Ai/AiAutoReplySettings.cs` (new),
`src/Omnichannel.Application/Abstractions/{IAiProvider,IAppDbContext}.cs`,
`src/Omnichannel.Application/Ai/{AiAutoReplyService,AiAutoReplySettingsService}.cs` (new),
`src/Omnichannel.Application/Auth/AuthService.cs`,
`src/Omnichannel.Application/Conversations/ConversationService.cs`,
`src/Omnichannel.Application/Channels/WebhookIngestionService.cs`,
`src/Omnichannel.Application/Widget/WidgetService.cs`,
`src/Omnichannel.Application/DependencyInjection.cs`,
`src/Omnichannel.Infrastructure/Ai/GroqAiProvider.cs`,
`src/Omnichannel.Infrastructure/Persistence/AppDbContext.cs`,
`src/Omnichannel.Infrastructure/Persistence/Configurations/AiAutoReplySettingsConfiguration.cs` (new),
`src/Omnichannel.Infrastructure/Persistence/Migrations/20260904032607_AddAiAutoReplySettings*` (new),
`src/Omnichannel.Infrastructure/Persistence/RoleSeeder.cs` (pre-Phase-12 CI fix),
`src/Omnichannel.Api/Endpoints/AiEndpoints.cs`,
`src/Omnichannel.Contracts/Ai/AiContracts.cs`,
`tests/Omnichannel.UnitTests/Domain/AiAutoReplySettingsTests.cs` (new),
`tests/Omnichannel.ApiTests/Ai/{AiAutoReplyEndpointsTests,FakeAiProvider}.cs`,
`tests/Omnichannel.SecurityTests/AiAutoReplySecurityTests.cs` (new),
`docs/decisions/ADR-0022` (new), `docs/{ai,security}.md`.

## Next Phase

Phase 13 — Business Rules + Automation (PRD §72): business hours (the full management feature,
building on Phase 12's schedule storage), holidays, escalation rules, notifications, saved
replies, basic automation.

**Stopping here per explicit user instruction — not proceeding to Phase 13 in this session.**
