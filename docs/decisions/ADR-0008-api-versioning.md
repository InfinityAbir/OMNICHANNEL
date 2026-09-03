# ADR-0008: URL-segment API versioning (`/api/v1/...`)

**Status:** Accepted
**Date:** 2026-09-03

## Context

PRD §41/§42 require API versioning and list example routes under `/api/v1/...`. The API is
public and mobile-consumable (future Android client per PRD §7/§89), so the versioning scheme
must be simple to reason about from any HTTP client, not just Angular's `HttpClient`.

## Decision

Use URL-segment versioning via `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer`:
`/api/v{version}/...`, default version `1.0`, `AssumeDefaultVersionWhenUnspecified = true` so
unversioned calls during early development don't hard-fail, `ReportApiVersions = true` so
clients can discover supported versions via response headers.

No versioned endpoints exist yet in Phase 0 (only unversioned `/health/live`, `/health/ready`,
which are operational, not public API surface and intentionally excluded from versioning). The
first versioned routes land in Phase 1 with auth/tenancy endpoints.

## Alternatives considered

- **Header-based versioning** (`Api-Version` header). More "correct" REST-purist option but
  harder to explore/debug/curl casually, and PRD's own example routes already show the URL-segment
  style — matching it avoids a documentation mismatch.
- **Query-string versioning.** Rejected: easy to omit accidentally, doesn't compose well with
  caching.

## Consequences

- Every future controller/endpoint group under `/api/` must declare an explicit `ApiVersion`.
- OpenAPI/document generation per version is deferred until Phase 1 has real endpoints to
  document — Phase 0 intentionally does not wire `Microsoft.AspNetCore.OpenApi` together with
  versioning yet (the `Asp.Versioning` analyzer flags that combination as needing a
  `MapOpenApi().WithDocumentPerVersion()` setup that has no versioned endpoints to describe yet).
