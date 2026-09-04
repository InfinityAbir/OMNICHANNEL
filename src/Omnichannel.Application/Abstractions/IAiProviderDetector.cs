using Omnichannel.Domain.Ai;

namespace Omnichannel.Application.Abstractions;

public sealed record AiProviderDetectionResult(
    bool Success, string Message, AiProviderKind ProviderKind, string? BaseUrl, IReadOnlyList<string> AvailableModels, string? SuggestedModel);

/// <summary>
/// Given just a pasted API key, guesses the provider from well-known key-prefix conventions
/// (Groq's "gsk_", Anthropic's "sk-ant-", OpenAI's plain "sk-") and calls that provider's own
/// live models-list endpoint to populate a real, current model list — the same "verify against
/// the live API, don't guess the model name" discipline this project already applies to the
/// platform's own Groq configuration (ADR-0020), now available to a tenant configuring their own
/// key. Every result stays editable — this only ever pre-fills.
/// </summary>
public interface IAiProviderDetector
{
    Task<AiProviderDetectionResult> DetectAsync(string apiKey, AiProviderKind? hintedKind, string? hintedBaseUrl, CancellationToken cancellationToken);
}
