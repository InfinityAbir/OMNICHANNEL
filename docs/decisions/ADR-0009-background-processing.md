# ADR-0009: In-process background processing, no external broker (yet)

**Status:** Accepted
**Date:** 2026-09-03

## Context

PRD §44 requires background handling for webhook processing, outbound retries, AI processing,
knowledge indexing, embedding generation, analytics aggregation, notifications, and cleanup
jobs — and explicitly allows starting with ASP.NET Core hosted workers, introducing a durable
queue only "if scale requires it later." AGENTS.md forbids introducing a message broker without
a documented, demonstrated constraint.

## Decision

Use in-process `BackgroundService`/`IHostedService` workers within `Omnichannel.Api` for Phase
0–9. Idempotent processing (required by PRD §17 for webhook delivery) is achieved via a
Postgres-backed outbox/inbox pattern — persisted event/message IDs with a unique constraint
(e.g. `UNIQUE(ChannelAccountId, ExternalMessageId)`) — rather than an external message broker.
No RabbitMQ/Kafka/Azure Service Bus/etc. in this phase.

## Alternatives considered

- **External message broker from day one.** Rejected per AGENTS.md's explicit constraint; no
  current throughput data justifies the operational cost (broker HA, dead-lettering
  infrastructure, another moving part in local dev and CI).
- **Hangfire/Quartz.NET for durable scheduling.** Not ruled out permanently, but Phase 0 has no
  jobs yet to schedule; introducing a job-scheduling dependency before there's a job to run would
  violate the "don't add dependencies without clear need" coding rule. Reconsider when Phase 6
  (channel framework) or Phase 10 (AI processing) defines real retry/backoff requirements the
  built-in `BackgroundService` can't satisfy cleanly.

## Consequences

- Every background operation must be safe to run more than once (idempotency is the design
  default, not an afterthought) — this constrains how outbound-message retries and webhook
  processing get built from Phase 5/6 onward.
- If a specific worker demonstrably needs independent scaling or a durable queue's crash-recovery
  guarantees beyond what Postgres-backed outbox gives, that becomes its own ADR with the
  demonstrated constraint documented, not a default.
