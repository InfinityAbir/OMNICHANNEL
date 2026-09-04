# Privacy & Data Retention Review

PRD §74 (Phase 15): privacy review, data retention review.

## What personal data this system stores

| Data | Where | Notes |
|---|---|---|
| Agent/owner email, display name, password hash | `identity_users`/`app_users` | Password hashed via ASP.NET Core Identity's default hasher (PBKDF2-HMAC-SHA256), never stored or logged in plaintext. |
| Customer contact identifiers (phone/handle per channel) | `contacts`/`contact_identifiers` | The customer's channel-specific identity (WhatsApp number, Instagram/Messenger id, website-chat visitor id). |
| Customer message content | `messages` | Full conversation content, including anything a customer types — could include names, order numbers, addresses, etc. volunteered in free text. |
| Channel provider credentials | `channel_credentials` | Encrypted at rest (ASP.NET Core Data Protection, ADR-0016) — never stored or logged as plaintext. |
| Refresh tokens | `refresh_tokens` | Stored as a hash, never the raw token (PRD §60/security.md). |
| Audit trail | `audit_logs` | Actor, action, entity references, and a metadata JSON blob — `AuditService`'s own doc comment states metadata "must never contain secrets or full message content." |

## Privacy review findings

- **No plaintext secrets at rest**: passwords (Identity hasher), refresh tokens (hashed), channel
  credentials (Data Protection encryption) — verified during this phase's audit, consistent with
  `docs/security.md`'s existing review record.
- **AI providers see customer message content**: Suggest mode (Phase 10) and auto-reply (Phase
  12) both send the bounded conversation history to Groq to generate a draft/reply. This is
  inherent to the feature (PRD §69/§71) — the mitigation is structural, not avoidance: internal
  notes are explicitly excluded from what's sent (verified,
  `AiEndpointsTests.GenerateSuggestion_InternalNotesNeverIncludedInContext`), and the system
  prompt treats all content as data, never instructions (prompt-injection defense, ADR-0020).
  Tenants relying on this feature are implicitly sending customer message content to a
  third-party AI provider — worth stating explicitly in any customer-facing terms/privacy policy
  once one exists (not something this codebase can enforce; a product/legal decision).
  - Note: `HashingEmbeddingProvider` (Phase 11) is a local, deterministic embedding — no customer
    text ever leaves the process for knowledge-base indexing/retrieval itself, only for the final
    AI completion call.
- **PII never appears in URLs/query strings**: reviewed every `GET` endpoint added across all 15
  phases — search/filter parameters (`?q=`, `?cursor=`, `?status=`) are non-identifying; no
  endpoint accepts a token, email, or session identifier as a query parameter.
- **Logging**: Serilog structured logs (`Program.cs`) log request/response metadata via
  OpenTelemetry's ASP.NET Core instrumentation, not full request/response bodies — message
  content, credentials, and tokens are never logged. `SmtpEmailSender`'s own comment states this
  explicitly ("Never log the message body or recipient's full address").
- **Frontend token storage**: `localStorage` (documented XSS trade-off, ADR-0013) — a stolen
  token exposes the agent's own session, not other tenants' data (every subsequent request is
  still tenant-scoped server-side by the token's own `tenant_id` claim).

## Data retention — current state

**No automated retention/deletion policy exists yet.** Every table is append-only or
soft-mutated (status changes, not deletes) except where an explicit domain method removes a row
(e.g. removing a tag from a conversation, deleting a channel credential). Concretely, as of Phase
15:

- Messages, conversations, contacts, audit logs: retained indefinitely, no TTL/archival job.
- A tenant/account has no self-service "delete my data" or "delete my account" flow.
- No secure-deletion (crypto-shredding, hard-delete-on-request) mechanism exists.

This is an honest gap, not an oversight papered over: PRD's MVP scope (§4.3) doesn't call out
data retention/deletion tooling as in-scope for the phases built so far, and building a real
retention policy (which requires a product decision — how long is "long enough," what happens to
audit logs referencing deleted data, whether closed conversations get archived vs. deleted)
is a business decision this session can't make unilaterally. Recorded here so it's a visible,
tracked gap for whoever takes this to production, not silently absent.

**What a real retention policy would need, when scoped**: a configurable per-tenant retention
window (e.g. "delete closed conversations after N months"), a background job to enforce it (this
project has none yet — see `docs/disaster-recovery.md`'s "what's explicitly not built"), and a
decision on whether audit-log entries referencing deleted entities are redacted or retained
(compliance trails often need to survive the data they describe).

## Recommendation before real production use

1. A privacy policy / terms of service (product/legal, not engineering) should explicitly
   disclose that customer message content is sent to a third-party AI provider (Groq) when a
   tenant enables Suggest mode or auto-reply.
2. A data retention policy should be decided and then built as its own scoped phase — this
   review's job was to surface the gap accurately, not to guess a number and implement it.
3. A tenant offboarding/account-deletion flow should exist before onboarding real customer data
   at scale.
