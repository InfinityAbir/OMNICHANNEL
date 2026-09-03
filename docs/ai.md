# AI Assistant

No AI code exists yet — deliberately: Phase 0 does not scaffold `IAiProvider` or any AI
abstraction ahead of the phase that needs it (AGENTS.md: don't implement future-phase features
prematurely). This document records the binding design constraints from PRD §22–28/§37/§51/§52
so they aren't lost before Phase 10:

- Provider-agnostic abstraction (`IAiProvider`, `IAiEmbeddingProvider`, optional
  `IAiModerationProvider`) — the application must not depend on a specific AI vendor.
- Two modes only: **Suggest** (human approves before send) ships first (Phase 10); **Auto-reply**
  (Phase 12) only after Suggest is stable, and only within business hours/eligibility/confidence
  rules — never bypassing them.
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

This file gets real content starting Phase 10 (AI Suggestion Mode) and Phase 11 (Knowledge
Base / RAG).
