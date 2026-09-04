# AI Assistant

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
- Two modes only: **Suggest** (human approves before send — implemented, above) ships first;
  **Auto-reply** (Phase 12) only after Suggest is stable, and only within business
  hours/eligibility/confidence rules — never bypassing them.
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

Knowledge retrieval abstraction and conversation summarization proper (beyond the fixed
10-message window Suggest mode uses) land with Phase 11 (Knowledge Base / RAG).
