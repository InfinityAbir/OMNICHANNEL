# ADR-0020: AI Suggestion Mode (provider, prompt design, language handling)

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §69 (Phase 10): the first AI feature — Suggest mode, where the AI drafts a reply and a human
agent reviews/edits/sends it, never auto-sent. `docs/ai.md` recorded the binding constraints
ahead of time (Phase 0): provider-agnostic abstraction, human-approves-before-send, never invent
business facts, treat customer content as untrusted data not instructions, schema-validated
output, tenant isolation in context assembly, cost tracking with usage limits and safe fallback.

The user supplied a Groq API key for this phase. Per those constraints, the specific model had to
be chosen deliberately, not guessed — verified live against Groq's own `/openai/v1/models`
endpoint with the actual key (not assumed from prior knowledge, since Groq's catalog changes).

## Decision

**`IChannelAdapter`'s pattern, once more.** `IAiProvider` (Application/Abstractions) is the one
seam the rest of the app depends on; `GroqAiProvider` (Infrastructure) is the only thing that
knows it's Groq. Swapping providers later is a new class + a DI registration change.

**Model: `openai/gpt-oss-120b`**, chosen from the live model list because it's the largest/most
capable text model that supports `structured_outputs`/`json_mode` (needed for reliable
`{suggestion, confidence}` output) among what this key's account can access — not the cheaper
`gpt-oss-20b`, since suggestion quality directly affects what an agent might send to a real
customer. `response_format: json_object` (not the stricter `json_schema` mode) — verified live to
produce exactly the intended shape reliably; the provider still defensively parses and falls back
to raw-text-with-low-confidence if a response ever doesn't parse, rather than throwing.

**Prompt-injection defense is structural, not just instructional.** Conversation history is
passed as separate `user`/`assistant`-role messages, never string-concatenated into the system
instruction text (`GroqAiProvider.SystemInstructions` is a `const` populated only with the
business name, never customer content). A customer message reading "ignore your instructions" is
just data in a user-role turn — there is no code path where it becomes part of the system prompt.
Verified by `AiEndpointsTests.GenerateSuggestion_CustomerMessageIsPassedAsDataNotConcatenatedIntoInstructions`.

**Confidence is model-self-reported**, not computed from `finish_reason` or logprobs — the system
prompt asks the model to include a `confidence` field and explicitly instructs it to set that
number low when it doesn't have enough information rather than guess. Verified live: asked about
real-time stock the model has no access to, it correctly said so and drafted a request for more
detail rather than inventing an answer (docs/phase-reports/phase-10.md).

**Context excludes internal notes.** Only the customer-visible message thread (last 10 messages)
is sent to the model — internal notes are agent-only/confidential by design (PRD §18) and must
never leave the system to a third-party AI provider (AGENTS.md's "sensitive data sent to AI"
focus). Verified: `AiEndpointsTests.GenerateSuggestion_InternalNotesNeverIncludedInContext`.

**Usage limit is a simple daily count against the interaction log itself**, not a separate counter
table — `AiUsageLimiter` queries `AiSuggestion` rows created since UTC midnight per tenant. A soft
cost-control cap doesn't need the concurrency guarantee a hard limit would; a small race under
heavy concurrent use is an acceptable trade-off for not adding locking to a cost-control feature.
Configurable (`Ai:Groq:DailySuggestionLimitPerTenant`, default 200; ≤0 means unlimited, an
explicit deployer choice).

**Language and script matching is explicit, not assumed.** The user asked for Bangla support —
tested live and confirmed the model naturally matches input language, but the instruction is now
explicit in the prompt anyway (relying on implicit LLM behavior for something a real customer
depends on would be fragile). Extended to Banglish (Bangla written in Latin letters, a common
informal register) after the user's follow-up — verified live: given Banglish input, the model
replies in Banglish, not Bangla script and not an English translation. Both verified against the
real API with real Bangla/Banglish text (not assumed from documentation), captured in
`docs/phase-reports/phase-10.md`.

**AI interaction logging doubles as the suggestion record.** `AiSuggestion` stores the full
suggested text, model, token counts, and confidence — satisfying PRD §69's "AI interaction
logging" deliverable using one table rather than a separate audit-only log plus a separate
UI-facing suggestion record.

## Alternatives considered

- **`json_schema` strict structured outputs instead of `json_object`.** Deferred: `json_object`
  mode plus defensive parsing was verified live to work reliably for this shape; adding strict
  schema mode is a low-risk future tightening, not required to ship correctly now.
- **A separate counter table/row for usage limits.** Rejected: the interaction log already has
  everything needed; a dedicated counter adds write-amplification and its own concurrency
  questions for a feature that only needs to be an approximate cost guard, not a hard quota.
- **Ask the customer which language to reply in, or always reply in English.** Rejected per the
  user's explicit ask — matching the customer's own language/script is what makes a suggestion
  actually usable by the agent without extra translation work.

## Consequences

- Groq's `/chat/completions` endpoint and this exact prompt shape are now load-bearing — if Groq
  changes `openai/gpt-oss-120b`'s availability or behavior, `AiOptions.Model` is the one place to
  change, not application code.
- No template/multi-turn conversation summarization exists yet (PRD §69 also lists "conversation
  summarization" and "knowledge retrieval abstraction" as Phase 10 deliverables) — summarization
  wasn't needed for the 10-message window used here; knowledge retrieval is explicitly Phase 11
  (RAG) per PRD's own phase order. Tracked as a known limitation, not silently dropped.
