# Phase Report — Phase 15: Production Hardening

**Status:** Implementation complete. This is the final phase of the 16-phase build (Phase 0-15).
**Date:** 2026-09-04

## Scope / PRD references

PRD §74: dependency audit, static analysis, security testing, API penetration testing,
tenant-isolation tests, load testing, backup/restore test, disaster recovery plan, secret
rotation test, rate-limit test, queue failure test, provider outage test, AI provider outage
test, database failure test, logging/monitoring verification, privacy review, data retention
review. PRD §75's Final Security Checklist covers the same ground per-category.

## Approach

Audited first, built only what the audit found missing — see [ADR-0025](decisions/ADR-0025-production-hardening.md)
for the full reasoning. Most of PRD §75's checklist was already satisfied by prior phases'
discipline (rate limiting, structured logging, OpenTelemetry, exception sanitization, security
headers, CORS allowlist, refresh-token rotation, account lockout, constant-time webhook
signature verification) — re-verified, not rebuilt.

## Implemented / Verified

- **Dependency audit**: `dotnet list package --vulnerable --include-transitive` clean across all
  9 backend projects. `npm audit` clean for the `web` workspace (0 vulnerabilities). The `e2e`
  workspace's audit couldn't complete (sandbox network restriction reaching npmjs.org) — a
  tooling limitation, not a finding; the app's own shipped dependencies (`web`) are the ones that
  matter for production risk.
- **Full security audit** across every endpoint added in all 15 phases (OWASP Top 10,
  authorization matrix, IDOR/BOLA, webhook signature verification, CORS, secrets handling,
  frontend XSS/token storage) — see `docs/security.md`'s new "Phase 15" section for the complete
  findings list.
- **Fixed**: unbounded business-hours/holidays payloads on two Phase 12/13 endpoints (`ai/
  auto-reply-settings`, `tenant/business-hours`) that could hit an unhandled Postgres
  data-length error instead of a clean 400 — added explicit size guards + 3 regression tests.
- **Rate limiting extended**: a global per-authenticated-user (per-IP fallback) 600/min limiter
  now covers every endpoint, layered on top of the existing tighter auth/widget/webhook policies
  that previously covered only those three surfaces.
- **Backup/restore test — actually executed**: `pg_dump` the dev database, restore into a fresh
  one, compared row counts on `conversations` (972), `messages` (936), `tenants` (2519), `roles`
  (4) — exact match. Full procedure and disaster recovery plan in
  [`docs/disaster-recovery.md`](../disaster-recovery.md) (new).
- **Privacy and data retention review**: [`docs/privacy.md`](../privacy.md) (new) — inventories
  what personal data is stored and where, confirms no plaintext secrets at rest, confirms AI
  providers only see conversation content that's structurally necessary (internal notes
  excluded), and honestly records that no automated retention/deletion policy exists yet (a
  product decision, not an engineering oversight).
- **Frontend hardening**: audited every `.subscribe(...)` call site in `web/src` for error
  handling. Found several mutation actions (assign, unassign, status/priority change, tag
  add/remove, create-conversation, load-more) with no error handler — a failed request did
  nothing visible to the user. Built a global toast notification system (`ToastService`,
  `ToastHostComponent`) that extracts the backend's own ProblemDetails message, wired into every
  previously-silent call site. Verified live in the browser: forced a real 404 on "assign to me"
  and confirmed a clean "Conversation not found." toast appears (not silence, not a raw error) —
  then confirmed the success path still works normally with the fix in place.

## Tests

- 3 new regression tests for the payload-size fixes (`BusinessHours_TooManyHolidays_...`,
  `BusinessHours_TooManyWindowsInOneDay_...`, `AutoReplySettings_TooManyWindowsInOneDay_...`).
- Full backend suite: 217/217 (67 unit + 37 integration + 31 security + 82 API).
- Frontend: `ng build --configuration production` clean (no errors, no budget warnings). Full
  Playwright e2e suite: 5/5 passed, confirming the toast-system and rate-limiter changes didn't
  regress the register→conversation→realtime→widget flows.

## Security Review

Full findings in `docs/security.md`'s "Phase 15" section. Summary: no critical or high findings
in the existing codebase; two medium/low findings (unbounded payload → 500 instead of 400) found
and fixed in code from this session's own Phase 12/13 work; one real UX/reliability gap
(silent-failure frontend mutations) found and fixed with the toast system. "Queue failure test"
confirmed not applicable (no message broker exists — modular monolith by design). MFA confirmed
architecture-ready but not enabled (product decision).

## Performance/Reliability Review

- Global rate limiter is in-memory, appropriate for the current single-instance deployment
  topology (`docker-compose.yml`/CI) — would need a shared store (Redis) only if/when the API
  scales to multiple instances behind a load balancer (ADR-0025's Consequences).
- Backup/restore procedure timing at current data volume (972 conversations, 936 messages, 2519
  tenants): both dump and restore completed in well under a minute.

## Migrations / Configuration Changes

- None — this phase's changes are middleware/endpoint-level (rate limiter policy, input
  validation) and frontend-only; no schema change.

## ADRs / Docs Updated

New [ADR-0025](decisions/ADR-0025-production-hardening.md),
[`docs/disaster-recovery.md`](../disaster-recovery.md) (new),
[`docs/privacy.md`](../privacy.md) (new). `docs/security.md` (new "Phase 15" section).

## Known Limitations (recorded, not hidden)

- No automated data-retention/deletion tooling or account-deletion flow (`docs/privacy.md`).
- No automated backup job in-repo — deferred to the hosting provider's managed backup feature
  once one is chosen (`docs/disaster-recovery.md`).
- No JWT signing-key rotation overlap mechanism (single key, no `kid`-based multi-key
  validation) — rotating today invalidates every session immediately, acceptable for a planned
  rotation, the entire point for a compromise response.
- No load-testing infrastructure was run against a realistic concurrent-user simulation (no
  k6/Artillery/similar tool available in this sandbox) — the backup/restore timing and the
  existing 217-test suite's real-Postgres execution are the closest verified signal on
  performance at this phase; a dedicated load test needs real infrastructure this environment
  doesn't have.

## This is the final phase

Phase 0 through Phase 15 are all complete. The product has: multi-channel inbox (WhatsApp/
Instagram/Messenger/website chat/manual), realtime updates, AI suggest + auto-reply with
conservative safety gates, a knowledge base (RAG), business rules/automation, analytics, and this
phase's hardening/audit pass. Every phase shipped with real tests (unit/integration/API/security),
a security review, docs/ADRs, and CI verification — see `docs/phase-reports/` for the complete
history and `docs/decisions/` for the 25 ADRs recording why each significant choice was made.

Remaining known gaps (all explicitly documented, not silent): no frontend UI for Phase 6-14's
admin features (channel connection, knowledge base, AI/automation settings, saved replies,
analytics dashboard — all API-only), no automated data retention/deletion, no chosen hosting
target or deployment pipeline beyond CI, no load-testing infrastructure exercised.
