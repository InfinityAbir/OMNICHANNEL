# ADR-0027: Dynamic Per-Tenant AI Provider and SMTP Configuration

**Status:** Accepted
**Date:** 2026-09-04

## Context

The user's requirement for ADR-0026's admin UI extended further: a tenant's own AI provider (not
just the platform's default Groq key) and a tenant's own SMTP mail server should both be
configurable per business, entirely at runtime — "should not need to change code, nothing will be
hardcoded." Follow-up requirements arrived incrementally and are all in scope here: not limited to
a fixed list of AI vendors; the model/endpoint should auto-detect from a pasted API key while
staying editable; both AI and SMTP need a "test connection" check before saving; both handle
credentials the codebase already treats as sensitive (AGENTS.md: never in source, logs, or client
bundles).

## Decision

**One generic `TenantSecret` entity, not two bespoke ones.** `ChannelCredential` (ADR-0016)
already proved the pattern — Data Protection API encryption at rest, keyed to a tenant. Rather
than build a second parallel encrypted-secret mechanism for AI keys and a third for SMTP
passwords, `TenantSecret` generalizes it: `(TenantId, Purpose)` as a unique key, `Purpose` an
arbitrary string (`"ai.apikey"`, `"smtp.password"`) rather than a fixed enum, so a future secret
type needs no schema change. `DataProtectionTenantSecretStore` never exposes the encrypted value
outside itself — every caller gets plaintext-in/plaintext-out through `ITenantSecretStore`, never
the ciphertext.

**One generic `OpenAiCompatibleProvider`, plus `AnthropicProvider` for the one real outlier —
not a bespoke class per vendor.** The large majority of the market (Groq, OpenAI itself, Together,
Fireworks, Mistral, DeepSeek, OpenRouter, self-hosted OpenAI-shim servers) implements the same
`/chat/completions` request/response shape; the only variable that actually differs between them
is the base URL, key, and model string. `OpenAiCompatibleProvider(HttpClient, baseUrl, apiKey,
model)` covers all of them, backed by `OpenAiCompatibleChatClient` — the same request-building,
JSON-schema, and parsing logic `GroqAiProvider` already used, extracted so the platform's own
default provider and every tenant-configured OpenAI-compatible provider share one implementation
rather than duplicating the system prompt and parsing logic. Anthropic's Messages API is a
genuinely different shape (system as a top-level field, `x-api-key`/`anthropic-version` headers
instead of a bearer token, required `max_tokens`) and gets its own class. This directly answers
"not only those 3 providers" — the frontend's preset list (Groq/OpenAI/Together/Fireworks/
Mistral/DeepSeek/OpenRouter/Custom) is a convenience auto-fill, not an enforced allowlist; picking
"Custom" and typing any other OpenAI-compatible base URL works identically to a listed preset.

**Resolve-with-fallback, not a required migration.** `IAiProviderResolver.ResolveAsync(tenantId)`
returns the tenant's own provider only if BOTH a `TenantAiProviderSettings` row exists AND an
`ai.apikey` secret exists; otherwise it returns whatever `IAiProvider` is registered
platform-wide (today, `GroqAiProvider`). `SmtpEmailSender.ResolveConfigAsync` applies the
identical shape for SMTP: tenant's own `TenantEmailSettings` + `smtp.password` secret if both
present and `IsConfigured`, else the existing global `SmtpOptions`. Consequence: every existing
test, and every tenant who never opens the settings screen, keeps working exactly as before —
`AiSuggestionService`/`AiAutoReplyService`'s refactor from `IAiProvider` to `IAiProviderResolver`
broke zero of the ~30 pre-existing AI tests, since `services.AddSingleton<IAiProvider>(fake)` in
each of those tests is precisely the "default" the resolver falls back to.

**Auto-detect from a pasted key, always left editable.** `IAiProviderDetector.DetectAsync` guesses
provider kind and base URL from well-known key-prefix conventions (`sk-ant-` → Anthropic, `gsk_` →
Groq, `sk-` → OpenAI, else Groq as a starting point — the same "verify against the live API, don't
guess" discipline ADR-0020 established for Groq's own model id), then calls that provider's real
`/models` listing endpoint to return actual, current model ids plus a heuristically-ranked
suggested default. The frontend never locks the resulting fields — `applyPreset`/`detectFromKey`
only pre-fill `baseUrl`/`model`/`providerKind` signals a user can still type over, satisfying
"auto fill, but user wants to still able to edit them" literally.

**Test-before-save, both surfaces.** `AiProviderSettingsService.TestAsync` resolves the tenant's
currently-configured provider (their own if set, otherwise the platform default) and sends one
minimal real completion request — proving the key/model/base-URL combination actually works, not
just that a row was saved. `EmailSettingsService.TestAsync` → `IEmailSender.SendTestEmailAsync`
sends a real test email to the *calling user's own address* — a self-directed connection check,
matching how "send test email" works in any mail client, not a message to an arbitrary third
party. Both return a structured `(bool Success, string Message)` rather than throwing, so a wrong
password or an unreachable host shows a clean inline result instead of a 500.

**Every tenant-secret code path uses `IgnoreQueryFilters()` + an explicit `tenantId`, mirroring
ADR-0016/ADR-0022.** `DataProtectionTenantSecretStore`, `AiProviderResolver`, and
`SmtpEmailSender.ResolveConfigAsync` all run from contexts that may have no authenticated tenant —
registration and password-reset call `IEmailSender` before any session exists, and auto-reply
runs from unauthenticated provider webhooks — so `IEmailSender`'s interface signature gained an
explicit leading `Guid tenantId` on every method (a breaking change to the interface, all three
call sites updated) rather than relying on ambient `ITenantContext`.

## Consequences

- `AnthropicProvider` is built from Anthropic's public API documentation, not verified against a
  real Anthropic key (none was available in this environment) — its own doc comment and this ADR
  both flag that explicitly. Verify against a real key before depending on it in production; the
  `OpenAiCompatibleProvider`/detector paths, by contrast, were verified live against a real Groq
  key end-to-end (detect → save → test connection, in the actual browser UI).
- A tenant's own AI provider key/SMTP password, once saved, is only ever decrypted inside the
  request that needs it (`AiProviderResolver`/`SmtpEmailSender`) — never cached, logged, or
  returned to the client. `GET` endpoints return `HasApiKey`/`HasPassword` booleans only.
- `ai.configure`/`tenant.update` gate every write (save, clear, test, detect); `ai.read`/
  `tenant.read` alone can view configuration state (provider kind, base URL, model, "is
  configured") but never write or clear it — the Agent role has the read permissions but not the
  write ones, verified by `TenantSecretsSecurityTests`.
- One incidental real SMTP send happened during manual verification of the platform-default
  fallback path (to a fake, nonexistent recipient — harmless, disclosed to the user at the time).
  No further real sends were triggered from automated tests; `EmailSettingsEndpointsTests`
  deliberately does not exercise the `/test` endpoint for this reason.
