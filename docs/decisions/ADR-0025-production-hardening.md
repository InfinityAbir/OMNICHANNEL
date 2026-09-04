# ADR-0025: Production Hardening

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §74 (Phase 15): dependency audit, static analysis, security testing, API penetration
testing, tenant-isolation tests, load testing, backup/restore test, disaster recovery plan,
secret rotation test, rate-limit test, queue failure test, provider outage test, AI provider
outage test, database failure test, logging/monitoring verification, privacy review, data
retention review. PRD §75's Final Security Checklist covers the same ground at a more granular,
per-category level.

## Decision

**Audit before building — most of the checklist was already satisfied by Phase 0-14's own
discipline.** Rather than assume gaps and build defensively, this phase inventoried what already
exists (rate limiting on auth/widget/webhook, Serilog structured logging, OpenTelemetry tracing/
metrics, a global `ProblemDetailsExceptionHandler`, HSTS/CSP/security headers, CORS allowlist,
refresh-token rotation + revocation, Identity account lockout, constant-time HMAC webhook
verification, health-check liveness/readiness probes) and only added what a genuine audit found
missing. Rebuilding already-solid infrastructure would have been wasted, riskier work.

**One global rate-limit policy added, layered on top of the existing named ones.** Auth/widget/
webhook already had their own tighter, purpose-specific policies (ADR predates this phase); every
other authenticated endpoint — `conversations`, `ai-suggestions`, the whole Phase 12-14 admin
surface — had none. Rather than add a `RequireRateLimiting` call to every individual endpoint
(tedious, easy to miss one on a future addition), a single `GlobalLimiter` partitioned per
authenticated user (falling back to per-IP) covers the whole API uniformly and composes with the
existing named policies rather than replacing them.

**Two payload-size fixes, found by the audit, not assumed in advance.** Two Phase 12/13 endpoints
accepted business-hours/holidays payloads with no size bound, while their backing columns are
`character varying(4000)` — an oversized request would have hit an unhandled Postgres error (a
500) rather than a clean validation failure. This is the kind of finding that audit-then-fix
surfaces and defensive-by-default coding sometimes misses; fixed with explicit bounds + regression
tests once found.

**Backup/restore was actually executed against a real database, not just described.** `pg_dump`
→ fresh database → `pg_restore` → row-count comparison across four tables, all exact matches.
Writing "backup/restore procedure documented" without running it once would be exactly the kind
of unverified claim this project's discipline (real end-to-end verification every phase) exists
to avoid.

**Frontend error handling: a global toast system, not per-component patches.** An audit of every
`.subscribe(...)` call site in `web/src` found several mutation actions with no error callback at
all — a failed request did nothing visible. A per-call-site fix would have needed to touch each
one individually and would drift as new features are added; a `ToastService` + `ToastHostComponent`
pair, wired into every previously-silent call site this phase and available for any future one,
extracts the backend's own ProblemDetails `title`/`detail` so the message shown is the one the API
actually intended, never a raw `HttpErrorResponse` object or a generic framework string.

**What's explicitly declared not-applicable, not silently skipped**: "queue failure test" has no
target — this is a modular monolith with no message broker (AGENTS.md's own architectural
constraint) — and "MFA" is a product decision (Identity's architecture already supports adding it
later; enabling it isn't a security *gap* in what's built). Both are recorded as reviewed-and-
not-applicable in `docs/security.md` rather than left ambiguous.

**Data retention and automated backup jobs are recorded as known gaps, not built speculatively.**
Neither has a chosen hosting target or a product decision behind it yet (how long to retain,
what a managed provider's backup feature will cover). Building either now would be guessing at
requirements that don't exist; `docs/privacy.md` and `docs/disaster-recovery.md` record them as
visible, tracked gaps for whoever takes this to actual production, which is more honest than a
speculative implementation that doesn't match the real (still undetermined) hosting environment.

## Consequences

- The global rate limit (600/min/user) is generous by design — it exists to bound scripted abuse
  and runaway clients, not to shape normal dashboard traffic. If real usage patterns ever
  approach it, that's a signal to revisit the number, not evidence it was wrong to add.
- No distributed/shared rate-limit store (e.g. Redis) — the current deployment is single-instance
  (`docker-compose.yml`, CI); an in-memory limiter is correct for that topology and would need
  revisiting only if/when the API scales to multiple instances behind a load balancer.
- Data retention and automated backup jobs remain unbuilt — explicitly a follow-up once hosting
  and a retention policy are decided, not this phase's job to guess.
