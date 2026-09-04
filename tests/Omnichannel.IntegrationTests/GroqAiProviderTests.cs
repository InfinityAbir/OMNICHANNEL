using System.Net;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Ai;

namespace Omnichannel.IntegrationTests;

/// <summary>
/// Verifies GroqAiProvider's request/response handling against a stubbed HttpMessageHandler — no
/// real network call, but the success-path fixture below is the *actual* response body captured
/// from a real Groq chat-completions call made while building this phase (model
/// openai/gpt-oss-120b, response_format json_object), not synthesized from documentation guesses.
/// </summary>
public class GroqAiProviderTests
{
    private static GroqAiProvider CreateProvider(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.com") };
        var options = Options.Create(new AiOptions { ApiKey = "test-key", BaseUrl = "https://api.groq.com/openai/v1", Model = "openai/gpt-oss-120b" });
        return new GroqAiProvider(httpClient, options);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_RealCapturedResponseShape_ParsesCorrectly()
    {
        // Captured verbatim from a real Groq API call during Phase 10 development (ADR-0020).
        const string body = """
        {"id":"chatcmpl-9859fe72-1c78-42ec-b45d-569f8231d0c9","object":"chat.completion","created":1788469484,"model":"openai/gpt-oss-120b","choices":[{"index":0,"message":{"role":"assistant","content":"{\"suggestion\":\"I’m not able to check real‑time inventory, but you can find out if the blue jacket in size M is in stock by visiting our website or contacting the store directly.\", \"confidence\":0.92}","reasoning":"..."},"logprobs":null,"finish_reason":"stop"}],"usage":{"queue_time":0.36154567,"prompt_tokens":145,"prompt_time":0.0055141,"completion_tokens":137,"completion_time":0.285253806,"total_tokens":282}}
        """;
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(
            new AiPromptContext("Test Business", [new AiTranscriptMessage("user", "Is the blue jacket in stock in size M?")]),
            CancellationToken.None);

        Assert.Contains("real", result.SuggestedText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.92, result.Confidence);
        Assert.Equal(145, result.PromptTokens);
        Assert.Equal(137, result.CompletionTokens);
        Assert.Equal("openai/gpt-oss-120b", result.Model);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_MalformedJsonContent_FallsBackToRawTextWithLowConfidence()
    {
        const string body = """{"choices":[{"message":{"content":"Sorry, not valid JSON here"}}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None);

        Assert.Equal("Sorry, not valid JSON here", result.SuggestedText);
        Assert.Equal(0.3, result.Confidence);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_ConfidenceOutOfRange_IsClamped()
    {
        const string body = """{"choices":[{"message":{"content":"{\"suggestion\":\"hi\",\"confidence\":1.5}"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None);

        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_ApiError_ThrowsAiProviderException()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid api key"}}"""));

        await Assert.ThrowsAsync<AiProviderException>(() =>
            provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
