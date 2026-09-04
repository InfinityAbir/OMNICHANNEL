# ADR-0024: Analytics

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §73 (Phase 14): inbox metrics, response time, resolution, AI metrics, channel metrics, agent
metrics. Security review: analytics queries must never aggregate across tenants. Performance
review: avoid calculating expensive analytics on every dashboard request; use appropriate
indexes/materialized/aggregated data if needed.

## Decision

**One summary endpoint, one service, real SQL aggregates — no materialized views or precomputed
tables yet.** `GET /api/v1/analytics/summary?from=&to=` computes every metric via COUNT/AVG/
GROUP BY queries executed directly against the live tables, never by loading full row sets into
memory. At this project's actual data scale (a single small/medium business's inbox), a
well-indexed aggregate query is fast; a materialized-view or scheduled-rollup layer would be
premature infrastructure for a need that doesn't exist yet (AGENTS.md: no premature
abstraction) — revisit if/when real usage shows otherwise.

**A new `(TenantId, CreatedAt)` index on `conversations`.** Every analytics query starts by
filtering conversations to the requested date range; without this index that's a full tenant-scoped
scan on every dashboard load. Existing indexes back the inbox list query (`Status`,
`AssignedUserId`) but none covered date-range filtering — the concrete "appropriate index" PRD §73
asks for.

**Response time and resolution time are computed differently, on purpose.** Resolution time uses
the existing `Conversation.ClosedAt` field directly (already captured since Phase 2) — no message
scan needed. First-response time has no equivalent stored field, so it's computed with a grouped
query per conversation (`MIN(CASE WHEN SenderType = 'Customer' THEN CreatedAt END)` vs. the same
for Agent/Ai), which EF Core translates to a single indexed aggregate query, not N+1 lookups.

**Agent metrics are scoped to what's actually trackable today.** `Message` has no
"sent-by-user-id" field (an agent's own reply isn't attributed to them individually anywhere in
the schema), so per-agent *reply* counts aren't possible without a schema change this phase didn't
make. Agent metrics are scoped instead to what `Conversation.AssignedUserId` already captures:
conversations currently assigned to an agent, and how many of those are `Closed` — a real,
honestly-scoped proxy, not a fabricated per-message attribution.

**Tenant isolation relies on the ordinary EF global filter, not an explicit exception.** Unlike
`AiAutoReplyService`/`AutomationRuleService` (Phase 12/13), `AnalyticsService` has no
unauthenticated call site — it's a pure authenticated-dashboard read — so it uses `ITenantContext`
and the standard automatic tenant filter (ADR-0005) rather than `IgnoreQueryFilters()` +
an explicit `tenantId`. This is deliberately the *simpler* of the two patterns this codebase now
has, used because the simpler one is actually correct here.

## Consequences

- No per-message agent attribution — a real, documented limitation, not a silent gap. Adding it
  later means a schema change to `Message` (e.g. `SentByUserId`), out of this phase's scope.
- No frontend dashboard UI yet — API-only, the same launch-state pattern as every Phase 6+
  feature.
- No caching/materialization layer — acceptable at current scale; the index added this phase is
  the concrete performance work PRD §73 actually asked for, not a preemptive one.
