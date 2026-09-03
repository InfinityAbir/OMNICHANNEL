# ADR-0001: Modular monolith with Clean Architecture boundaries

**Status:** Accepted
**Date:** 2026-09-03

## Context

The PRD's core value is a single unified inbox across channels with a controlled AI decision
pipeline, for SME customers. At MVP stage there is no demonstrated scale requirement that
justifies distributed-systems complexity, and AGENTS.md explicitly forbids introducing
microservices, a message broker, distributed transactions, or separate operational databases
without a documented, demonstrated constraint.

## Decision

Build a single deployable ASP.NET Core host (`Omnichannel.Api`) backed by a Clean Architecture
layering:

```
Omnichannel.Domain          (no dependencies)
Omnichannel.Application  -> Domain
Omnichannel.Infrastructure -> Application
Omnichannel.Api          -> Application, Infrastructure, Contracts
Omnichannel.Contracts        (shared DTOs; no dependency on Domain internals)
```

Dependencies point inward only. Domain has zero framework references. Provider-specific code
(channel adapters, AI vendor SDKs) lives in Infrastructure, never in Domain or Application.

## Alternatives considered

- **Microservices per channel/module from day one.** Rejected: adds operational overhead
  (service discovery, distributed tracing, network failure modes, deployment complexity) with
  no current load that requires it. Revisit if a specific module (e.g. AI processing, webhook
  ingestion) demonstrably needs independent scaling.
- **Single project, no layering.** Rejected: PRD explicitly requires Clean Architecture
  boundaries so domain logic stays independent of HTTP/DB/provider-SDK/AI-vendor details, and so
  a future Android client can consume the same API contracts without Angular-specific coupling.

## Consequences

- Fast local iteration, one deployable artifact, one database, simpler transactions.
- Module boundaries are enforced by project references, not network calls — cheap to keep
  correct, cheap to violate if reviewers aren't careful. Code review must check dependency
  direction on every PR that touches Domain/Application.
- If a component later needs independent scaling or deployment cadence, extracting it means
  promoting an internal interface (e.g. `IChannelGateway`) to a network boundary — the interface
  already exists at the Application/Infrastructure seam, so extraction is additive, not a rewrite.
