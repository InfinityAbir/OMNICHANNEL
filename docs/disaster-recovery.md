# Disaster Recovery Plan

PRD §74 (Phase 15): disaster recovery plan, backup/restore test, database failure test. No
hosting target is chosen yet (`docs/deployment.md`), so this is a plan to execute once one is —
written and validated against the local/CI Postgres now, not deferred until it's needed.

## Backup strategy

- **What**: full logical backup via `pg_dump -F c` (custom format — compressed, supports
  selective/parallel restore, the Postgres-recommended format for this scale).
- **Cadence** (once hosted): daily automated snapshot + point-in-time recovery via the hosting
  provider's managed Postgres backup feature (e.g. RDS/Cloud SQL/Azure Database for PostgreSQL
  automated backups), retained per `docs/privacy.md`'s retention policy. A managed provider's
  continuous WAL archiving is strictly better than a cron'd `pg_dump` for RPO — use it over a
  hand-rolled backup job once a provider is chosen, rather than building custom backup tooling
  now for a target that doesn't exist yet.
- **Secrets**: `Jwt:SigningKey`, AI provider keys, SMTP credentials, and the Data Protection key
  ring (used to encrypt channel credentials at rest, ADR-0016) all live outside the database —
  back these up separately, through the secrets manager itself (its own backup/versioning), not
  via the database backup.

## Restore procedure (validated this phase)

1. `pg_dump -U <user> -d <db> -F c -f backup.dump`
2. `createdb <target-db>`
3. `pg_restore -U <user> -d <target-db> backup.dump`
4. Verify row counts on a handful of key tables against the source before cutting over.

**Actually executed against the live local/CI-equivalent database this phase** (not just
documented): backed up the dev database (972 conversations, 936 messages, 2519 tenants, 4 roles
accumulated from the session's own test runs), restored into a fresh database, and confirmed
every one of those counts matched exactly. See `docs/phase-reports/phase-15.md` for the exact
numbers. This is the same three-command procedure a real incident response would use — validated
now, not assumed to work.

## Database failure handling (today, pre-hosting)

- `GET /health/ready` fails (`AddNpgSql` health check) whenever Postgres is unreachable — an
  orchestrator (k8s/ECS/etc., once one exists) can use this to stop routing traffic to an instance
  that can't reach its database, rather than serving 500s.
- Every EF Core write goes through `AppDbContext.SaveChangesAsync` — a lost connection surfaces as
  a typed `Npgsql`/`DbUpdateException`, caught by the global `ProblemDetailsExceptionHandler`
  (`src/Omnichannel.Api/Middleware`) and returned as a generic, sanitized 500 — never a raw
  connection string, stack trace, or internal exception detail to the client.
- No automatic failover/read-replica routing exists yet — single-instance Postgres via
  `docker-compose.yml` locally and in CI. A managed provider's built-in HA (multi-AZ standby,
  automatic failover) is the intended production answer, not custom application-level retry logic
  around a single primary.

## Recovery objectives (targets to hold hosting infra to, once chosen)

- **RPO** (acceptable data loss): ≤ 24 hours with daily backups alone; near-zero with a managed
  provider's continuous WAL archiving / point-in-time recovery — prefer the latter.
- **RTO** (acceptable downtime): restore-from-backup alone is the procedure validated above (a
  few minutes at this data volume); a managed provider's automatic failover should bring this
  well under that once configured.

## Secret rotation

- **JWT signing key** (`Jwt:SigningKey`): rotating it invalidates every outstanding access and
  refresh token immediately (both are HMAC-signed with this one key, ADR — no per-token key id).
  Acceptable for a planned rotation (users re-authenticate, same as a forced logout); for a
  suspected-compromise rotation, that's the entire point. No overlap/grace-period mechanism exists
  today (would need a `kid`-keyed multi-key validation set) — out of scope until a real rotation
  need arrives, not built preemptively.
- **AI provider key / SMTP credentials**: both read from configuration (`dotnet user-secrets`
  locally, environment/secrets-manager in any real deployment) on process start — rotating either
  is: update the secret store, restart the process. No code change, no redeploy of application
  code required.
- **Channel credentials** (WhatsApp/Instagram/Messenger tokens, per tenant): encrypted at rest via
  ASP.NET Core Data Protection (`DataProtectionChannelCredentialStore`, ADR-0016). Rotating the
  Data Protection key ring itself would make previously-encrypted credentials unreadable unless
  the old keys are retained in the ring (the framework's default behavior — keys are additive, not
  replaced) — so a rotation only needs to add a new key, never delete an old one still protecting
  live data.

## Provider outage handling (already built, not new this phase)

- **Channel providers** (WhatsApp/Instagram/Messenger): `ChannelSendService`'s Polly retry
  pipeline retries transient/rate-limited failures with exponential backoff before giving up and
  marking the message `Failed` — verified in
  `ChannelWebhookEndpointsTests.OutboundSend_RetriesTransientFailureThenSucceeds`.
- **AI provider** (Groq): every AI-calling path (`AiSuggestionService`, `AiAutoReplyService`)
  catches `AiProviderException` and falls back to "ask a human" rather than blocking the request
  or retrying indefinitely — verified in both Phase 10's and Phase 12's test suites
  (`GenerateSuggestion_ProviderFails_ReturnsServiceUnavailableNotCrash`,
  `AutoReply*_SkippedProviderUnavailable` paths).

## What's explicitly not built

- No message queue/broker exists (modular monolith, AGENTS.md) — "queue failure test" from PRD
  §74 doesn't apply; there's no queue to fail.
- No automated backup job/cron exists in this repo — intentionally deferred to the hosting
  provider's managed backup feature once one is chosen, rather than building and maintaining
  custom backup infrastructure for an undetermined target.
