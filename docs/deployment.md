# Deployment

## Environments

Development, Testing, Staging, Production (PRD §82). Never use production credentials in
development; never use real customer data in tests unless anonymized and authorized.

## Phase 0 state

No deployment pipeline beyond CI build/test yet (`.github/workflows/ci.yml`). No hosting target
chosen. This section gets filled in when a phase actually requires deploying somewhere
(realistically no earlier than after Phase 4–5, once there's a usable product).

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
