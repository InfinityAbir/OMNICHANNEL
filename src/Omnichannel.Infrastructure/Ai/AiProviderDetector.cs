using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Infrastructure.Ai;

/// <inheritdoc cref="IAiProviderDetector" />
public sealed class AiProviderDetector(IHttpClientFactory httpClientFactory) : IAiProviderDetector
{
    private const string HttpClientName = "tenant-ai-provider";

    public async Task<AiProviderDetectionResult> DetectAsync(
        string apiKey, AiProviderKind? hintedKind, string? hintedBaseUrl, CancellationToken cancellationToken)
    {
        var (kind, baseUrl) = ResolveKindAndBaseUrl(apiKey, hintedKind, hintedBaseUrl);
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            return kind == AiProviderKind.Anthropic
                ? await DetectAnthropicAsync(httpClient, apiKey, cancellationToken)
                : await DetectOpenAiCompatibleAsync(httpClient, baseUrl!, apiKey, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new AiProviderDetectionResult(false, $"Could not reach the provider to list models: {ex.Message}", kind, baseUrl, [], null);
        }
    }

    // Well-known, publicly documented key-prefix conventions — a best-effort starting point, not
    // a guarantee; the caller can always pass an explicit hintedKind/hintedBaseUrl to skip
    // guessing entirely (e.g. a self-hosted OpenAI-compatible server has no recognizable prefix).
    private static (AiProviderKind Kind, string? BaseUrl) ResolveKindAndBaseUrl(string apiKey, AiProviderKind? hintedKind, string? hintedBaseUrl)
    {
        if (hintedKind is not null)
        {
            return (hintedKind.Value, hintedKind == AiProviderKind.Anthropic ? null : hintedBaseUrl);
        }

        if (apiKey.StartsWith("sk-ant-", StringComparison.Ordinal))
        {
            return (AiProviderKind.Anthropic, null);
        }

        if (apiKey.StartsWith("gsk_", StringComparison.Ordinal))
        {
            return (AiProviderKind.OpenAiCompatible, "https://api.groq.com/openai/v1");
        }

        if (apiKey.StartsWith("sk-", StringComparison.Ordinal))
        {
            return (AiProviderKind.OpenAiCompatible, "https://api.openai.com/v1");
        }

        return (AiProviderKind.OpenAiCompatible, hintedBaseUrl ?? "https://api.groq.com/openai/v1");
    }

    private static async Task<AiProviderDetectionResult> DetectOpenAiCompatibleAsync(
        HttpClient httpClient, string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new AiProviderDetectionResult(
                false, $"The provider rejected this key or URL (HTTP {(int)response.StatusCode}).", AiProviderKind.OpenAiCompatible, baseUrl, [], null);
        }

        var parsed = JsonSerializer.Deserialize<OpenAiModelListResponse>(body);
        var models = (parsed?.Data ?? [])
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (models.Count == 0)
        {
            return new AiProviderDetectionResult(false, "The key worked, but the provider returned no models.", AiProviderKind.OpenAiCompatible, baseUrl, [], null);
        }

        return new AiProviderDetectionResult(true, $"Found {models.Count} model(s).", AiProviderKind.OpenAiCompatible, baseUrl, models, PickSuggestedModel(models));
    }

    private static async Task<AiProviderDetectionResult> DetectAnthropicAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new AiProviderDetectionResult(
                false, $"The provider rejected this key (HTTP {(int)response.StatusCode}).", AiProviderKind.Anthropic, null, [], null);
        }

        var parsed = JsonSerializer.Deserialize<AnthropicModelListResponse>(body);
        var models = (parsed?.Data ?? []).Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

        if (models.Count == 0)
        {
            return new AiProviderDetectionResult(false, "The key worked, but Anthropic returned no models.", AiProviderKind.Anthropic, null, [], null);
        }

        return new AiProviderDetectionResult(true, $"Found {models.Count} model(s).", AiProviderKind.Anthropic, null, models, models.FirstOrDefault());
    }

    // No live model-quality signal to rank on (a models-list endpoint returns ids, not
    // capability) — a small, transparent preference order, always overridable by the user.
    private static string? PickSuggestedModel(List<string> models)
        => models.FirstOrDefault(m => m.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
        ?? models.FirstOrDefault(m => m.Contains("gpt-4", StringComparison.OrdinalIgnoreCase) && !m.Contains("instruct", StringComparison.OrdinalIgnoreCase))
        ?? models.FirstOrDefault(m => !m.Contains("whisper", StringComparison.OrdinalIgnoreCase) && !m.Contains("embed", StringComparison.OrdinalIgnoreCase) && !m.Contains("tts", StringComparison.OrdinalIgnoreCase) && !m.Contains("dall-e", StringComparison.OrdinalIgnoreCase) && !m.Contains("moderation", StringComparison.OrdinalIgnoreCase))
        ?? models.FirstOrDefault();

    private sealed record OpenAiModelListResponse([property: JsonPropertyName("data")] List<OpenAiModel>? Data);

    private sealed record OpenAiModel([property: JsonPropertyName("id")] string Id);

    private sealed record AnthropicModelListResponse([property: JsonPropertyName("data")] List<AnthropicModel>? Data);

    private sealed record AnthropicModel([property: JsonPropertyName("id")] string Id);
}
