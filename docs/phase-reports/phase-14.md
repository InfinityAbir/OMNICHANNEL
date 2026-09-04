# Phase Report — Phase 14: Analytics

**Status:** Implementation complete. Proceeding to Phase 15 per explicit user instruction (no
approval pause).
**Date:** 2026-09-04

## Scope / PRD references

PRD §73 (Phase 14): inbox metrics, response time, resolution, AI metrics, channel metrics, agent
metrics. Security: analytics queries must never aggregate across tenants. Performance: avoid
expensive per-request calculation, use indexes/aggregated data as needed.

## Implemented

- **`AnalyticsService`** (Application, new) — `GetSummaryAsync(from, to)` computes, via real SQL
  aggregate/grouped queries (never full row materialization): conversation counts by status,
  average first-response time (grouped `MIN(CASE WHEN ...)` over messages), average resolution
  time (from the existing `Conversation.ClosedAt`), resolution rate, AI suggestion count +
  average confidence, AI auto-reply send count, per-channel conversation counts, per-agent
  assigned/closed conversation counts. Full design reasoning in
  [ADR-0024](decisions/ADR-0024-analytics.md).
- **API**: `GET /api/v1/analytics/summary?from=&to=` (`analytics.read`, an existing PRD §12
  permission key, unused until now), defaults to the trailing 30 days when no range is given,
  400s on an inverted range.
- **Performance**: new `(TenantId, CreatedAt)` index on `conversations` — every summary query
  starts with a date-range filter that had no covering index before this phase.
- **Tenant isolation**: relies on the ordinary EF global tenant filter (no unauthenticated call
  site exists for analytics, unlike Phase 12/13's services) — the simpler of the two isolation
  patterns this codebase now has, used because it's the correct one here.

## Tests

- **API** (6 new, `AnalyticsEndpointsTests`): zeroed metrics with no data; status/channel counts
  and resolution rate/time after closing a conversation; first-response time after an agent reply;
  AI suggestion count + confidence via `FakeAiProvider`; per-agent breakdown after assignment;
  invalid (inverted) date range → 400.
- **Security** (1 new, `AnalyticsSecurityTests`): a query shaped to actually reveal cross-tenant
  leakage if the filter were missing (5 conversations on Tenant A, 1 on Tenant B — Tenant B's
  summary must show exactly 1) rather than just "different tenants see different counts."
- **Full backend suite**: 214/214 (67 unit + 37 integration + 31 security + 79 API).
- Verified the EF query translations that matter most here — the grouped conditional-`MIN`
  first-response-time query and the conversation-id subquery filter — actually execute correctly
  against real Postgres (not just compile), by running the new tests against the live CI/local
  Postgres service rather than an in-memory provider.

## Security Review

Addressed PRD §73's focus in full — see `docs/security.md`'s new "Phase 14 controls" section:
never aggregates across tenants (verified with a leak-revealing test shape, not just presence/
absence), authorization via the existing `analytics.read` key. No high/critical findings.

## Performance/Reliability Review

- New `(TenantId, CreatedAt)` index on `conversations` backs every summary query's date-range
  filter — see ADR-0024.
- No materialized views or precomputed rollups — a deliberate scope decision at current data
  scale, not an oversight (ADR-0024's Consequences).
- Every metric is a single grouped/aggregate SQL query; no N+1 pattern anywhere in
  `AnalyticsService`.

## Migrations / Configuration Changes

- Migration `20260904061616_AddConversationCreatedAtIndex`: `(TenantId, CreatedAt)` index on
  `conversations`.

## ADRs / Docs Updated

New [ADR-0024](decisions/ADR-0024-analytics.md). `docs/security.md` (new "Phase 14 controls"
section).

## Known Limitations

- No per-agent reply attribution — `Message` has no "sent by user" field, so agent metrics are
  scoped to assignment-based counts (assigned/closed), not individual reply counts. A real,
  documented gap, not a silent one (ADR-0024).
- No frontend dashboard UI yet — API-only, same launch-state pattern as every Phase 6+ feature.

## Files/Modules Changed

`src/Omnichannel.Application/Analytics/AnalyticsService.cs` (new),
`src/Omnichannel.Application/DependencyInjection.cs`,
`src/Omnichannel.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs`,
`src/Omnichannel.Infrastructure/Persistence/Migrations/20260904061616_AddConversationCreatedAtIndex*` (new),
`src/Omnichannel.Api/Endpoints/AnalyticsEndpoints.cs` (new), `Program.cs`,
`src/Omnichannel.Contracts/Analytics/AnalyticsContracts.cs` (new),
`tests/Omnichannel.ApiTests/Analytics/AnalyticsEndpointsTests.cs` (new),
`tests/Omnichannel.SecurityTests/AnalyticsSecurityTests.cs` (new),
`docs/decisions/ADR-0024` (new), `docs/security.md`.

## Next Phase

Phase 15 — Production Hardening (PRD §74): dependency audit, static analysis, security testing,
API penetration testing, tenant-isolation tests, load testing, backup/restore test, disaster
recovery plan, secret rotation test, rate-limit test, queue failure test, provider outage test,
AI provider outage test, database failure test, logging/monitoring verification, privacy review,
data retention review.

**Proceeding directly to Phase 15 per explicit user instruction — no approval pause.**
