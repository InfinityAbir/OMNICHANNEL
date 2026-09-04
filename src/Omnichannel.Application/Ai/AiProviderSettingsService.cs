using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Application.Ai;

public sealed record AiProviderTestResult(bool Success, string Message);

/// <summary>
/// CRUD + connection test for a tenant's own AI provider configuration (Phase 16, ADR-0027) —
/// separate from <see cref="AiAutoReplyService"/> (the decision pipeline) and
/// <see cref="AiSuggestionService"/> (Suggest mode), same separation-of-concerns reasoning as
/// <c>AiAutoReplySettingsService</c>.
/// </summary>
public sealed class AiProviderSettingsService(
    IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider,
    ITenantSecretStore secrets, IAiProviderResolver resolver, IAiProviderDetector detector)
{
    private const string ApiKeyPurpose = "ai.apikey";

    public Task<AiProviderDetectionResult> DetectAsync(string apiKey, AiProviderKind? hintedKind, string? hintedBaseUrl, CancellationToken cancellationToken)
        => detector.DetectAsync(apiKey, hintedKind, hintedBaseUrl, cancellationToken);

    public async Task<(TenantAiProviderSettings Settings, bool HasApiKey)> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.TenantAiProviderSettings.SingleOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
        settings ??= await CreateDefaultAsync(cancellationToken);

        var hasKey = await secrets.ExistsAsync(tenantContext.TenantId, ApiKeyPurpose, cancellationToken);
        return (settings, hasKey);
    }

    public async Task<(TenantAiProviderSettings Settings, bool HasApiKey)> UpdateAsync(
        AiProviderKind providerKind, string? baseUrl, string model, string? apiKey, CancellationToken cancellationToken)
    {
        var (settings, hasApiKey) = await GetAsync(cancellationToken);
        settings.Configure(providerKind, baseUrl, model, timeProvider.GetUtcNow());

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await secrets.SetAsync(tenantContext.TenantId, ApiKeyPurpose, apiKey, cancellationToken);
            hasApiKey = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (settings, hasApiKey);
    }

    public async Task ClearApiKeyAsync(CancellationToken cancellationToken)
        => await secrets.DeleteAsync(tenantContext.TenantId, ApiKeyPurpose, cancellationToken);

    /// <summary>Sends a minimal real completion request through the tenant's currently-resolved provider (their own if configured, otherwise the platform default) — proves the key/model/base-URL actually work, not just that a row exists.</summary>
    public async Task<AiProviderTestResult> TestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var provider = await resolver.ResolveAsync(tenantContext.TenantId, cancellationToken);
            var result = await provider.GenerateSuggestionAsync(
                new AiPromptContext("Test Business", [new AiTranscriptMessage("user", "This is a connection test — reply with any short greeting.")]),
                cancellationToken);
            return new AiProviderTestResult(true, $"Connected successfully (model: {result.Model}).");
        }
        catch (AiProviderException ex)
        {
            return new AiProviderTestResult(false, ex.Message);
        }
    }

    private async Task<TenantAiProviderSettings> CreateDefaultAsync(CancellationToken cancellationToken)
    {
        // Defaults mirror the platform's own Groq configuration (AiOptions) — the same values
        // proven live during Phase 10 — so a tenant who just clicks "Save" without changing
        // anything gets a real, working configuration, not a placeholder.
        var settings = TenantAiProviderSettings.CreateDefault(
            tenantContext.TenantId, "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", timeProvider.GetUtcNow());
        db.TenantAiProviderSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
