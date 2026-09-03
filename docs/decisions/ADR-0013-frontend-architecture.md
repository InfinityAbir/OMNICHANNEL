# ADR-0013: Angular frontend architecture

**Status:** Accepted
**Date:** 2026-09-03

## Context

Phase 3 (PRD §62) builds the first Angular UI: auth screens and the unified inbox. Needed to
decide state management, API-client shape, and how the SPA authenticates against the JWT-bearer
backend from ADR-0007.

## Decision

- **Signals, not NgRx.** Angular 21's native signals (`signal`/`computed`/`input`/`effect`) hold
  all UI state — one `AuthService` with a `currentUser` signal, per-feature services
  (`ConversationService`, `ContactService`, `TagService`) that return RxJS `Observable`s from
  `HttpClient` calls, consumed via `.subscribe()` into signals inside components. No global store
  library: the app has a handful of independent feature areas with no cross-cutting state
  complex enough to justify one, matching AGENTS.md's "avoid unnecessary abstractions."
- **Component input binding for routing** (`withComponentInputBinding()`): the `:id` route param
  binds directly to a component's `id` input signal — no manual `ActivatedRoute` subscription
  boilerplate per component.
- **Bearer tokens in `localStorage`**, not an httpOnly cookie. The backend is header-bearer-token
  based already (ADR-0007), and introducing cookie-based auth now would mean CSRF protection,
  SameSite/domain configuration, and a second auth mechanism to maintain alongside the JWT one
  already built for a future Android client. Documented XSS trade-off: `localStorage` is
  readable by any script running on the page, so this depends on the app never rendering
  untrusted content via `[innerHTML]`/`bypassSecurityTrust*` — audited, and none does; Angular's
  default template interpolation HTML-escapes everything. Revisit if a future phase needs to
  render untrusted rich content (e.g. formatted knowledge-base articles).
- **One HTTP interceptor** attaches the access token and handles 401 by attempting a single
  silent refresh (via `/api/v1/auth/refresh`) before redirecting to `/login` — mirrors the
  backend's own rotate-on-use refresh design (ADR-0007).
- **Keyset cursors passed through as opaque strings** — the frontend never decodes or
  reconstructs them, matching the backend's own "opaque, unversioned" design (ADR-0012).
- **Monochromatic design system**: CSS custom properties (`--gray-0`..`--gray-900`, one `danger`
  accent) in `styles.scss`, per the user's explicit standing direction for the inbox UI —
  skeleton loaders (`SkeletonComponent`), empty states (`EmptyStateComponent`), and real
  interaction states (hover/focus-visible/transitions) throughout rather than static panels.

## Alternatives considered

- **NgRx/a global store.** Rejected for the same reason central package management didn't
  reach for a repository-pattern abstraction in the backend: no demonstrated need yet. Revisit
  if cross-feature state coordination (e.g. realtime updates in Phase 4) makes signals-in-services
  awkward.
- **httpOnly cookie auth.** Rejected per above — would duplicate the auth mechanism rather than
  reuse the one already built, for a security property (script-inaccessible token) this phase
  addresses instead through strict output encoding discipline.

## Consequences

- Phase 4 (Realtime Messaging / SignalR) will need to decide how push updates flow into these
  same signals — likely each feature service exposing an `update$`-style method services can
  call from a shared SignalR connection handler, not a redesign of the state model itself.
- Any future component that needs to render user-supplied HTML (not plain text) must go through
  Angular's `DomSanitizer` explicitly and needs its own security review — nothing today does.
