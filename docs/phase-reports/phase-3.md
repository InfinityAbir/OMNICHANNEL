# Phase Report — Phase 3: Unified Inbox UI

**Status:** Implementation complete. Awaiting user approval to proceed to Phase 4.
**Date:** 2026-09-03

## Scope / PRD references

PRD §62 (Phase 3): conversation list, conversation view, customer panel, message composer,
assignment, tags, notes, search, filters, status controls, responsive design. PRD §49
(dashboard pages — `/login`, `/inbox`, `/inbox/:conversationId`).

## Implemented

- **Auth screens**: `/login`, `/register` (reactive forms, inline validation, error messages
  distinguishing locked/unconfirmed/invalid-credentials without leaking which).
- **Route guard**: `authGuard` redirects unauthenticated visits to `/inbox*` to `/login`
  (UX-only — backend authorization remains authoritative, per AGENTS.md).
- **Inbox**: two-pane layout (list + detail), responsive down to a single pane on narrow
  viewports (list ↔ detail, back-link appears only where needed). Filters (All / Assigned to me
  / Escalated / Closed — only the ones the backend actually supports; "Unread"/"AI handled"/
  "Needs human" are not built since there's no backend concept for them yet, not faked), search
  (debounced, contact-name match), keyset "load more" pagination reusing the backend's opaque
  cursor unchanged.
- **Conversation detail**: customer panel, full message timeline (chat-bubble style, inbound/
  outbound/system visually distinct), composer, tags (add existing/create new/remove), internal
  notes (separate tab, clearly labeled "never visible to the customer"), assignment, status and
  priority controls.
- **New-conversation flow**: modal creating a contact + conversation + optional first message in
  one action — the practical entry point for the Manual channel (ADR-0012).
- **Design system**: monochromatic CSS custom properties, skeleton loaders during data fetches,
  empty states for no-data conditions, real interaction states (hover/focus-visible/transitions)
  — per the user's standing design direction.
- **State/API layer**: signals-based (no NgRx), one HTTP interceptor attaching the bearer token
  and handling 401 via a single silent refresh attempt before redirecting to `/login`. Full
  rationale in [ADR-0013](../decisions/ADR-0013-frontend-architecture.md).
- **Backend additions this phase** (small, needed to support the UI honestly rather than fake
  it): `GET /api/v1/conversations?search=` (contact-name match, same pattern as Phase 2's
  contact search); `Conversation.LastMessagePreview` (denormalized at write time — the list
  query never needs a per-row join to fetch it); conversation `Tags` now return `{id, name}`
  instead of bare names (removing a tag needs its id, which bare strings can't provide).
- **Playwright E2E** (first phase requiring it): `e2e/` project, real API + real Angular dev
  server + real Postgres, no mocking. Covers the critical path: register → land on inbox →
  create a conversation → send a message → see it in the list with its preview → sign out; plus
  the unauthenticated-redirect case. New CI job runs it on every push.

## A mid-phase process failure, root-caused and fixed

While building Phase 3's E2E test, checking CI (per the user's new standing instruction) surfaced
that **the backend CI job had been failing since Phase 1's push** — both Phase 1 and Phase 2 were
reported "complete, all tests pass" based only on local `dotnet test`, never on the actual GitHub
Actions run. Two causes, both invisible locally:

1. `Program.cs` throws if `Jwt:SigningKey` is missing. That key only ever existed in local
   `dotnet user-secrets` — CI (and a fresh clone) has no access to it, so every
   `WebApplicationFactory`-based test failed to even boot the app.
2. Nothing ever applied EF Core migrations to CI's Postgres. `Program.cs` only auto-migrates
   when the environment is Development/Testing, but tests never explicitly set that — they
   inherited whatever `ASPNETCORE_ENVIRONMENT` happened to be ambient locally (silently
   "Development" via `dotnet run`'s `launchSettings.json`, never exercised the same way by
   `dotnet test`/CI).

Fixed (own commit, `5cf8f39`, verified before continuing Phase 3 work): `appsettings.Testing.json`
(committed, non-secret test signing key + the same dev-only connection string docker-compose/CI
already use), `TestWebApplicationFactory` in both API/Security test projects forcing the
`Testing` environment explicitly instead of relying on ambient state, an explicit
`dotnet ef database update` CI step (deterministic regardless of test ordering/parallelism), and
`SmtpEmailSender`'s catch clause broadened to match its own stated intent (a connection/DNS
failure wasn't being caught, only 3 specific MailKit exception types were — this also means
tests no longer send real email during every run, which they had been doing since Phase 1). Full
detail in `docs/security.md`'s Phase 3 section and `MEMORY.md` (local). User has made "check CI
after every push" a standing rule going forward.

## Tests

