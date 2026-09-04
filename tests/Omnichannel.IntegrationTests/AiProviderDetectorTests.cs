using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Domain.Ai;
using Omnichannel.Infrastructure.Ai;

namespace Omnichannel.IntegrationTests;

/// <summary>
/// AiProviderDetector (Phase 16, ADR-0027): guesses provider kind/base-URL from a pasted key's
/// well-known prefix, then verifies against the provider's own live /models listing — these tests
/// stub that HTTP call so they cover the prefix heuristics and response parsing without a real key.
/// </summary>
public class AiProviderDetectorTests
{
    private static AiProviderDetector CreateDetector(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("tenant-ai-provider").ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return new AiProviderDetector(provider.GetRequiredService<IHttpClientFactory>());
    }

    [Fact]
    public async Task DetectAsync_GroqPrefixedKey_UsesGroqBaseUrlAndReturnsModels()
    {
        const string body = """{"data":[{"id":"openai/gpt-oss-120b"},{"id":"llama-3.3-70b-versatile"}]}""";
        var handler = new RecordingStubHandler(HttpStatusCode.OK, body);
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("gsk_faketestkey", null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(AiProviderKind.OpenAiCompatible, result.ProviderKind);
        Assert.Equal("https://api.groq.com/openai/v1", result.BaseUrl);
        Assert.Equal(2, result.AvailableModels.Count);
        Assert.Equal("openai/gpt-oss-120b", result.SuggestedModel);
        Assert.Contains("api.groq.com", handler.LastRequestUri!.Host);
        Assert.Equal("Bearer", handler.LastAuthScheme);
    }

    [Fact]
    public async Task DetectAsync_OpenAiPrefixedKey_UsesOpenAiBaseUrl()
    {
        var handler = new RecordingStubHandler(HttpStatusCode.OK, """{"data":[{"id":"gpt-4o"}]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("sk-faketestkey", null, null, CancellationToken.None);

        Assert.Equal("https://api.openai.com/v1", result.BaseUrl);
        Assert.Equal(AiProviderKind.OpenAiCompatible, result.ProviderKind);
    }

    [Fact]
    public async Task DetectAsync_AnthropicPrefixedKey_UsesXApiKeyHeaderNotBearer()
    {
        var handler = new RecordingStubHandler(HttpStatusCode.OK, """{"data":[{"id":"claude-3-5-sonnet-latest"}]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("sk-ant-faketestkey", null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(AiProviderKind.Anthropic, result.ProviderKind);
        Assert.Null(result.BaseUrl);
        Assert.Equal("claude-3-5-sonnet-latest", result.SuggestedModel);
        Assert.Null(handler.LastAuthScheme); // Anthropic uses x-api-key, never a Bearer token.
        Assert.Contains("anthropic.com", handler.LastRequestUri!.Host);
    }

    [Fact]
    public async Task DetectAsync_UnrecognizedPrefix_FallsBackToGroqAsStartingPoint()
    {
        var handler = new RecordingStubHandler(HttpStatusCode.OK, """{"data":[{"id":"some-model"}]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("totally-unknown-format", null, null, CancellationToken.None);

        Assert.Equal("https://api.groq.com/openai/v1", result.BaseUrl);
    }

    [Fact]
    public async Task DetectAsync_HintedKindOverridesPrefixGuessing()
    {
        var handler = new RecordingStubHandler(HttpStatusCode.OK, """{"data":[{"id":"self-hosted-model"}]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("gsk_thisLooksLikeGroq", AiProviderKind.OpenAiCompatible, "https://my-server.example/v1", CancellationToken.None);

        Assert.Equal("https://my-server.example/v1", result.BaseUrl);
    }

    [Fact]
    public async Task DetectAsync_RejectedKey_ReturnsFailureNotException()
    {
        var handler = new RecordingStubHandler(HttpStatusCode.Unauthorized, """{"error":"invalid_api_key"}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("gsk_badkey", null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(result.AvailableModels);
        Assert.Null(result.SuggestedModel);
    }

    [Fact]
    public async Task DetectAsync_EmptyModelList_ReturnsFailure()
    {
        var handler = new RecordingStubHandler(HttpStatusCode.OK, """{"data":[]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectAsync("gsk_key", null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("no models", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_NetworkFailure_ReturnsFailureNotException()
    {
        var detector = CreateDetector(new ThrowingHandler());

        var result = await detector.DetectAsync("gsk_key", null, null, CancellationToken.None);

        Assert.False(result.Success);
    }

    private sealed class RecordingStubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthScheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastAuthScheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated network failure");
    }
}
