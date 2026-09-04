using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Infrastructure.Ai;

/// <summary>
/// The default <see cref="IAiProvider"/> injected here is whatever's registered platform-wide
/// (<c>GroqAiProvider</c> today) — this class only ever adds a per-tenant override on top of it,
/// never replaces the platform default's own DI registration (ADR-0027).
/// </summary>
public sealed class AiProviderResolver(
    IAppDbContext db, IAiProvider defaultProvider, IHttpClientFactory httpClientFactory, ITenantSecretStore secrets) : IAiProviderResolver
{
    private const string ApiKeyPurpose = "ai.apikey";
    private const string HttpClientName = "tenant-ai-provider";

    public async Task<IAiProvider> ResolveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await db.TenantAiProviderSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (settings is null)
        {
            return defaultProvider;
        }

        var apiKey = await secrets.GetAsync(tenantId, ApiKeyPurpose, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return defaultProvider;
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        return settings.ProviderKind switch
        {
            AiProviderKind.Anthropic => new AnthropicProvider(httpClient, apiKey, settings.Model),
            _ => new OpenAiCompatibleProvider(httpClient, settings.BaseUrl ?? string.Empty, apiKey, settings.Model),
        };
    }
}
