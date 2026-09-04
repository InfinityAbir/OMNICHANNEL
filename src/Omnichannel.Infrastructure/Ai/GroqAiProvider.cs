using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Ai;

/// <summary>
/// The platform's own default AI provider — Groq's OpenAI-compatible Chat Completions API, keyed
/// by the deployer's own API key (<see cref="AiOptions"/>), used whenever a tenant hasn't
/// configured their own provider (see <c>AiProviderResolver</c>, ADR-0027). Isolated entirely
/// behind <see cref="IAiProvider"/> (ADR-0020) — nothing above this class knows it's Groq
/// specifically. The actual request/response logic lives in
/// <see cref="OpenAiCompatibleChatClient"/>, shared with <see cref="OpenAiCompatibleProvider"/>
/// (the tenant-configurable variant) rather than duplicated.
/// </summary>
public sealed class GroqAiProvider(HttpClient httpClient, IOptions<AiOptions> options) : IAiProvider
{
    private readonly AiOptions _options = options.Value;

    public Task<AiCompletionResult> GenerateSuggestionAsync(AiPromptContext context, CancellationToken cancellationToken)
        => OpenAiCompatibleChatClient.GenerateSuggestionAsync(
            httpClient, "Groq", _options.BaseUrl, _options.ApiKey, _options.Model, context, cancellationToken);
}
