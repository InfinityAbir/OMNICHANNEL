using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;
using Omnichannel.Infrastructure.Channels;

namespace Omnichannel.IntegrationTests;

/// <summary>Mirrors WhatsAppChannelAdapterTests — same Graph API webhook mechanics, verified independently for Instagram's own payload shapes (ADR-0018).</summary>
public class InstagramChannelAdapterTests
{
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";

    private static InstagramChannelAdapter CreateAdapter(HttpMessageHandler? handler = null)
    {
        var httpClient = new HttpClient(handler ?? new StubHandler(HttpStatusCode.OK, "{}"))
        {
            BaseAddress = new Uri("https://graph.instagram.com"),
        };
        var options = Options.Create(new InstagramOptions
        {
            AppSecret = AppSecret,
            VerifyToken = VerifyToken,
            GraphApiVersion = "v25.0",
            GraphApiBaseUrl = "https://graph.instagram.com",
        });
        return new InstagramChannelAdapter(httpClient, options);
    }

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public async Task VerifyWebhookAsync_GetHandshake_ValidTokenReturnsChallenge()
    {
        var adapter = CreateAdapter();
        var request = new WebhookRequest(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["hub.mode"] = "subscribe", ["hub.verify_token"] = VerifyToken, ["hub.challenge"] = "555" },
            string.Empty);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("555", result.ChallengeResponse);
    }

    [Fact]
    public async Task VerifyWebhookAsync_PostDelivery_TamperedBodyIsInvalid()
    {
        var adapter = CreateAdapter();
        const string signedBody = """{"object":"instagram","entry":[]}""";
        const string tamperedBody = """{"object":"instagram","entry":[{"injected":true}]}""";
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
        var adapter = CreateAdapter();
        // Shape per Meta's Instagram Messaging API documentation.
        const string body = """
        {
          "object": "instagram",
          "entry": [
            {
              "time": 1778223729706,
              "id": "17841476961942794",
              "messaging": [
                {
                  "sender": { "id": "978239761327698" },
                  "recipient": { "id": "17841476961942794" },
                  "timestamp": 1778223722476,
                  "message": { "mid": "aWdfZAG1faXRlbToxOklHTWVz", "text": "Is this in stock?" }
                }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        var @event = Assert.Single(events);
        Assert.Equal(NormalizedInboundEventKind.Message, @event.Kind);
        Assert.Equal("17841476961942794", @event.ProviderAccountExternalId);
        Assert.Equal("aWdfZAG1faXRlbToxOklHTWVz", @event.ExternalMessageId);
        Assert.Equal("978239761327698", @event.VisitorExternalId);
        Assert.Equal("Is this in stock?", @event.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1778223722476), @event.OccurredAt);
    }

    [Fact]
    public async Task ParseWebhookAsync_DeliveryReceipt_MapsToStatusUpdate()
    {
        var adapter = CreateAdapter();
        const string body = """
        {
          "object": "instagram",
          "entry": [
            {
              "id": "17841476961942794",
              "messaging": [
                { "sender": { "id": "1" }, "recipient": { "id": "17841476961942794" }, "timestamp": 1778223722476,
                  "delivery": { "mids": ["aWdfZAG1faXRlbToxOklHTWVz"] } }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        var @event = Assert.Single(events);
        Assert.Equal(NormalizedInboundEventKind.StatusUpdate, @event.Kind);
        Assert.Equal(MessageDeliveryStatus.Delivered, @event.Status);
    }

    [Fact]
    public async Task SendMessageAsync_Success_ReturnsExternalMessageId()
    {
        const string responseBody = """{"recipient_id":"978239761327698","message_id":"ig-msg-1"}""";
        var adapter = CreateAdapter(new StubHandler(HttpStatusCode.OK, responseBody));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "17841476961942794", "978239761327698", "hello", "fake-token"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("ig-msg-1", result.ExternalMessageId);
    }

    [Fact]
    public async Task SendMessageAsync_ExpiredToken_ReturnsAuthFailed()
    {
        const string responseBody = """{"error":{"message":"Error validating access token","code":190}}""";
        var adapter = CreateAdapter(new StubHandler(HttpStatusCode.Unauthorized, responseBody));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "17841476961942794", "978239761327698", "hello", "expired"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ChannelSendErrorKind.AuthFailed, result.ErrorKind);
    }

    [Fact]
    public async Task SendMessageAsync_RateLimited_ThrowsRetryableException()
    {
        const string responseBody = """{"error":{"message":"Application request limit reached","code":4}}""";
        var adapter = CreateAdapter(new StubHandler((HttpStatusCode)429, responseBody));

        var exception = await Assert.ThrowsAsync<ChannelSendException>(() => adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "17841476961942794", "978239761327698", "hello", "token"),
            CancellationToken.None));

        Assert.Equal(ChannelSendErrorKind.RateLimited, exception.ErrorKind);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
