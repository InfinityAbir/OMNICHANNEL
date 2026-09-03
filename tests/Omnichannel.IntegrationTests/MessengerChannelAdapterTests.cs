using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;
using Omnichannel.Infrastructure.Channels;

namespace Omnichannel.IntegrationTests;

/// <summary>Mirrors WhatsApp/InstagramChannelAdapterTests — verifies Messenger's own wire shapes (ADR-0019), including its query-string access_token auth (unlike WhatsApp/Instagram's Bearer header).</summary>
public class MessengerChannelAdapterTests
{
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";

    private static (MessengerChannelAdapter Adapter, RecordingHandler Handler) CreateAdapter(HttpMessageHandler? handler = null)
    {
        var recorder = new RecordingHandler(handler ?? new StubHandler(HttpStatusCode.OK, "{}"));
        var httpClient = new HttpClient(recorder) { BaseAddress = new Uri("https://graph.facebook.com") };
        var options = Options.Create(new MessengerOptions
        {
            AppSecret = AppSecret,
            VerifyToken = VerifyToken,
            GraphApiVersion = "v23.0",
            GraphApiBaseUrl = "https://graph.facebook.com",
        });
        return (new MessengerChannelAdapter(httpClient, options), recorder);
    }

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public async Task VerifyWebhookAsync_GetHandshake_ValidTokenReturnsChallenge()
    {
        var (adapter, _) = CreateAdapter();
        var request = new WebhookRequest(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["hub.mode"] = "subscribe", ["hub.verify_token"] = VerifyToken, ["hub.challenge"] = "321" },
            string.Empty);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("321", result.ChallengeResponse);
    }

    [Fact]
    public async Task VerifyWebhookAsync_PostDelivery_TamperedBodyIsInvalid()
    {
        var (adapter, _) = CreateAdapter();
        const string signedBody = """{"object":"page","entry":[]}""";
        const string tamperedBody = """{"object":"page","entry":[{"injected":true}]}""";
        var request = new WebhookRequest(
            new Dictionary<string, string> { ["X-Hub-Signature-256"] = Sign(signedBody) },
            new Dictionary<string, string>(),
            tamperedBody);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ParseWebhookAsync_InboundTextMessage_MapsToNormalizedEvent()
    {
        var (adapter, _) = CreateAdapter();
        const string body = """
        {
          "object": "page",
          "entry": [
            {
              "id": "PAGE_ID_123",
              "time": 1458692752478,
              "messaging": [
                {
                  "sender": { "id": "USER_PSID_456" },
                  "recipient": { "id": "PAGE_ID_123" },
                  "timestamp": 1458692752478,
                  "message": { "mid": "mid.1457764197618:41d102a3e1ae206a38", "text": "hello, world!" }
                }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        var @event = Assert.Single(events);
        Assert.Equal(NormalizedInboundEventKind.Message, @event.Kind);
        Assert.Equal("PAGE_ID_123", @event.ProviderAccountExternalId);
        Assert.Equal("mid.1457764197618:41d102a3e1ae206a38", @event.ExternalMessageId);
        Assert.Equal("USER_PSID_456", @event.VisitorExternalId);
        Assert.Equal("hello, world!", @event.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1458692752478), @event.OccurredAt);
    }

    [Fact]
    public async Task ParseWebhookAsync_DeliveryWithMids_MapsToStatusUpdate()
    {
        var (adapter, _) = CreateAdapter();
        const string body = """
        {
          "object": "page",
          "entry": [
            {
              "id": "PAGE_ID_123",
              "messaging": [
                { "sender": { "id": "1" }, "recipient": { "id": "PAGE_ID_123" }, "timestamp": 1458692752478,
                  "delivery": { "mids": ["mid.ABC"], "watermark": 1458692752482 } }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        var @event = Assert.Single(events);
        Assert.Equal(NormalizedInboundEventKind.StatusUpdate, @event.Kind);
        Assert.Equal("mid.ABC", @event.ExternalMessageId);
        Assert.Equal(MessageDeliveryStatus.Delivered, @event.Status);
    }

    [Fact]
    public async Task ParseWebhookAsync_ReadReceipt_WatermarkOnlyProducesNoEvent()
    {
        // Read events carry only a watermark timestamp, no message id — cannot be mapped under
        // this pipeline's id-based status model (ADR-0019). Confirms the adapter doesn't guess.
        var (adapter, _) = CreateAdapter();
        const string body = """
        {
          "object": "page",
          "entry": [
            {
              "id": "PAGE_ID_123",
              "messaging": [
                { "sender": { "id": "1" }, "recipient": { "id": "PAGE_ID_123" }, "timestamp": 1458692752478,
                  "read": { "watermark": 1458692752482 } }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        Assert.Empty(events);
    }

    [Fact]
    public async Task SendMessageAsync_UsesQueryStringAccessTokenNotBearerHeader()
    {
        var (adapter, recorder) = CreateAdapter(new StubHandler(HttpStatusCode.OK, """{"recipient_id":"USER_PSID_456","message_id":"mid.SENT1"}"""));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "PAGE_ID_123", "USER_PSID_456", "hello", "page-token-abc"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("mid.SENT1", result.ExternalMessageId);
        Assert.NotNull(recorder.LastRequest);
        Assert.Contains("access_token=page-token-abc", recorder.LastRequest!.RequestUri!.Query);
        Assert.Null(recorder.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task SendMessageAsync_ExpiredToken_ReturnsAuthFailed()
    {
        var (adapter, _) = CreateAdapter(new StubHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"Error validating access token","code":190}}"""));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "PAGE_ID_123", "USER_PSID_456", "hello", "expired"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ChannelSendErrorKind.AuthFailed, result.ErrorKind);
    }

    [Fact]
    public async Task SendMessageAsync_RateLimited_ThrowsRetryableException()
    {
        var (adapter, _) = CreateAdapter(new StubHandler((HttpStatusCode)429, """{"error":{"message":"Rate limit","code":613}}"""));

        var exception = await Assert.ThrowsAsync<ChannelSendException>(() => adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "PAGE_ID_123", "USER_PSID_456", "hello", "token"),
            CancellationToken.None));

        Assert.Equal(ChannelSendErrorKind.RateLimited, exception.ErrorKind);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
