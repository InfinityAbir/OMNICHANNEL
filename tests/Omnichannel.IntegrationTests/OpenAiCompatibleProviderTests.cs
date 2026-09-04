using System.Net;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Ai;

namespace Omnichannel.IntegrationTests;

/// <summary>
/// A tenant's own OpenAI-compatible provider (Phase 16, ADR-0027) — exercises the shared
/// <see cref="OpenAiCompatibleChatClient"/> parsing/error-handling logic through the
/// tenant-configurable wrapper, the same response shapes <see cref="GroqAiProviderTests"/> already
/// verified against a real captured Groq response (the two share the same client, so this focuses
/// on the parts that differ per-tenant: an arbitrary base URL and model).
/// </summary>
public class OpenAiCompatibleProviderTests
{
    private static OpenAiCompatibleProvider CreateProvider(HttpMessageHandler handler, string baseUrl = "https://api.example-provider.test/v1", string model = "some-model")
    {
        var httpClient = new HttpClient(handler);
        return new OpenAiCompatibleProvider(httpClient, baseUrl, "test-key", model);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_ValidJsonContent_ParsesCorrectly()
    {
        const string body = """{"choices":[{"message":{"content":"{\"suggestion\":\"We ship worldwide.\",\"confidence\":0.8}"}}],"usage":{"prompt_tokens":20,"completion_tokens":10}}""";
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None);

        Assert.Equal("We ship worldwide.", result.SuggestedText);
        Assert.Equal(0.8, result.Confidence);
        Assert.Equal("some-model", result.Model);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_ApiError_ThrowsAiProviderException()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid api key"}}"""));

        await Assert.ThrowsAsync<AiProviderException>(() =>
            provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateSuggestionAsync_MalformedJsonContent_FallsBackToRawTextWithLowConfidence()
    {
        const string body = """{"choices":[{"message":{"content":"not json at all"}}],"usage":{"prompt_tokens":5,"completion_tokens":3}}""";
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None);

        Assert.Equal("not json at all", result.SuggestedText);
        Assert.Equal(0.3, result.Confidence);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
