# Omnichannel

**One inbox for every customer conversation, with an AI assistant that helps when your team can't.**

## What this is

Omnichannel is a SaaS platform for small and medium-sized businesses that unifies customer
conversations from WhatsApp, Instagram, Facebook Messenger, website chat — and future channels —
into a single inbox. It pairs that inbox with an AI assistant that can answer approved customer
questions when staff are unavailable, follows business-specific rules and knowledge, recognizes
when a conversation needs a human, and hands it over cleanly.

## Why we're building it

Small business owners today juggle several separate apps to talk to customers. Messages get
missed, customer history is scattered across platforms, replies are delayed outside business
hours, and there's no single view of what still needs attention. Omnichannel exists so that an
owner doesn't need to care which app the customer used — the message arrives, the business sees
it in one place, the AI helps when appropriate, and a human takes over when it matters. Every
action stays secure, auditable, and traceable.

## Who benefits

- **Business owners** get one place to see every conversation, configure how the AI behaves, and
  review team and AI performance — instead of checking four apps and losing track of who
  answered what.
- **Support agents** work from one inbox instead of switching apps, with full customer history
  and the ability to take over from the AI at any time.
- **Business administrators** manage integrations, team access, and security/audit settings from
  one place.
- **Customers** get faster answers (including outside business hours) through whichever channel
  they already use, without repeating themselves, and get transferred to a human when the
  question needs one.

## Who this is for

Small and medium-sized businesses that field customer messages across multiple channels — retail,
services, e-commerce sellers, and similar — and want fewer missed conversations without hiring a
24/7 support team. Not built as a CRM replacement, an e-commerce platform, or a marketing
automation tool — see `OMNICHANNEL_PRD.md` §4.3 for what's explicitly out of scope for the MVP.

## How it's built

- **Backend:** .NET 10, ASP.NET Core, C#, EF Core, PostgreSQL — Clean Architecture, modular
  monolith (see [`docs/architecture.md`](docs/architecture.md) and
  [`docs/decisions/`](docs/decisions/) for the reasoning).
- **Frontend:** Angular (workspace in [`web/`](web/)), strict TypeScript.
- **API-first:** the same backend contracts will serve a future Android/iOS client without
  Angular-specific coupling.
- **Security and multi-tenancy are non-negotiable invariants**, not features bolted on later —
  see [`docs/security.md`](docs/security.md).

Development follows a strict phase-by-phase plan (`OMNICHANNEL_PRD.md`) with a security review,
test run, and explicit approval gate after every phase — see `AGENTS.md` for the engineering
rules this project is held to.

## Local development

Prerequisites: .NET 10 SDK, Node 24+/npm, Docker (for local PostgreSQL).

```bash
# 1. Environment
cp .env.example .env   # never commit the real .env

# 2. Database
docker compose up -d postgres

# 3. Backend
dotnet build
dotnet test
dotnet run --project src/Omnichannel.Api   # http://localhost:5068

# 4. Frontend
cd web
npm ci
ng serve                                    # http://localhost:4200

# 5. End-to-end tests (starts the API + Angular dev server itself)
cd ../e2e
npm ci
npx playwright install chromium
npx playwright test
```

See [`docs/deployment.md`](docs/deployment.md) for environment details and
[`docs/troubleshooting.md`](docs/troubleshooting.md) for common local-dev issues.

## Documentation

| Doc | Covers |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | System shape, layering, request pipeline |
| [`docs/security.md`](docs/security.md) | Security baseline, living review record |
| [`docs/database.md`](docs/database.md) | Schema conventions, tenancy, migrations |
| [`docs/api.md`](docs/api.md) | API versioning, error contract, endpoints |
| [`docs/integrations.md`](docs/integrations.md) | Channel adapter plan (WhatsApp, Instagram, Messenger, web chat) |
| [`docs/ai.md`](docs/ai.md) | AI assistant design constraints and safety rules |
| [`docs/deployment.md`](docs/deployment.md) | Environments, local dev |
| [`docs/troubleshooting.md`](docs/troubleshooting.md) | Common issues |
| [`docs/decisions/`](docs/decisions/) | Architecture Decision Records |

## Status

Phase 7 (WhatsApp Integration) — see the phase reports under `docs/phase-reports/` for what's
been built, reviewed, and approved so far.
