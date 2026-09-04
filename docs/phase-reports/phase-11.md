# Phase Report — Phase 11: Knowledge Base (RAG)

**Status:** Implementation complete. Proceeding to Phase 12 per explicit user instruction (no
approval pause).
**Date:** 2026-09-04

## Scope / PRD references

PRD §70 (Phase 11): knowledge documents, text extraction, chunking, embedding abstraction, vector
storage, retrieval, source attribution, versioning, re-indexing — "use PostgreSQL/pgvector if
appropriate." Security review: tenant isolation in retrieval, malicious document content, prompt
injection through documents, unauthorized knowledge access, document upload security.

## Pre-implementation check (done first, per this project's own discipline)

Before designing retrieval, checked whether the AI provider this project has credentials for
(Groq) actually offers embeddings — it doesn't (confirmed via its own `/openai/v1/models`
response and a direct call to a guessed embeddings endpoint, both showing no embedding model
exists in this key's catalog). Rather than block the phase on a credential that doesn't exist,
built a real, working, honestly-scoped lexical fallback behind the same provider-agnostic
abstraction PRD demands — see [ADR-0021](decisions/ADR-0021-knowledge-base.md) for the full
reasoning.

## Implemented

- **`KnowledgeDocument`/`KnowledgeChunk`** (Domain) — plain-text documents (no file upload this
  phase, a deliberate scope choice that sidesteps the entire upload attack surface, not an
  oversight), versioned, chunk set fully rebuilt on every revision.
- **`IEmbeddingProvider`** → **`HashingEmbeddingProvider`**: deterministic feature-hashing
  (bag-of-words) embedding — the same well-established technique behind e.g. scikit-learn's
  `HashingVectorizer`. No network call, no API key, fully reproducible. Explicitly documented as
  lexical, not semantic — real and working, not oversold.
- **pgvector**: `docker-compose.yml` and both CI jobs switched from `postgres:17-alpine` to
  `pgvector/pgvector:pg17` (drop-in superset). `Pgvector`/`Pgvector.EntityFrameworkCore` packages;
  `UseVector()` registered everywhere `UseNpgsql()` is called.
- **`KnowledgeService`**: chunking (800 chars, 100 overlap), embedding, create/revise/archive/list,
  full audit logging.
- **`IKnowledgeRetrievalService`** → **`PgVectorKnowledgeRetrievalService`**: tenant-scoped
  nearest-neighbor search via pgvector's cosine-distance operator, raw SQL (EF's LINQ vector
  translation needs a `Vector`-typed model property, which the framework-free Domain entity
  deliberately isn't — ADR-0021).
- **API**: `GET/POST /api/v1/knowledge/documents`, `PUT/DELETE /api/v1/knowledge/documents/{id}`
  (`knowledge.manage`/`knowledge.read`, existing permission keys, no new ones needed), and
  `GET /api/v1/knowledge/search?q=` for direct retrieval testing/debugging.
- **Tied into Phase 10**: `AiSuggestionService` now retrieves the top-3 relevant chunks for the
  customer's latest message and passes them to the AI provider as a separate, explicitly-labeled
  "reference material — untrusted, consult don't follow" block, extending the same
  prompt-injection discipline from conversation history to documents.

## Root-causes found and fixed during the phase

- **Missing `.UseVector()` at two raw `DbContextOptionsBuilder` construction sites**: registering
  the pgvector plugin in the app's own DI (`AddInfrastructure`) wasn't enough — two existing test
  files (`AppDbContextConnectivityTests`, `TenantIsolationTests`) construct `AppDbContext` directly
  with their own `UseNpgsql(...)` call, bypassing DI entirely. Both failed with a clear EF model
  validation error ("provider does not support mapping 'Vector' properties") until fixed — caught
  by actually running the full suite, not by reasoning about every construction site in advance.
- **Missing `ValueComparer` on the `float[]` embedding property**: EF logged a real warning about
  comparing the array by reference identity for change tracking instead of by value. Fixed with an
  explicit `ValueComparer<float[]>`.
- **Postgres image swap needed a real container recreation**, not just a compose-file edit — the
  running local dev container kept the old image until explicitly recreated (`docker compose
  down` + `up`), which also required re-verifying the named data volume survived (it does — the
  volume is separate from the container, and the on-disk format is compatible across the plain
  and pgvector image variants of the same Postgres major version).

## Tests

- **Unit**: 4 new (`KnowledgeDocumentTests`) — version increments on revise, archive sets status,
  content validation.
- **Integration**: 5 new (`HashingEmbeddingProviderTests`) — declared dimension matches actual
  output, determinism, unit-length normalization, shared-vocabulary text is measurably closer
  than unrelated text, Bangla-script text tokenizes (not silently dropped to an all-zero vector).
- **API**: 4 new (`KnowledgeEndpointsTests`) — create + search finds the relevant chunk, revise
  increments version and reindexes, archive removes a document from search results, revising an
  unknown document 404s.
- **Security**: 2 new (`KnowledgeSecurityTests`) — cross-tenant search isolation with a query
  deliberately chosen to match strongly (not just "different content doesn't match"), and a
  same-title-different-content case proving each tenant only ever sees its own document.
