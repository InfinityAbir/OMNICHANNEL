# AI Assistant

## Phase 12 — Auto-reply (implemented)

`AiAutoReplyService` (Application/Ai): a conservative decision pipeline gating on, in order,
`ConversationAiMode` (AutoReply/AutoReplyWithEscalation), the tenant's `AiAutoReplySettings`
(off by default), business hours (unconfigured by default = never open — no assumed 24/7),
a per-tenant daily send cap independent of Suggest mode's own cap, the AI's own `requiresHuman`
self-assessment (refunds/complaints/high-risk/uncertain), and a configurable confidence threshold
(default 0.85). Only when every gate passes does it actually send — through the same
`ChannelSendService` outbound routing Suggest-mode-approved replies use, so it reaches WhatsApp/
Instagram/Messenger/website-chat exactly like an agent's own reply would.

`AutoReplyWithEscalation` mode differs from plain `AutoReply` only in what happens when the AI
*would* have handled a message but couldn't right now (outside hours, low confidence, daily limit,
`requiresHuman`, or a provider failure): it flips the conversation to `ConversationStatus.
Escalated` for priority human attention, audit-logged as `ai.autoreply.escalated`. Plain
`AutoReply` takes no extra action in that case — the message just sits for normal human pickup.

Verified live against the real Groq API that the model's `requiresHuman` field actually
distinguishes PRD §71's example cases correctly: a refund request → `requiresHuman: true` with a
reason; a known-FAQ question answered from reference material → `requiresHuman: false`. See
`docs/phase-reports/phase-12.md` for the exact exchanges.

Wired into all three inbound-message paths so it applies uniformly across channels:
`ConversationService.AddMessageAsync` (Manual channel), `WebhookIngestionService.
ApplyInboundMessageAsync` (WhatsApp/Instagram/Messenger), `WidgetService.SendInboundAsync`
(website chat) — each triggers evaluation only for a genuine inbound customer message, never an
agent's or the AI's own outbound send (the infinite-loop guard, PRD §71 security focus).

## Phase 10 — Suggest mode (implemented)

`IAiProvider` (Application/Abstractions) → `GroqAiProvider` (Infrastructure), model
`openai/gpt-oss-120b` (chosen from Groq's live model catalog, not assumed — see
[ADR-0020](decisions/ADR-0020-ai-suggestion-mode.md)). `AiSuggestionService` builds a bounded,
tenant-scoped context (last 10 customer-visible messages, internal notes excluded), calls the
provider, and logs every interaction (`AiSuggestion`: text, model, tokens, confidence) — doubling
as the PRD §69 interaction log. `POST /api/v1/conversations/{id}/ai-suggestions`
(`ai.read` permission) returns the draft to the Angular composer; the agent reviews, edits, and
sends via the existing message-send path — the AI never sends anything itself. A configurable
daily-per-tenant cap (`AiUsageLimiter`) falls back to "reply manually" once reached, and a
provider failure falls back the same way rather than surfacing a raw error.

**Language matching**: the system prompt instructs the model to reply in the same language *and
script* as the customer's message — Bangla script for Bangla, Latin letters for Banglish (Bangla
written phonetically in English letters), English for English — verified live against the real
API with real Bangla and Banglish input, not assumed. See ADR-0020 and
`docs/phase-reports/phase-10.md` for the actual verified exchanges.

**Prompt injection**: conversation history is passed as separate role-tagged messages, never
concatenated into the system instruction text — a customer message can't rewrite the assistant's
instructions no matter what it says, by construction, not just by asking the model nicely.

This document also records the binding design constraints from PRD §22–28/§37/§51/§52 for
phases still ahead:

- Provider-agnostic abstraction (`IAiProvider`, `IAiEmbeddingProvider`, optional
  `IAiModerationProvider`) — the application must not depend on a specific AI vendor.
- Two modes only: **Suggest** (human approves before send — Phase 10) and **Auto-reply**
  (Phase 12, implemented above) — both shipped, in that order, and Auto-reply only ever acts
  within business hours/eligibility/confidence rules, never bypassing them.
- The AI is not the authority: it proposes, the application validates, authorizes, and acts
  (PRD §87). No AI output is ever executed directly.
- Must never invent business facts (prices, availability, refund eligibility, order status,
  etc.) — must say "I don't have enough information" and escalate instead.
- Customer messages, documents, and retrieved knowledge are untrusted data, never instructions —
  system policy stays higher priority than any content the AI is shown (PRD §37 prompt injection
  defense).
- Structured, schema-validated AI output only; never blindly executed.
- Tenant isolation applies to retrieval and context assembly, not just storage.
- Cost tracking and configurable usage limits (daily/monthly/per-conversation) with safe
  fallback to human handling when limits are reached.

## Phase 11 — Knowledge Base / retrieval (implemented)

`IEmbeddingProvider` → `HashingEmbeddingProvider` (Infrastructure): a deterministic lexical
(feature-hashing) embedding, not neural — checked first, not assumed, that no embeddings-capable
key was available (Groq's own model catalog has none). Real, working keyword/near-duplicate
retrieval today; swappable for a neural provider later via the same interface. `KnowledgeService`
owns document → chunk → embedding indexing (plain-text documents, fixed-size chunking,
versioning, re-indexing on revision). `PgVectorKnowledgeRetrievalService` does tenant-scoped
nearest-neighbor lookup over pgvector. `AiSuggestionService` retrieves the top-3 relevant chunks
for the customer's latest message and feeds them to the AI as labeled, untrusted reference
material — verified end-to-end against the real Groq API (a knowledge document's exact figures
appeared correctly in a live suggestion, not invented ones). Full detail:
[ADR-0021](decisions/ADR-0021-knowledge-base.md).

Conversation summarization proper (beyond the fixed 10-message window Suggest mode uses) isn't
built — not needed at the scale exercised so far; revisit if long conversations make the window
insufficient.
