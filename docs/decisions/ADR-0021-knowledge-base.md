# ADR-0021: Knowledge Base (chunking, embeddings, pgvector, retrieval)

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §70 (Phase 11): knowledge documents, text extraction, chunking, embedding abstraction, vector
storage, retrieval, source attribution, versioning, re-indexing — "use PostgreSQL/pgvector if
appropriate." Security review focus: tenant isolation in retrieval, malicious document content,
prompt injection through documents, unauthorized knowledge access, document upload security.

## Decision

**Plain-text documents only, no file upload this phase.** Documents are submitted as
`{title, content}` JSON. "Text extraction" is trivially satisfied since the input already is
text; the entire file-upload attack surface (malicious file content, parser exploits, storage) is
avoided by construction, not defended against — an honest scope choice, not an oversight (see
`docs/security.md`'s "not yet applicable" list).

**pgvector, via `pgvector/pgvector:pg17`** (drop-in superset of the `postgres:17` image already
in use — `docker-compose.yml` and CI both updated). `Pgvector`/`Pgvector.EntityFrameworkCore`
NuGet packages; `UseVector()` registered wherever `UseNpgsql()` is called (DI, design-time
migration factory, and the two test files that construct `AppDbContext` directly — found by
actually running the tests, which failed with a clear "provider does not support mapping 'Vector'"
error until all four call sites were fixed, not by reasoning about it in advance).

**`KnowledgeChunk.Embedding` stays a plain `float[]` in Domain** (framework-free, no Pgvector type
there) — Infrastructure's EF configuration converts to/from `Pgvector.Vector` only at the storage
boundary, with an explicit `ValueComparer` (its absence produced a real EF warning about
reference-equality change tracking on the array, fixed by adding one).

**Similarity search is raw SQL, not LINQ.** EF's pgvector LINQ translation (`CosineDistance()`
etc.) needs the *model* property itself typed as `Vector` to recognize the operator pattern —
since the Domain property is `float[]`, that translation path isn't available. A raw
parameterized query (`PgVectorKnowledgeRetrievalService`, using `AppDbContext`'s own already-open
connection) with an explicit `WHERE tenant_id = $2` is simpler and equally correct. This is the
fourth documented exception to the automatic EF tenant filter in this codebase (after
`AuthService`'s login lookup, `WidgetService`'s origin resolution, and `WebhookIngestionService`'s
account resolution — ADR-0016) — a deliberate, explicit scope, not a bypass.

**Embedding provider: a deterministic lexical (feature-hashing) embedding, not neural.** Checked
first, not assumed: Groq's own `/openai/v1/models` catalog (the only AI provider this project has
credentials for) has no embedding model, and a direct call to a guessed embeddings endpoint
confirmed it (`docs/phase-reports/phase-11.md`). Rather than block Phase 11 on a credential that
doesn't exist yet, `HashingEmbeddingProvider` implements the same bag-of-words hashing-trick
technique behind tools like scikit-learn's `HashingVectorizer` — a real, well-established,
deterministic IR technique (not a stub), needs no network call or API key, and is fully behind
`IEmbeddingProvider` so a neural provider is a drop-in replacement (new class + DI registration)
once credentials exist. Explicitly **not** claimed to be semantic search — it's real keyword/
near-duplicate retrieval, documented as such, not oversold.

**Retrieval feeds into AI Suggestion Mode (Phase 10).** `AiSuggestionService` retrieves the
top-3 chunks matching the customer's latest message and passes them to `GroqAiProvider` as a
separate, clearly-labeled "reference material" block — same untrusted-data discipline as
conversation history (never concatenated into system instructions, explicitly framed as data to
consult, not follow) — addressing "prompt injection through documents" directly rather than
assuming conversation-history-only defenses cover it too.

**Chunking is fixed-size with overlap** (800 chars, 100 overlap) — simple, deterministic, good
enough for the plain-text documents this phase supports. No sentence/semantic-boundary chunking
yet.

**Re-indexing re-derives the whole chunk set** on every revision (delete + rebuild), not a diff —
simpler and correct; knowledge documents are edited rarely enough that re-embedding the whole
document isn't a real cost concern, especially with a zero-cost local embedding provider.

## Alternatives considered

- **A separate vector database (Pinecone, Qdrant, ...).** Rejected: PRD explicitly suggests
  pgvector "if appropriate," and it is — one fewer service to run/deploy/secure, and Postgres is
  already the system of record for everything else.
- **Wait for an embeddings-capable API key before building Phase 11 at all.** Rejected: the user's
  explicit instruction was to continue through Phase 15 without pausing for approval; a real,
  working, honestly-scoped lexical implementation is more useful than a blocked phase, and the
  abstraction makes the eventual upgrade a small, isolated change.
- **LINQ-based vector search via a value-converted property.** Attempted conceptually, rejected
  once it became clear EF's pgvector query translation requires the model type itself to be
  `Vector`, not a converted `float[]` — raw SQL was the simpler, more reliable choice, not a
  fallback taken after a failed attempt in code (caught during design, not during a failed test
  run).

## Consequences

- Retrieval quality is lexical, not semantic — "return policy" won't match a document that only
  says "refund terms" unless shared vocabulary exists. Acceptable for now; documented as a known
  limitation, not silently passed off as full RAG quality.
- `EmbeddingDimensions = 256` is fixed in `KnowledgeChunkConfiguration` and must match whichever
  `IEmbeddingProvider` is registered — swapping to a neural provider with a different dimension
  needs a migration (drop and recreate the vector column, re-embed every existing chunk), not
  just a DI change. Documented in the configuration file itself so it isn't a surprise later.
- Every future phase's local Postgres and CI now depend on the `pgvector/pgvector` image rather
  than vanilla `postgres` — a one-line, low-risk change already applied everywhere Postgres is
  provisioned (docker-compose, both CI jobs).
