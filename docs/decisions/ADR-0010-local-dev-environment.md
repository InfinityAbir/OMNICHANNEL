# ADR-0010: Docker Compose for local Postgres

**Status:** Accepted
**Date:** 2026-09-03

## Context

PRD §83 recommends Docker Compose with PostgreSQL (+ optional pgAdmin, optional Ollama) for
local development, with the application itself run from the IDE/CLI rather than containerized.

## Decision

`docker-compose.yml` at repo root runs only PostgreSQL 17 by default. pgAdmin and Ollama services
are present but commented out — uncomment as needed rather than running unused containers by
default. `Omnichannel.Api` and the Angular dev server run directly on the host (`dotnet run`,
`ng serve`), not in containers, for fast iteration.

## Alternatives considered

- **Fully containerized dev environment (API + Angular + Postgres all in Compose).** Rejected
  for Phase 0: slower inner-loop (rebuild-on-change friction) with no current benefit; local
  .NET/Node tooling is already confirmed present and working. Revisit only if environment drift
  across contributors' machines becomes a real problem.

## Consequences

- Contributors need Docker Desktop (or an equivalent Docker engine) running locally, plus the
  .NET 10 SDK and Node/npm for the app processes themselves.
- `.env.example` documents the connection string shape; real `.env` is gitignored.
- CI (`.github/workflows/ci.yml`) runs a Postgres service container with matching credentials so
  the same connection string works locally and in CI without per-environment branching.
