using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Ai;

/// <summary>
/// A tenant's own OpenAI-compatible provider (Phase 16, ADR-0027) — any vendor speaking the same
/// `/chat/completions` shape Groq does (OpenAI itself, Together, Fireworks, Mistral, DeepSeek,
/// OpenRouter, a self-hosted OpenAI-shim server, ...), selected by base URL rather than a bespoke
/// class per vendor name. Constructed per-request by <c>AiProviderResolver</c> with the tenant's
/// own decrypted API key — never held anywhere longer than the call that needs it.
/// </summary>
public sealed class OpenAiCompatibleProvider(HttpClient httpClient, string baseUrl, string apiKey, string model) : IAiProvider
{
    public Task<AiCompletionResult> GenerateSuggestionAsync(AiPromptContext context, CancellationToken cancellationToken)
        => OpenAiCompatibleChatClient.GenerateSuggestionAsync(httpClient, "AI provider", baseUrl, apiKey, model, context, cancellationToken);
}
