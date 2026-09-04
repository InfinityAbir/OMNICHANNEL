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

## Data retention and account deletion — resolved 2026-09-05

**Self-service deletion now exists, with a 14-day grace period, for both a business account and
an individual user's own account.** Full design in
`docs/decisions/ADR-0030-data-retention-account-deletion.md`; summary:

- **Delete this business** (tenant Owner only, Settings → Account, or `POST`/`DELETE
  /api/v1/tenant/deletion`): schedules the whole tenant for permanent deletion 14 days out.
  Blocks new logins to that tenant immediately; the actual data isn't touched until the grace
  period elapses. Cancellable any time before then.
- **Delete my account** (any user, any time, Settings → Account, or `DELETE /api/v1/users/me`):
  removes the user from every business they belong to, scrubs their email/display name, and
  deletes their login credential outright (immediately unable to log in again). Blocked only if
  they're the sole Owner of a tenant that still has other members (no one would be left able to
  manage it) — in which case they're told to transfer ownership or delete the business account
  first. If they're the sole Owner of a tenant with no other members at all, deleting their
  account also schedules that now-ownerless tenant for deletion.
- **The actual purge** (`TenantDataPurgeService`, hourly): permanently removes every row across
  every tenant-owned table for a tenant whose grace period has elapsed — generic, by reflection
  over every `ITenantOwned` entity type, so a future new entity is covered automatically. Audit
  log entries and the `Tenant` row itself are deliberately kept (marked `Deleted`, not removed) —
  a compliance trail should survive the data it describes, per this doc's own earlier
  recommendation.
- Verified live end-to-end in the browser, not just via the automated suite (see ADR-0030), and
  with 16 new backend tests (296/296 total green).

## Recommendation before real production use

1. A privacy policy / terms of service (product/legal, not engineering) should explicitly
   disclose that customer message content is sent to a third-party AI provider (Groq) when a
   tenant enables Suggest mode or auto-reply.
2. ~~A data retention policy should be decided and then built as its own scoped phase~~ — done,
   see above.
3. ~~A tenant offboarding/account-deletion flow should exist before onboarding real customer data
   at scale~~ — done, see above.