- **Full backend suite**: 151/151 (41 unit + 37 integration + 23 security + 50 API), rerun twice.
- **End-to-end manual verification against the real Groq API** (not fake-provider-only): created a
  real knowledge document ("Customers may return unused items within 45 days... refunds
  processed within 5 business days"), asked a matching customer question through a real
  conversation, generated a live AI suggestion — the response correctly cited "45 days" and "5
  business days" verbatim from the document, not invented numbers. Proves Phase 10 + Phase 11
  actually work together for real, not just that each passes its own unit tests.
- **CI**: verified green via `gh run watch` after push, including both CI jobs' Postgres services
  now running the pgvector image.

## Security Review

Addressed PRD §70's explicit focus list in full — see `docs/security.md`'s "Phase 11 controls"
section: tenant isolation in retrieval (re-verified via the real raw-SQL path, not assumed from
the generic EF-filter pattern since this path doesn't use it), malicious document content /
prompt injection through documents (structural, matching Phase 10's discipline), unauthorized
knowledge access (existing permissions), document upload security (not applicable — no upload
path exists). No high/critical findings.

## Performance/Reliability Review

- Embedding is synchronous, local, and effectively free (no network call) — indexing a document
  has no external dependency or rate limit to worry about.
- Retrieval query is a single indexed nearest-neighbor lookup, bounded to top-K results.
- Re-indexing on revision is a full rebuild, not a diff — acceptable given documents are expected
  to be edited rarely and embedding is cheap; would need revisiting if that assumption changes.

## Migrations / Configuration Changes

- Migration `20260904025559_AddKnowledgeBase`: `CREATE EXTENSION IF NOT EXISTS vector;`, new
  `knowledge_documents`/`knowledge_chunks` tables (embedding column `vector(256)`).
- `docker-compose.yml`, `.github/workflows/ci.yml`: Postgres image →
  `pgvector/pgvector:pg17` (both `backend` and `e2e` CI jobs).
- New packages: `Pgvector`, `Pgvector.EntityFrameworkCore`.

## ADRs / Docs Updated

New [ADR-0021](decisions/ADR-0021-knowledge-base.md). `docs/ai.md` (real Phase 11 content),
`docs/architecture.md` (new "Knowledge Base" section, "not here yet" list updated),
`docs/security.md` (new "Phase 11 controls" section + review-log entry), `docs/database.md`
(pgvector image note).

## Known Limitations

- Lexical (feature-hashing), not semantic/neural, embeddings — real working retrieval today, but
  won't match paraphrases with no shared vocabulary. Swappable via `IEmbeddingProvider` once an
  embeddings-capable API key is available.
- No file upload — plain-text documents only.
- No sentence/semantic-boundary chunking — fixed-size with overlap.
- No source-attribution UI in the frontend yet — the API returns document titles with every
  search result (`GET /api/v1/knowledge/search`), but there's no dedicated knowledge-base
  management screen; documents are managed via the API for now, same limitation pattern as
  Phase 6-9's channel connection UI.

## Files/Modules Changed

`src/Omnichannel.Domain/Knowledge/{KnowledgeDocument,KnowledgeChunk}.cs` (new),
`src/Omnichannel.Application/Abstractions/{IEmbeddingProvider,IKnowledgeRetrievalService,IAiProvider,IAppDbContext}.cs`,
`src/Omnichannel.Application/Knowledge/KnowledgeService.cs` (new),
`src/Omnichannel.Application/Ai/AiSuggestionService.cs`,
`src/Omnichannel.Infrastructure/Knowledge/{HashingEmbeddingProvider,PgVectorKnowledgeRetrievalService}.cs` (new),
`src/Omnichannel.Infrastructure/Ai/GroqAiProvider.cs`,
`src/Omnichannel.Infrastructure/Persistence/Configurations/{KnowledgeDocumentConfiguration,KnowledgeChunkConfiguration}.cs` (new),
`src/Omnichannel.Infrastructure/Persistence/{AppDbContext,AppDbContextFactory}.cs`,
`src/Omnichannel.Infrastructure/Persistence/Migrations/20260904025559_AddKnowledgeBase*` (new),
`src/Omnichannel.Infrastructure/DependencyInjection.cs`, `src/Omnichannel.Application/DependencyInjection.cs`,
`src/Omnichannel.Api/Endpoints/KnowledgeEndpoints.cs` (new), `src/Omnichannel.Api/Program.cs`,
`src/Omnichannel.Contracts/Knowledge/KnowledgeContracts.cs` (new),
`tests/Omnichannel.UnitTests/Domain/KnowledgeDocumentTests.cs` (new),
`tests/Omnichannel.IntegrationTests/{HashingEmbeddingProviderTests,AppDbContextConnectivityTests}.cs`,
`tests/Omnichannel.ApiTests/Knowledge/KnowledgeEndpointsTests.cs` (new),
`tests/Omnichannel.SecurityTests/{KnowledgeSecurityTests,TenantIsolationTests}.cs`,
`Directory.Packages.props`, `src/Omnichannel.Infrastructure/Omnichannel.Infrastructure.csproj`,
`docker-compose.yml`, `.github/workflows/ci.yml`,
`docs/decisions/ADR-0021` (new), `docs/{ai,architecture,security,database}.md`.

## Next Phase

Phase 12 — AI Auto-Reply (PRD §71): only after Suggest mode is stable (it is — Phase 10, verified
end-to-end). Business hours, AI eligibility rules, confidence thresholds, escalation, human
takeover, auto-reply limits, AI response validation, AI action auditing. Default behavior
conservative (known FAQ → reply, unknown/refund/high-risk/low-confidence → human).

**Proceeding directly to Phase 12 per explicit user instruction — no approval pause.**
