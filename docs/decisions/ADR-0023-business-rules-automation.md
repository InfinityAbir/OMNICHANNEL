# ADR-0023: Business Rules + Automation

**Status:** Accepted
**Date:** 2026-09-04

## Context

PRD §72 (Phase 13): business hours, holidays, escalation rules, notifications, saved replies,
basic automation. Security review: rules cannot execute arbitrary code, access other tenants,
bypass authorization, send unlimited messages, or disable safety controls.

## Decision

**Escalation rules and basic automation are one entity, not two.** Both are "when inbound text
matches X, do Y" — `AutomationRule` covers both with a closed action set (apply a tag, set a
fixed priority, escalate) rather than inventing a separate rule engine for each PRD line item.
Time-based triggers (e.g. "no agent response within N minutes") were considered and explicitly
**not built** — this project has no background job scheduler yet, and faking a time-based check
by evaluating it lazily on next access would be dishonest about what it actually does; keyword-
triggered, message-time evaluation (the same insertion point as Phase 12's `AiAutoReplyService`)
is the scope that's real and correct today.

**A second, independent business-hours config, not a retrofit of Phase 12's.**
`AiAutoReplySettings.BusinessHoursJson` (Phase 12) already ships, is tested, and gates AI
auto-reply specifically. `TenantBusinessHours` (this phase) is the general "is the business open"
concept PRD §72 asks for, used for escalation/notification purposes — it reuses the same public
`BusinessHoursWindow` record Phase 12 defined (no duplicate type) but keeps its own JSON
(de)serialization rather than refactoring Phase 12's already-shipped entity to share it. A
small, deliberate duplication (~25 lines) over a cross-phase refactor with no concrete benefit
yet — worth reconsidering once the two configs actually need to reconcile (a "closed" banner
that has to describe two independent schedules, say).

**Holidays always close the business, weekly schedule or not.** `TenantBusinessHours.IsOpenNow`
checks the holiday list before the weekly schedule — a date in the holiday list is authoritative,
matching how holidays actually work for a real business (Friday hours don't apply on Eid, even if
Friday is normally a working day).

**No new permission keys.** PRD §12's permission catalog was defined up front for the whole
project (Phase 10/11's `ai.read`/`ai.configure`/etc. already existed, unused, until their phase);
Phase 13 reuses `tenant.update` (Owner/Admin — matches Phase 12's `ai.configure` precedent of
gating tenant-wide config to admins) for rules and business hours, and `conversations.reply`
(every Agent+ role) for saved replies, since those are an agent tool, not admin configuration.

**Notifications: in-app + email, to Owner/Admin members only.** A `Notification` row is created
per Owner/Admin `TenantMembership` when a rule escalates, plus an email via the existing
`IEmailSender`/SMTP infrastructure from Phase 1 (no new delivery channel invented). A small
business realistically has 1-3 owners/admins, so no batching/pagination is needed for the notify
step itself; the read-side (`GET /api/v1/notifications`) is paginated regardless.

**Automation rules never send a message.** Their only actions are tag/priority/escalate — the
PRD §72 security requirement "cannot send unlimited messages" is satisfied structurally: there is
no code path from an `AutomationRule` to an outbound send at all, so there's nothing to rate-limit
here (Phase 12's own daily cap already covers the one AI path that does send).

## Consequences

- No frontend UI for any of this yet (rules, saved replies, business hours, notification feed) —
  API-only, the same launch-state pattern as every Phase 6+ feature.
- Two independently configured business-hours schedules exist per tenant (Phase 12's AI-specific
  one, Phase 13's general one) until a future phase has a concrete reason to reconcile them.
- "Basic automation" is deliberately scoped to message-time, keyword-triggered rules — no
  time-based/scheduled automation exists (would need a background job scheduler this project
  doesn't have yet); not built, not faked.
