# Deployment

## Environments

Development, Testing, Staging, Production (PRD §82). Never use production credentials in
development; never use real customer data in tests unless anonymized and authorized.

## Production: Render

Hosting target: [Render](https://render.com). One Docker web service serves both the API and the
built Angular SPA from a single origin (see `Dockerfile` and `Program.cs`'s
`app.MapFallbackToFile("index.html")`) — no separate static site, no cross-origin CORS/SignalR
config needed between frontend and backend. One managed Postgres database. Both are defined
together in `render.yaml` (a Render "Blueprint") so they're provisioned from one file rather than
clicked through by hand.

### First deploy

1. In the Render dashboard: **New** → **Blueprint** → connect this repo (`InfinityAbir/OMNICHANNEL`).
   Render reads `render.yaml` at the repo root and shows a plan: one Postgres database
   (`omnichannel-db`) and one web service (`omnichannel`).
2. Render prompts for the secrets marked `sync: false` in `render.yaml` before the first deploy:
   - `Ai__Groq__ApiKey` — the platform's own default Groq key (get one at
     [console.groq.com/keys](https://console.groq.com/keys)). Optional: leave blank and every
     tenant must configure their own AI provider (Settings → AI Provider) before Suggest/Auto-Reply
     works for them — see ADR-0027.
   - `Smtp__Username`, `Smtp__Password`, `Smtp__FromAddress` — the platform's own default SMTP
     account (e.g. a Gmail address with an app password). Optional, same fallback logic: leave
     blank and every tenant must configure their own SMTP (Settings → Email) before transactional
     email works for them.
   - `Jwt__SigningKey` is NOT prompted — Render generates a strong random value for it
     automatically (`generateValue: true` in `render.yaml`).
3. Click **Apply**. Render builds the Docker image (multi-stage: Angular build → .NET publish →
   ASP.NET Core runtime), provisions Postgres, and starts the web service. `RunMigrationsOnStartup`
   is `true` by default, so the app applies all EF Core migrations itself on this first boot —
   nothing to run by hand.
4. Once the service shows "Live", open its URL. `/health/live` is the health check Render polls;
   the app itself (Angular SPA) is served at `/`.

### Subsequent deploys

Push to `main` (or whatever branch the service is connected to) — Render rebuilds and redeploys
automatically. `RunMigrationsOnStartup=true` means any new EF Core migration in that push is
applied on startup, same as the first deploy.

### Rotating the JWT signing key

The signing key ring lives in Postgres (ADR-0029), not `render.yaml`'s `Jwt__SigningKey` — that
value is only ever used once, to seed the very first key on a brand-new database. To rotate (e.g.
after a suspected token compromise, or on a periodic policy):

1. Open a shell into the running `omnichannel` service (Render dashboard → the service → **Shell**).
2. Run: `dotnet Omnichannel.Api.dll --rotate-jwt-key`
3. It prints the new/retired key ids and how long the old key stays valid for (default: 1 hour —
   set `Jwt:KeyRotationOverlapHours` to change it). No redeploy, no restart — every running
   instance picks up the new key ring within ~60 seconds (`JwtSigningKeyRefreshService`'s poll
   interval), and already-issued access tokens keep working until the overlap window ends.

This deliberately has no HTTP endpoint — see ADR-0029 for why (no cross-tenant admin role exists
in this app, so rotation stays an operator-only, shell-level action).

### Why a single combined service (not a separate static site)

The Angular frontend calls the API via relative `/api/...` paths (see `web/proxy.conf.json` for
the local-dev equivalent) — same-origin in production only if both are served from the same
place. Two separate Render services (a Static Site for `web/` + a Web Service for the API) would
need an absolute API base URL baked into the Angular build, a CORS allowlist entry for the static
site's origin, and cross-origin SignalR negotiation for the realtime hubs. Serving the SPA's build
output as static files from the same ASP.NET Core process avoids all of that — one Render service,
one bill, one origin, and the existing `Cors:AllowedOrigins` config (already deny-by-default) needs
no change for this deployment shape. The widget embed (loaded on arbitrary third-party sites) is
unaffected — it already has its own, separately-justified wildcard CORS policy (`WidgetEmbed`).

### Why Data Protection keys are stored in Postgres, not the container filesystem

`ITenantSecretStore` (ADR-0027) and `ChannelCredential` (ADR-0016) both encrypt at rest via
ASP.NET Core's Data Protection API. Its default key-ring storage is the local filesystem, which on
Render (and most container platforms) is ephemeral — every redeploy or restart would otherwise
mint a fresh key ring and permanently strand every previously-encrypted secret. `EfXmlRepository`
(`src/Omnichannel.Infrastructure/Security/`) persists the key ring in the `data_protection_keys`
table instead, so it survives redeploys. Verified live: encrypted an AI provider key in one
container, killed that container entirely (not just restarted — a fresh container from the same
image, simulating a real redeploy), and confirmed a second container could still decrypt it
(`POST /api/v1/ai/provider-settings/test` returned a clean network-layer response, not a
decryption error).

### What isn't set up yet

- No automated database backup job (Render's own paid-tier managed Postgres backup is the
  documented path once budget allows — see `docs/disaster-recovery.md`).
- No custom domain configured (Render issues a `*.onrender.com` URL by default; adding a custom
  domain is a Render dashboard step, not a code change).
- No horizontal scaling — a single instance is enough for the current traffic level; the global
  rate limiter (Phase 15) is in-memory and would need a shared store (Redis) if this ever runs as
  more than one instance behind a load balancer.

## Local development

```bash
cp .env.example .env        # fill in values, never commit .env
docker compose up -d postgres
dotnet build
dotnet test
cd web && npm ci && ng serve
```

API: http://localhost:5068 (http profile) — see `src/Omnichannel.Api/Properties/launchSettings.json`.
Angular: http://localhost:4200.