- **Unit**: 18/18 (unchanged from Phase 2 — no new domain logic this phase).
- **Integration**: 1/1.
- **API**: 16/16.
- **Security**: 8/8.
- **Frontend**: `ng lint` clean, `ng test` 2/2 (app shell creation + signed-out nav-hiding).
- **E2E (Playwright)**: 2/2 — full conversation lifecycle, unauthenticated redirect.
- **Manual verification in-browser**: register, login, logout, session persistence across
  reload, create conversation, send messages both directions, assign/unassign, add/remove tag,
  add internal note, change status/priority, filter, list preview text — all confirmed working.
  Found and fixed one real responsive-layout bug during this pass (header wrapped and visually
  overlapped the status dropdown at narrow widths — not a viewport-emulation artifact, a genuine
  CSS flex-layout bug; fixed with proper `flex-wrap` + `flex-basis` on the header row).

All counts green. `dotnet build`: 0 warnings/errors. `ng build`: 0 warnings/errors (one
component-style budget warning found and fixed by removing genuine CSS duplication, not by
loosening the budget). `dotnet list package --vulnerable`: 0 findings. `npm audit` (both `web/`
and `e2e/`): 0 vulnerabilities.

## Security Review

- **Route guards**: verified — unauthenticated `/inbox` visits redirect to `/login`
  (Playwright-regression-tested). Guard is UX-only; every backend endpoint still independently
  enforces authentication/authorization regardless of what the frontend does.
- **XSS**: audited the whole `web/src/app` tree — no `[innerHTML]`, no
  `DomSanitizer.bypassSecurityTrust*`, anywhere. All user content (message text, notes, contact/
  tag names) renders via plain Angular interpolation, which HTML-escapes by default.
- **Token storage**: bearer tokens in `localStorage` — a documented, deliberate trade-off (see
  ADR-0013), not an oversight. Its safety currently rests entirely on the XSS audit above; any
  future component rendering untrusted rich content needs its own review before it ships.
- **CI/process finding**: see above — fixed, not a runtime vulnerability, but recorded because
  it's exactly the "looked done, wasn't verified" gap the phase-gate process exists to catch.

No high/critical application-level findings.

## Performance/Accessibility Review

- `LastMessagePreview` denormalization avoids an N+1/per-row join on the conversation list —
  same "optimize every endpoint" discipline as prior phases.
- Keyset pagination carried through to the UI unchanged (opaque cursor, no client-side
  reconstruction) — no new pagination anti-pattern introduced.
- Accessibility: form labels on every input, `:focus-visible` styling globally, `aria-modal`/
  `role="dialog"`/`aria-labelledby` on the new-conversation modal, semantic `role="tablist"`/
  `role="tab"` on filters and detail tabs, `alt`-equivalent (`aria-hidden`) on the decorative
  avatar initial. Not a full WCAG audit — deeper accessibility testing (screen reader pass,
  contrast audit) is reasonable to schedule before Phase 15's production hardening, not blocking
  this phase.
- Bundle budget: initial ~272 kB raw / ~75 kB transfer, lazy-loaded feature chunks — within
  Angular's default production budgets.

## Migrations / Configuration Changes

- No new migration this phase beyond `AddConversationLastMessagePreview` (already applied).
- New: `src/Omnichannel.Api/appsettings.Testing.json` (test-only, non-secret).
- New: `web/proxy.conf.json` (dev-server → API proxy, avoids CORS in local dev).
- New: `e2e/` project (`package.json`, `playwright.config.ts`, `tests/inbox.spec.ts`).
- CI: new `e2e` job; `backend` job gained an explicit migration-apply step.

## ADRs / Docs Updated

ADR-0013 (frontend architecture). `docs/architecture.md`, `docs/security.md`, `docs/api.md`,
`README.md` (setup instructions now include the frontend + E2E steps) — all updated for Phase 3
state.

## Known Limitations

- List filters limited to what the backend supports (no unread tracking, no AI-mode filters yet
  — both need later-phase backend work).
- No saved replies in the composer (no backend support).
- Password-reset flow still uses Phase 1's plain-HTML form, not yet an Angular page.
- E2E coverage is the one agreed critical path, not exhaustive; expected to grow incrementally
  as more UI ships rather than being front-loaded now.

## Files/Modules Changed

`web/src/app/**` (new: `core/`, `features/auth/`, `features/inbox/`, `shared/`; modified:
`app.*`, `app.config.ts`, `app.routes.ts`), `web/proxy.conf.json`, `web/angular.json`,
`e2e/**` (new), `.github/workflows/ci.yml` (e2e job + migration step),
`src/Omnichannel.Api/appsettings.Testing.json` (new), `src/Omnichannel.Infrastructure/Email/SmtpEmailSender.cs`,
`tests/Omnichannel.{Api,Security}Tests/TestWebApplicationFactory.cs` (new) + 6 test classes
updated to use it, backend search/preview/tag-shape additions listed above,
`docs/decisions/ADR-0013`, `docs/{architecture,security,api}.md`, `README.md`.

## Next Phase

Phase 4 — Realtime Messaging (SignalR) (PRD §63): new message / conversation update /
assignment update / notification / message-status events, tenant-scoped SignalR groups,
reconnection/duplicate-event/multi-tab reliability testing.

**Requesting approval to commit/push this phase and begin Phase 4.**
