# ADR-0011: Email delivery via SMTP (MailKit)

**Status:** Accepted
**Date:** 2026-09-03

## Context

Phase 1 requires working email confirmation and password reset (PRD §13). Initial plan (Phase 0
report) was to ship a logging stub for Phase 1 and wire a real provider later, since no email
account existed yet. Mid-Phase-1, the user supplied a real Gmail SMTP account and asked that
transactional emails be well-designed, not plain text — upgrading the plan.

## Decision

`IEmailSender` (Application abstraction, unchanged) is implemented by `SmtpEmailSender`
(Infrastructure) using MailKit against Gmail SMTP (`smtp.gmail.com:587`, STARTTLS). Credentials
live only in local `dotnet user-secrets` (dev) / environment variables (any other environment) —
never in `appsettings.json` or `.env.example`. Emails are HTML with a plain-text fallback,
single-accent branded layout (`Infrastructure/Email/EmailTemplates.cs`), sender display name
"Omnichannel" (not the raw mailbox address).

Delivery failures are caught and logged, never allowed to fail the calling flow (registration,
password reset) — losing an email is recoverable (resend/retry later); breaking registration
because SMTP hiccuped is not.

## Alternatives considered

- **Stay with the logging stub through Phase 1, add real SMTP in a later phase.** Superseded by
  the user directly providing working credentials and asking for the real thing now — no reason
  to defer working functionality PRD §13 already requires.
- **A transactional email API (SendGrid/Postmark/etc.) instead of raw SMTP.** Not chosen: adds a
  vendor dependency and account-provisioning step neither requested nor available; Gmail SMTP is
  what's on hand and sufficient for current volume. `IEmailSender` is already the seam — swapping
  to a provider API later means a new Infrastructure implementation, not an Application change.

## Consequences

- Single Gmail account sending on behalf of the product; fine at current volume, but Gmail SMTP
  has real per-account sending limits — revisit (a transactional provider, or Google Workspace)
  before any meaningful production volume. Documented here as a known scaling limit, not silently
  assumed adequate forever.
- The Gmail app password was shared directly in chat during this session — flagged to the user;
  worth rotating if that's a concern, independent of how the code stores it (which never exposes
  it beyond local user-secrets/environment variables).
