# ADR-0028: Render Deployment — Single Combined Service, DB-Persisted Data Protection Keys

**Status:** Accepted
**Date:** 2026-09-04

## Context

The user chose [Render](https://render.com) as the hosting target (the third of their three
stated post-launch tasks, after the admin UI/dynamic-config work and before JWT key rotation/data
retention, though this landed first since the user was actively on the Render dashboard asking how
to proceed). The repo had no Dockerfile, no deployment config, and `docs/deployment.md` was still
a Phase-0 stub — none of this existed yet.

## Decision

**One Docker image serves both the API and the Angular SPA — not a separate static site.** The
frontend calls the API via relative `/api/...` paths (see `web/proxy.conf.json`'s local-dev
equivalent), which only resolves correctly if both are same-origin in production. A
two-service split (Render Static Site + Web Service) would require baking an absolute API URL
into the Angular build, adding a CORS allowlist entry, and handling cross-origin SignalR
negotiation for the realtime hubs. Serving Angular's `dist/web/browser` output as static files
from the same ASP.NET Core process (multi-stage `Dockerfile`: Angular build → .NET publish →
ASP.NET Core runtime, `Program.cs`'s `app.MapFallbackToFile("index.html")` after every API/hub
route mapping) avoids all of that — one Render service, one origin, zero new CORS surface.

**`render.yaml` (a Render Blueprint), not manual dashboard clicks.** Defines the Postgres database
and the web service together as versioned, reviewable config — matching this project's existing
discipline of nothing being a hand-clicked one-off. Secrets requiring a real value
(`Ai__Groq__ApiKey`, `Smtp__*`) are marked `sync: false` so Render prompts for them without ever
storing a real value in the repo; `Jwt__SigningKey` uses Render's `generateValue: true` so a
strong key is generated automatically and never has to be chosen or transmitted by a human.

**Data Protection's key ring moved from the container filesystem to Postgres
(`EfXmlRepository`/`data_protection_keys` table).** This is the one correctness-critical finding
from this deploy prep: Render's (and most PaaS/container platforms') filesystem is ephemeral, so
the default file-system key storage would silently mint a fresh key ring on every redeploy or
restart — permanently stranding every `TenantSecret` (AI provider keys, SMTP passwords, ADR-0027)
and `ChannelCredential` (ADR-0016) encrypted under the old ring, with no error, just silent
undecryptable garbage from that point on. `EfXmlRepository : IXmlRepository` opens its own DI
scope per call via `IServiceScopeFactory` (Data Protection can call it before any request scope
exists), matching Microsoft's own documented pattern for a custom key repository.
`ApplicationName` is pinned to a fixed literal (`"Omnichannel"`) — Data Protection scopes keys to
it, so an accidental change would have the identical effect to losing the key ring outright.

**`ForwardedHeaders` middleware, required for Render's TLS-terminating proxy.** Render (like
almost every PaaS) terminates HTTPS at its edge and forwards plain HTTP to the container; without
`UseForwardedHeaders()` reading `X-Forwarded-Proto`, Kestrel sees every request as HTTP, so the
existing `UseHttpsRedirection()`/`UseHsts()` would redirect-loop real HTTPS traffic forever.
`KnownNetworks`/`KnownProxies` are cleared rather than allowlisted to a fixed IP — Render's edge
isn't a fixed, allowlist-able address, so the header is trusted by topology (the container only
ever receives traffic from the platform's own edge) rather than by network origin.

**Migrations apply on startup, opt-in via `RunMigrationsOnStartup`, defaulted `true` in
`render.yaml`.** The pre-existing code only auto-migrated in Development/Testing, deliberately
requiring a reviewed manual step elsewhere (a documented Phase 15 decision) — but a hosted
platform like Render has no interactive terminal to run `dotnet ef database update` from between
deploys. Rather than blanket-enabling migration-on-startup for every non-dev environment (which
would silently apply schema changes on ANY production-like deploy, including ones where that's not
wanted), a new explicit `RunMigrationsOnStartup` config flag keeps the opt-in nature intact while
making the common single-instance-platform case (this one) work without a separate step.
`Database.MigrateAsync()` is idempotent — it only applies migrations not already recorded as run —
so leaving this on by default for Render is safe.

**`Jwt:SigningKey` startup check now rejects an empty string, not just a missing key.** Found
while live-verifying the Docker image: the original `jwtSection["SigningKey"] ?? throw` only
guards against `null`; an empty string passed it, then failed later — `SymmetricSecurityKey`
rejects a zero-length key only when `JwtBearerOptions` is first lazily resolved (the first
authenticated request), surfacing as an unhandled 500 instead of a clear startup failure. Fixed to
`string.IsNullOrWhiteSpace`.

## Consequences

- Verified live, not just built: a full `docker build` of the actual `Dockerfile`, run against
  local Postgres, registering a tenant, saving an encrypted AI provider key, then **destroying
  that container and starting a fresh one from the same image** (a true redeploy simulation, not
  a `docker restart` which reuses the same filesystem) — the new container correctly decrypted the
  key saved by the old one (`POST .../provider-settings/test` returned a clean network-layer
  failure against the fake test URL, not a decryption exception, proving `Unprotect` succeeded).
- No horizontal scaling story yet — a single Render instance is sufficient for current traffic;
  the Phase 15 in-memory rate limiter would need a shared store (Redis) if this ever runs as more
  than one instance behind a load balancer (already documented as a known Phase 15 consequence).
- No automated backup job configured yet (Render's managed Postgres backup is the documented
  future path, `docs/disaster-recovery.md`) and no custom domain (Render's default `*.onrender.com`
  URL for now) — both are Render-dashboard/billing decisions, not blocked on code.
- Full backend suite (274/274) re-verified green after all of the above changes; no test behavior
  changed, since none of this touches request-handling logic exercised by
  `WebApplicationFactory`-based tests (`ForwardedHeaders`/Data-Protection-storage/migration-timing
  are all outside what those tests configure).
