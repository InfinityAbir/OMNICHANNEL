# Phase Report — Phase 16: Admin UI + Dynamic Tenant Configuration

**Status:** Implementation complete.
**Date:** 2026-09-04

Not a PRD-numbered phase — post-launch work requested directly by the user after the 16-phase
build (Phase 0-15) completed: "What next?" surfaced three known gaps (`docs/phase-reports/
phase-15.md`'s own "Known Limitations"); the user chose the admin UI first, then extended the
scope mid-task to per-tenant AI provider and SMTP configuration.

## Scope

1. A settings/admin UI for every phase's previously API-only feature (channels, knowledge base,
   AI auto-reply, automation rules, business hours, saved replies, analytics) — the user's
   explicit requirement: "everything dynamic... should not need to change code, nothing will be
   hardcoded."
2. Per-tenant AI provider configuration (any OpenAI-compatible vendor or Anthropic, not a fixed
   list of 3), auto-detected from a pasted API key while staying editable, encrypted at rest, with
   a test-connection check.
3. Per-tenant SMTP configuration (a business's own mail server instead of the platform default),
   same encryption/auto-fill/test-connection treatment.

## Approach

See [ADR-0026](decisions/ADR-0026-admin-ui.md) (admin UI: permission-driven nav, no hardcoded
menu, shared editor/style components) and [ADR-0027](decisions/ADR-0027-dynamic-tenant-configuration.md)
(generic `TenantSecret`/`OpenAiCompatibleProvider` + one `AnthropicProvider` for the real outlier,
resolve-with-fallback so every tenant gets a working default without configuring anything).

## Implemented / Verified

**Admin UI** (ADR-0026): `SettingsPageComponent` shell + 9 lazy-loaded screens (channels,
knowledge, AI auto-reply, AI provider, email/SMTP, automation, business hours, saved replies,
analytics), a shared `BusinessHoursEditorComponent` reused across two independent backend configs,
a global `ToastService`/`ToastHostComponent` notification system, a polling notification bell.
Verified live in the browser end-to-end for every screen (see ADR-0026's own account).

**Dynamic AI provider config** (ADR-0027): `TenantSecret`/`ITenantSecretStore`/
`DataProtectionTenantSecretStore` (Data Protection API encryption, generalized from
`ChannelCredential`'s ADR-0016 pattern); `TenantAiProviderSettings`; `OpenAiCompatibleProvider` +
`AnthropicProvider` behind the unchanged `IAiProvider` interface; `IAiProviderResolver` (tenant's
own provider if configured, else the platform default — zero pre-existing AI tests broke);
`IAiProviderDetector` (key-prefix heuristics + a real live `/models` call, verified against a real
Groq key end-to-end in the browser: paste key → Auto-detect → 14 real models found → Save → Test
connection → "Connected successfully"). 5 new endpoints under `/api/v1/ai/provider-settings`.

**Dynamic SMTP config** (ADR-0027): `TenantEmailSettings`; `SmtpEmailSender` rewritten to resolve
tenant config + fallback to the platform default per-call; `IEmailSender`'s interface gained an
explicit `tenantId` parameter (breaking change, 3 call sites updated: registration, password
reset, automation-rule escalation notify). 4 new endpoints under `/api/v1/tenant/email-settings`.
Verified live: an SMTP provider preset (Gmail/Outlook/Yahoo/Zoho/SendGrid/Amazon SES/Mailgun,
"Custom" for anything else) auto-fills host/port and stays editable; save round-trips through a
real PUT/GET cycle (confirmed via network trace, not just a UI screenshot); the "Using your own
SMTP" / "Using platform default" badge updates correctly; Clear reverts to the platform default.

**Bugs found and fixed during this session's own live verification** (not from a prior phase):
the SMTP settings screen originally had no provider preset, so host/port had to be typed by hand —
added the same preset+auto-fill UX pattern already used for AI providers (`SMTP_PROVIDER_PRESETS`).

## Tests

53 new test methods across the domain/integration/API/security layers:

- **Unit** (`Omnichannel.UnitTests`): `TenantSecretTests`, `TenantAiProviderSettingsTests`,
  `TenantEmailSettingsTests` — entity invariants (Configure validation, Anthropic clears BaseUrl,
  port range, IsConfigured boundary).
- **Integration** (`Omnichannel.IntegrationTests`): `OpenAiCompatibleProviderTests`,
  `AnthropicProviderTests` (stubbed-response parsing, malformed-content fallback, API-error
  handling — Anthropic's fixture is built from public docs, not a captured real call, flagged in
  both the class and ADR-0027), `AiProviderDetectorTests` (key-prefix routing for
  Groq/OpenAI/Anthropic/unknown, hinted-kind override, rejected-key and network-failure handling).
- **API** (`Omnichannel.ApiTests`): `AiProviderSettingsEndpointsTests`,
  `EmailSettingsEndpointsTests` — CRUD round-trips, validation (blank fields, port range, invalid
  enum), auth requirement, key/password persistence across partial updates.
- **Security** (`Omnichannel.SecurityTests`): `TenantSecretsSecurityTests` — cross-tenant
  isolation on both new settings types, a plaintext-marker-never-in-database assertion, and
  Agent-role read-but-not-write permission gating on both endpoint groups.

Full backend suite: 274/274 (84 unit + 53 integration + 36 security + 101 API).

## Security Review

Full findings in `docs/security.md`'s new "Phase 16" section. Summary: no high/critical findings.
Secrets encrypted at rest, never returned to the client; every new query path uses
`IgnoreQueryFilters()` + explicit `tenantId` (required — these paths run from unauthenticated
registration/password-reset/webhook contexts); permission gating verified by test, not assumed;
both "test connection" endpoints are self-directed (no way to message an arbitrary third party
through them). One incidental real SMTP send during manual verification, disclosed to the user at
the time, harmless (fake recipient address). `AnthropicProvider` is unverified against a real
Anthropic API — explicitly flagged, not silently assumed correct.

## Migrations / Configuration Changes

New migration `20260904113527_AddTenantSecretsAiProviderEmailSettings` — three new tables
(`tenant_secrets`, `tenant_ai_provider_settings`, `tenant_email_settings`), one unique index on
`(tenant_id, purpose)`. No changes to existing tables/columns.

## ADRs / Docs Updated

New [ADR-0026](decisions/ADR-0026-admin-ui.md), new
[ADR-0027](decisions/ADR-0027-dynamic-tenant-configuration.md), `docs/security.md` (new "Phase 16"
section), `README.md` (Status section), this phase report.

## Known Limitations (recorded, not hidden)

- `AnthropicProvider` unverified against a real Anthropic key (none available in this
  environment) — verify before production use.
- No real-time push for the notification bell (30s polling, ADR-0026).
- Still outstanding from Phase 15, next up per the user's own stated order: JWT signing-key
  rotation overlap mechanism, data retention/account-deletion policy and flow, and choosing a
  Render deployment target.
