# ADR-0026: Admin UI for Phase 6-14 (Channels, Knowledge Base, AI, Automation, Analytics)

**Status:** Accepted
**Date:** 2026-09-04

## Context

Phases 6 through 14 shipped real, tested backend functionality (channel connections, knowledge
base, AI suggest/auto-reply, automation rules, saved replies, business hours, analytics) but
stayed API-only — every phase report explicitly recorded "no frontend UI yet" as a known
limitation. The user asked for this gap closed, with an explicit requirement: **everything
dynamic, nothing hardcoded — changing configuration should never require a code change.**

## Decision

**A `/settings` area with a permission-driven nav, not a hardcoded menu.** `SettingsPageComponent`
filters its nav items against the current user's own `CurrentUserResponse.permissions` (from the
JWT, via `AuthService.currentUser()`) — a role gains or loses a settings screen purely by what
permissions the backend actually issued it, never a hardcoded role-name check in the frontend.
This satisfies "dynamic" at the navigation layer: add a permission to a role on the backend and
the corresponding screen appears, no frontend deploy needed.

**Configuration data lives in the database, edited through forms — never redeployed as code.**
Every setting this UI edits (channel credentials, knowledge documents, AI thresholds/business
hours, automation rules, saved replies) was already a database-backed, API-driven concept from
its originating phase (12-13's whole design point was avoiding hardcoded thresholds). This UI is
the missing edit surface for data that was already dynamic — it doesn't introduce any new
hardcoded configuration.

**One reusable `BusinessHoursEditorComponent`, not two.** Phase 12's AI-specific business hours
and Phase 13's general business hours are two independent backend configs (ADR-0023) but need
the identical weekly-schedule editing UI. Built once, used by both `ai-settings` and
`business-hours-settings` — the two screens differ only in which service they call.

**A shared `settings-common.scss`, not per-screen duplication.** Seven new screens share the same
card/table/form/button/modal visual language established in Phase 3's design system (same CSS
custom property tokens: `--surface`, `--border`, `--accent`, etc.). Rather than duplicate ~200
lines of structural CSS seven times (the project's prior convention for smaller per-component
diffs), one shared partial is loaded via each component's `styleUrls` array — the first
deliberate exception to "duplicate small CSS blocks" in this codebase, justified by the sheer
repetition seven near-identical screens would otherwise require.

**Genuinely fixed enums stay as small frontend arrays — not invented API catalog endpoints.**
Channel types (`WhatsApp`/`Instagram`/`Messenger`), AI modes, and priorities have no
"GET /catalog" endpoint on the backend (there's nothing to paginate or permission-gate about a
fixed 3-4-value enum) — mirroring how `ConversationStatus`/`ConversationPriority` were already
handled in `conversation-detail.ts` since Phase 3. Building a backend catalog endpoint for a
handful of compile-time-fixed enum values would be inventing dynamism where none is needed;
"dynamic" was scoped to configuration a business owner actually changes, not the shape of the
domain model itself.

**Notifications: polling, not a new SignalR event.** The bell polls `GET /notifications/
unread-count` every 30s plus on-demand when opened. Real-time push would need a new SignalR hub
event and backend wiring beyond what was asked; polling is a small, real, working feature that
doesn't expand scope into new backend infrastructure for a "nice to have" (sub-30-second
notification latency) the ask didn't call for.

**Verified live, every screen, not just built.** Registered a real user, connected a real
WhatsApp account (external id + credential, watched the "Connected" badge flip), created a real
knowledge document and searched it, configured real AI auto-reply settings (business hours,
confidence, limit) and confirmed persistence across a reload, created a real automation rule,
triggered it with a real inbound message via the API, and watched the resulting notification
appear in the bell, link to the right conversation, and mark itself read on click. Every one of
these is an actual round trip through the real backend against the real Postgres database, not a
mocked screenshot.

## Consequences

- `web/src/app/shared/settings-common.scss` is now a real shared-style dependency across 7
  components — a change there affects all of them. Acceptable given how structurally identical
  the screens are; would need splitting if any one screen's needs diverge significantly later.
- No real-time push for notifications — 30-second polling latency at worst. A concrete, scoped
  future improvement if the user wants it, not built speculatively now.
- Business hours windows are edited one `<input type="time">` pair at a time per day, no bulk
  "copy Monday to all weekdays" shortcut — a reasonable v1 scope, not a completeness gap in what
  the backend supports (the backend already accepts an arbitrary per-day window list).
