# ADR-0022: AI Auto-Reply

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §71 (Phase 12): only after Suggest mode (Phase 10) is stable. Business hours, AI eligibility
rules, confidence thresholds, escalation, human takeover, auto-reply limits, AI response
validation, AI action auditing. "Default behavior should be conservative", with an explicit
example table: known FAQ/known product info → reply; unknown/refund/high-risk/low-confidence →
human. Security review focus: unauthorized AI actions, prompt injection, hallucination, data
leakage, infinite reply loops, duplicate replies, human takeover race conditions, provider
restrictions.

## Decision

**Two independent opt-in gates, both off by default.** `AiAutoReplySettings.Enabled`
(tenant-wide) and `Conversation.AiMode` (per-conversation, `AutoReply`/`AutoReplyWithEscalation`)
must both explicitly allow it — a fresh tenant, or a conversation nobody has configured, can never
auto-reply. Belt-and-suspenders rather than a single switch, matching PRD's "conservative default"
instruction literally rather than just in spirit.

**Business hours are unconfigured = never eligible, not 24/7.** `AiAutoReplySettings.
BusinessHoursJson` is null until a tenant explicitly sets a weekly schedule; `IsWithinBusinessHours`
returns `false` for an unconfigured schedule, an unresolvable IANA time zone id, or a day not in
the schedule — every failure mode in this method fails toward "don't auto-reply," never toward
"assume it's fine."

**The AI's own `requiresHuman` self-assessment is a hard gate, not a suggestion.**
`AiCompletionResult` gained `RequiresHuman`/`EscalationReason`; `GroqAiProvider`'s system prompt
now asks the model to flag refunds, complaints, high-risk/sensitive topics, and anything it can't
answer confidently — mirroring PRD §71's example table almost verbatim. `AiAutoReplyService`
treats `RequiresHuman == true` as authoritative regardless of the numeric confidence score, and a
malformed/unparseable provider response defaults to `RequiresHuman = true` (GroqAiProvider's
`ParseSuggestionJson` fallback) — the model failing to follow the structured-output contract is
itself a reason not to trust it enough to auto-send. Verified live against the real Groq API for
both branches (see `docs/phase-reports/phase-12.md`) — this isn't just a documented intent, it's
an observed model behavior with the actual production system prompt.

**A separate daily cap from Suggest mode's own limit.** `AiUsageLimiter` (Phase 10) throttles how
many suggestion *drafts* a tenant generates; `AiAutoReplySettings.DailyLimit` throttles how many
messages get auto-*sent* to real customers — a materially different risk, so it gets its own,
independently configurable number rather than reusing the same counter.

**`AutoReplyWithEscalation` reuses the existing, previously-unused `ConversationStatus.Escalated`
value** rather than inventing a new concept. When the tenant-wide switch is on, the conversation's
mode is `AutoReplyWithEscalation`, and the AI would have handled the message in principle but
couldn't right now (outside hours, daily limit, low confidence, `requiresHuman`, or a provider
failure), the conversation flips to `Escalated` for priority human attention. Plain `AutoReply`
mode takes no such action in the same situation — the message just sits for ordinary human pickup,
identical to pre-Phase-12 behavior. Settings-disabled and mode-disabled are *not* escalation
triggers — those mean the feature isn't meant to apply here at all, not "the AI tried and failed."

**`AiAutoReplyService` takes an explicit `tenantId`, not `ITenantContext`.** Unlike
`AiSuggestionService` (Phase 10), which only runs from an authenticated agent request, auto-reply
must be invoked from three different contexts: an authenticated agent request (Manual channel), an
authenticated-but-not-tenant-authenticated widget-visitor request (website chat), and a fully
unauthenticated provider webhook (WhatsApp/Instagram/Messenger). Every query inside it uses
`IgnoreQueryFilters()` + an explicit `TenantId ==` predicate — the same documented exception to the
automatic EF tenant filter that `WebhookIngestionService` already established (ADR-0016), extended
here because this service specifically needs to be correct in all three contexts uniformly rather
than assuming ambient tenant context is always present.

**Infinite-loop and duplicate-reply prevention are structural, not defensive checks.**
`EvaluateAsync` is only ever called from the three genuine inbound-*customer*-message code paths
(`ConversationService.AddMessageAsync` when `Direction == Inbound && SenderType == Customer`,
`WebhookIngestionService.ApplyInboundMessageAsync`, `WidgetService.SendInboundAsync`) — never from
an outbound send, so the AI's own auto-sent message (`MessageDirection.Outbound`,
`MessageSenderType.Ai`) cannot itself become a future trigger. Duplicate replies are prevented one
layer up: the existing `UNIQUE(ChannelAccountId, ExternalMessageId)` webhook idempotency check
(PRD §17) rejects a redelivered provider event before `EvaluateAsync` is ever invoked for it.

**Human-takeover race guard: re-check right before sending, not just at the start.** The AI
provider call is a real network round-trip (typically sub-second to a few seconds against Groq).
Right before persisting the auto-sent message, `EvaluateAsync` re-queries the conversation's
current `AiMode` and checks whether any agent message was created since evaluation started,
skipping the send with no escalation (a human is already handling it) if either changed.

## Consequences

- A tenant must do real configuration work (enable the switch, set business hours, choose a
  conversation's mode) before anything auto-sends — correct for a conservative default, but means
  the feature is invisible/inert until deliberately set up; no onboarding UI for it exists yet
  (API-only, same limitation pattern as Phase 6-9's channel connection and Phase 11's knowledge
  base management).
- `RouteOutboundAsync`'s recipient-resolution + send + mark-sent/failed logic is duplicated (not
  shared via a new abstraction) between `ConversationService` and `AiAutoReplyService` — a
  deliberate, small, contained duplication rather than a premature shared abstraction across two
  services with otherwise unrelated responsibilities (AGENTS.md: no premature abstraction).
- No frontend UI for configuring auto-reply settings or per-conversation AI mode yet — API-only,
  consistent with every other Phase 6+ feature's launch state.
