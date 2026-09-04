using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Ai;

/// <summary>
/// Which AI provider a tenant's own key/model talks to. "OpenAiCompatible" covers the large
/// majority of providers on the market (Groq, OpenAI itself, Together, Fireworks, Mistral,
/// DeepSeek, OpenRouter, self-hosted OpenAI-shim servers, ...) since they all implement the same
/// `/chat/completions` request/response shape — one implementation with a configurable base URL,
/// not a bespoke class per vendor name. Anthropic's Messages API is a genuinely different shape,
/// so it gets its own value/implementation. See ADR-0027.
/// </summary>
public enum AiProviderKind
{
    OpenAiCompatible = 0,
    Anthropic = 1,
}

/// <summary>
/// Per-tenant AI provider configuration — one row per tenant, created with sensible defaults at
/// registration but always overridable. The API key itself is never stored here: it lives in
/// <see cref="Security.TenantSecret"/> (purpose "ai.apikey"), encrypted at rest, so this entity
/// (and therefore any query result / audit log referencing it) never carries the secret.
/// When a tenant hasn't configured their own key, <c>AiProviderResolver</c> (Infrastructure) falls
/// back to the platform's own default provider/key — this entity only ever describes an
/// *override*, never a requirement.
/// </summary>
public sealed class TenantAiProviderSettings : ITenantOwned
{
    public Guid TenantId { get; private set; }
    public AiProviderKind ProviderKind { get; private set; } = AiProviderKind.OpenAiCompatible;

    /// <summary>Only meaningful for <see cref="AiProviderKind.OpenAiCompatible"/> — Anthropic's endpoint is fixed.</summary>
    public string? BaseUrl { get; private set; }

    public string Model { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantAiProviderSettings()
    {
    }

    public static TenantAiProviderSettings CreateDefault(Guid tenantId, string baseUrl, string model, DateTimeOffset now)
        => new()
        {
            TenantId = tenantId,
            ProviderKind = AiProviderKind.OpenAiCompatible,
            BaseUrl = baseUrl,
            Model = model,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Configure(AiProviderKind providerKind, string? baseUrl, string model, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required.", nameof(model));
        }

        if (providerKind == AiProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is required for an OpenAI-compatible provider.", nameof(baseUrl));
        }

        ProviderKind = providerKind;
        BaseUrl = providerKind == AiProviderKind.Anthropic ? null : baseUrl!.Trim();
        Model = model.Trim();
        UpdatedAt = now;
    }
}
