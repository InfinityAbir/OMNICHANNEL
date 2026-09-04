using System.Net;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Ai;

namespace Omnichannel.IntegrationTests;

/// <summary>
/// AnthropicProvider (Phase 16, ADR-0027) against a stubbed HttpMessageHandler. Unlike
/// GroqAiProviderTests/OpenAiCompatibleProviderTests, the response fixture here is built from
/// Anthropic's public Messages API documentation, not a real captured call — no Anthropic key was
/// available in this environment, a limitation the class's own doc comment and ADR-0027 both
/// record. These tests verify the parsing logic this codebase controls; they cannot verify
/// Anthropic's actual wire format matches what's assumed here.
/// </summary>
public class AnthropicProviderTests
{
    private static AnthropicProvider CreateProvider(HttpMessageHandler handler, string model = "claude-3-5-sonnet-latest")
        => new(new HttpClient(handler), "test-key", model);

    [Fact]
    public async Task GenerateSuggestionAsync_DocumentedResponseShape_ParsesCorrectly()
    {
        const string body = """
        {"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"{\"suggestion\":\"We ship worldwide within 5-7 days.\",\"confidence\":0.88,\"requiresHuman\":false,\"escalationReason\":\"\"}"}],"model":"claude-3-5-sonnet-latest","usage":{"input_tokens":50,"output_tokens":30}}
        """;
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(
            new AiPromptContext("Test Business", [new AiTranscriptMessage("user", "How long does shipping take?")]),
            CancellationToken.None);

        Assert.Equal("We ship worldwide within 5-7 days.", result.SuggestedText);
        Assert.Equal(0.88, result.Confidence);
        Assert.False(result.RequiresHuman);
        Assert.Equal(50, result.PromptTokens);
        Assert.Equal(30, result.CompletionTokens);
        Assert.Equal("claude-3-5-sonnet-latest", result.Model);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_RequiresHumanTrue_CarriesEscalationReason()
    {
        const string body = """
        {"content":[{"type":"text","text":"{\"suggestion\":\"Let me connect you with a specialist.\",\"confidence\":0.5,\"requiresHuman\":true,\"escalationReason\":\"refund request\"}"}],"usage":{"input_tokens":10,"output_tokens":10}}
        """;
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None);

        Assert.True(result.RequiresHuman);
        Assert.Equal("refund request", result.EscalationReason);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_MalformedContent_FallsBackToRawTextRequiresHuman()
    {
        const string body = """{"content":[{"type":"text","text":"not valid json"}],"usage":{"input_tokens":1,"output_tokens":1}}""";
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, body));

        var result = await provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None);

        Assert.Equal("not valid json", result.SuggestedText);
        Assert.Equal(0.3, result.Confidence);
        Assert.True(result.RequiresHuman);
    }

    [Fact]
    public async Task GenerateSuggestionAsync_ApiError_ThrowsAiProviderException()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}"""));

        await Assert.ThrowsAsync<AiProviderException>(() =>
            provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateSuggestionAsync_NoTextContentBlock_ThrowsAiProviderException()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, """{"content":[],"usage":{"input_tokens":1,"output_tokens":0}}"""));

        await Assert.ThrowsAsync<AiProviderException>(() =>
            provider.GenerateSuggestionAsync(new AiPromptContext("Test", []), CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
