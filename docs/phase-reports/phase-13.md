# Phase Report — Phase 13: Business Rules + Automation

**Status:** Implementation complete. Proceeding to Phase 14 per explicit user instruction (no
approval pause).
**Date:** 2026-09-04

## Scope / PRD references

PRD §72 (Phase 13): business hours, holidays, escalation rules, notifications, saved replies,
basic automation. Security review: rules cannot execute arbitrary code, access other tenants,
bypass authorization, send unlimited messages, or disable safety controls.

## Implemented

- **`TenantBusinessHours`** (Domain, new) — general per-tenant business hours + holidays,
  independent of Phase 12's AI-specific config (ADR-0023). `IsOpenNow` checks holidays first,
  then the weekly schedule, fails toward "closed" for any unconfigured/unresolvable input.
- **`AutomationRule`** (Domain, new) — one bounded entity covering both "escalation rules" and
  "basic automation": keyword substring trigger, closed action set (apply tag / set priority /
  escalate), no code execution. `AutomationRuleService.EvaluateAsync` wired into the same three
  inbound-message paths as `AiAutoReplyService` (Manual/WhatsApp/Instagram/Messenger/website-chat).
- **`Notification`** (Domain, new) — in-app per-user notification feed, currently created only by
  an escalating rule (notifies every Owner/Admin `TenantMembership`), also emailed via the
  existing SMTP infrastructure. `GET /api/v1/notifications`, `.../unread-count`,
  `POST /api/v1/notifications/{id}/read`.
- **`SavedReply`** (Domain, new) — tenant-shared canned responses, full CRUD, gated by
  `conversations.reply` (an agent tool, not admin config).
- **API**: `GET/POST /api/v1/automation-rules`, `PUT .../{id}/enabled`, `DELETE .../{id}`
  (`tenant.update`); `GET/POST/PUT/DELETE /api/v1/saved-replies` (`conversations.reply`);
  `GET/PUT /api/v1/tenant/business-hours` (`tenant.read`/`tenant.update`) — no new permission
  keys invented, all reused from PRD §12's original catalog.

## Tests

- **Unit** (18 new): `AutomationRuleTests` (keyword matching, action validation, enable/disable),
  `TenantBusinessHoursTests` (holiday priority over schedule, unresolvable time zone fails safe),
  `SavedReplyTests` (validation, trimming, update).
- **API** (10 new, `AutomationRuleEndpointsTests`): keyword match applies tag+priority; no match
  takes no action; escalating rule flips status to `Escalated` and creates a notification;
  disabled rule never matches; full saved-reply CRUD lifecycle; business-hours default/round-trip/
  validation.
- **Security** (4 new, `AutomationSecurityTests`): cross-tenant isolation for rules, saved
  replies, and notifications; Agent role blocked from rule/business-hours management (403) but
  allowed to manage saved replies (201) — confirming the deliberately different permission bar.
- **Full backend suite**: 207/207 (67 unit + 37 integration + 30 security + 73 API).

## Security Review

Addressed PRD §72's full focus list — see `docs/security.md`'s new "Phase 13 controls" section:
no code execution (closed trigger/action set), tenant isolation (explicit `tenantId` +
`IgnoreQueryFilters()`, same documented pattern as Phase 12), authorization (existing permission
keys, verified per-role), no unlimited sends (automation rules have no send path at all), no
safety-control bypass (action set has no path to AI configuration). No high/critical findings.

## Migrations / Configuration Changes

- Migration `20260904060904_AddAutomationAndNotifications`: `automation_rules`, `notifications`,
  `saved_replies`, `tenant_business_hours` tables.
- `AuthService.RegisterAsync` now also creates a default `TenantBusinessHours` row for every new
  tenant, alongside the existing Phase 12 `AiAutoReplySettings` row.
- `IEmailSender` gained `SendConversationEscalatedAsync`; new email template in `EmailTemplates`.

## ADRs / Docs Updated

New [ADR-0023](decisions/ADR-0023-business-rules-automation.md). `docs/security.md` (new "Phase
13 controls" section).

## Known Limitations

- No frontend UI for rules, saved replies, business hours, or the notification feed — API-only.
- Two independent business-hours configs per tenant (Phase 12's AI-specific, Phase 13's general)
  — a deliberate scope decision, not an oversight (ADR-0023).
- No time-based/scheduled automation (e.g. "no response within N minutes") — would need a
  background job scheduler this project doesn't have; not built, not faked.

## Files/Modules Changed

`src/Omnichannel.Domain/Automation/{TenantBusinessHours,AutomationRule,SavedReply}.cs` (new),
`src/Omnichannel.Domain/Notifications/Notification.cs` (new),
`src/Omnichannel.Application/Abstractions/{IAppDbContext,IEmailSender}.cs`,
`src/Omnichannel.Application/Automation/{AutomationRuleService,TenantBusinessHoursService,SavedReplyService}.cs` (new),
`src/Omnichannel.Application/Notifications/NotificationService.cs` (new),
`src/Omnichannel.Application/Auth/AuthService.cs`,
`src/Omnichannel.Application/Conversations/ConversationService.cs`,
`src/Omnichannel.Application/Channels/WebhookIngestionService.cs`,
`src/Omnichannel.Application/Widget/WidgetService.cs`,
`src/Omnichannel.Application/DependencyInjection.cs`,
`src/Omnichannel.Infrastructure/Email/{SmtpEmailSender,EmailTemplates}.cs`,
`src/Omnichannel.Infrastructure/Persistence/AppDbContext.cs`,
`src/Omnichannel.Infrastructure/Persistence/Configurations/{TenantBusinessHoursConfiguration,AutomationRuleConfiguration,SavedReplyConfiguration,NotificationConfiguration}.cs` (new),
`src/Omnichannel.Infrastructure/Persistence/Migrations/20260904060904_AddAutomationAndNotifications*` (new),
`src/Omnichannel.Api/Endpoints/{AutomationEndpoints,NotificationEndpoints}.cs` (new), `Program.cs`,
`src/Omnichannel.Contracts/Automation/AutomationContracts.cs` (new),
`src/Omnichannel.Contracts/Notifications/NotificationContracts.cs` (new),
`tests/Omnichannel.UnitTests/Domain/{AutomationRuleTests,TenantBusinessHoursTests,SavedReplyTests}.cs` (new),
`tests/Omnichannel.ApiTests/Automation/AutomationRuleEndpointsTests.cs` (new),
`tests/Omnichannel.SecurityTests/AutomationSecurityTests.cs` (new),
`docs/decisions/ADR-0023` (new), `docs/security.md`.

## Next Phase

Phase 14 — Analytics (PRD §73): inbox metrics, response time, resolution, AI metrics, channel
metrics, agent metrics. Security: analytics queries must never aggregate across tenants.
Performance: avoid expensive per-request calculation, use indexes/aggregation as needed.

**Proceeding directly to Phase 14 per explicit user instruction — no approval pause.**
