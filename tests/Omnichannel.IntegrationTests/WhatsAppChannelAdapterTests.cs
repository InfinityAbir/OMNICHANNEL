using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;
using Omnichannel.Infrastructure.Channels;

namespace Omnichannel.IntegrationTests;

/// <summary>
/// Exercises WhatsAppChannelAdapter's pure logic (signature verification, payload parsing, error
/// classification) against real payload shapes from Meta's own Cloud API documentation (ADR-0017)
/// — no network calls except SendMessageAsync, which uses a fake HttpMessageHandler.
/// </summary>
public class WhatsAppChannelAdapterTests
{
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";

    private static WhatsAppChannelAdapter CreateAdapter(HttpMessageHandler? handler = null)
    {
        var httpClient = new HttpClient(handler ?? new StubHandler(HttpStatusCode.OK, "{}"))
        {
            BaseAddress = new Uri("https://graph.facebook.com"),
        };
        var options = Options.Create(new WhatsAppOptions
        {
            AppSecret = AppSecret,
            VerifyToken = VerifyToken,
            GraphApiVersion = "v23.0",
            GraphApiBaseUrl = "https://graph.facebook.com",
        });
        return new WhatsAppChannelAdapter(httpClient, options);
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
            new Dictionary<string, string> { ["hub.mode"] = "subscribe", ["hub.verify_token"] = VerifyToken, ["hub.challenge"] = "12345" },
            string.Empty);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("12345", result.ChallengeResponse);
    }

    [Fact]
    public async Task VerifyWebhookAsync_GetHandshake_WrongTokenIsInvalid()
    {
        var adapter = CreateAdapter();
        var request = new WebhookRequest(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["hub.mode"] = "subscribe", ["hub.verify_token"] = "wrong", ["hub.challenge"] = "12345" },
            string.Empty);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task VerifyWebhookAsync_PostDelivery_ValidSignatureIsValid()
    {
        var adapter = CreateAdapter();
        const string body = """{"object":"whatsapp_business_account","entry":[]}""";
        var request = new WebhookRequest(
            new Dictionary<string, string> { ["X-Hub-Signature-256"] = Sign(body) },
            new Dictionary<string, string>(),
            body);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task VerifyWebhookAsync_PostDelivery_TamperedBodyIsInvalid()
    {
        var adapter = CreateAdapter();
        const string signedBody = """{"object":"whatsapp_business_account","entry":[]}""";
        const string tamperedBody = """{"object":"whatsapp_business_account","entry":[{"injected":true}]}""";
        var request = new WebhookRequest(
            new Dictionary<string, string> { ["X-Hub-Signature-256"] = Sign(signedBody) },
            new Dictionary<string, string>(),
            tamperedBody);

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task VerifyWebhookAsync_PostDelivery_MissingSignatureHeaderIsInvalid()
    {
        var adapter = CreateAdapter();
        var request = new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), "{}");

        var result = await adapter.VerifyWebhookAsync(request, CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ParseWebhookAsync_InboundTextMessage_MapsToNormalizedEvent()
    {
        var adapter = CreateAdapter();
        // Shape taken directly from Meta's Cloud API webhooks documentation.
        const string body = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "102290129340398",
              "changes": [
                {
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "15550783881", "phone_number_id": "106540352242922" },
                    "contacts": [ { "profile": { "name": "Sheena Nelson" }, "wa_id": "16505551234" } ],
                    "messages": [
                      { "from": "16505551234", "id": "wamid.ABC123", "timestamp": "1749416383", "type": "text", "text": { "body": "Does it come in another color?" } }
                    ]
                  },
                  "field": "messages"
                }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        var @event = Assert.Single(events);
        Assert.Equal(NormalizedInboundEventKind.Message, @event.Kind);
        Assert.Equal("106540352242922", @event.ProviderAccountExternalId);
        Assert.Equal("wamid.ABC123", @event.ExternalMessageId);
        Assert.Equal("16505551234", @event.VisitorExternalId);
        Assert.Equal("Sheena Nelson", @event.VisitorDisplayName);
        Assert.Equal("Does it come in another color?", @event.Text);
        Assert.Equal(MessageContentType.Text, @event.ContentType);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1749416383), @event.OccurredAt);
    }

    [Fact]
    public async Task ParseWebhookAsync_StatusUpdate_MapsToNormalizedEvent()
    {
        var adapter = CreateAdapter();
        const string body = """
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "102290129340398",
              "changes": [
                {
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "15550783881", "phone_number_id": "106540352242922" },
                    "statuses": [
                      { "id": "wamid.XYZ789", "status": "delivered", "timestamp": "1750263773", "recipient_id": "16505551234" }
                    ]
                  },
                  "field": "messages"
                }
              ]
            }
          ]
        }
        """;

        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), body), CancellationToken.None);

        var @event = Assert.Single(events);
        Assert.Equal(NormalizedInboundEventKind.StatusUpdate, @event.Kind);
        Assert.Equal("wamid.XYZ789", @event.ExternalMessageId);
        Assert.Equal(MessageDeliveryStatus.Delivered, @event.Status);
    }

    [Fact]
    public async Task ParseWebhookAsync_MalformedJson_ReturnsEmptyNotThrows()
    {
        var adapter = CreateAdapter();
        var events = await adapter.ParseWebhookAsync(new WebhookRequest(new Dictionary<string, string>(), new Dictionary<string, string>(), "not json"), CancellationToken.None);
        Assert.Empty(events);
    }

    [Fact]
    public async Task SendMessageAsync_Success_ReturnsExternalMessageId()
    {
        const string responseBody = """{"messages":[{"id":"wamid.SENT1"}]}""";
        var adapter = CreateAdapter(new StubHandler(HttpStatusCode.OK, responseBody));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "106540352242922", "16505551234", "hello", "fake-token"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("wamid.SENT1", result.ExternalMessageId);
    }

    [Fact]
    public async Task SendMessageAsync_ExpiredToken_ReturnsAuthFailed()
    {
        const string responseBody = """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""";
        var adapter = CreateAdapter(new StubHandler(HttpStatusCode.Unauthorized, responseBody));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "106540352242922", "16505551234", "hello", "expired-token"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ChannelSendErrorKind.AuthFailed, result.ErrorKind);
    }

    [Fact]
    public async Task SendMessageAsync_OutsideWindow_ReturnsPermanentFailureNotRetryable()
    {
        const string responseBody = """{"error":{"message":"Re-engagement message","type":"OAuthException","code":131047}}""";
        var adapter = CreateAdapter(new StubHandler(HttpStatusCode.BadRequest, responseBody));

        var result = await adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "106540352242922", "16505551234", "hello", "token"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ChannelSendErrorKind.PermanentFailure, result.ErrorKind);
    }

    [Fact]
    public async Task SendMessageAsync_RateLimited_ThrowsRetryableException()
    {
        const string responseBody = """{"error":{"message":"Too many requests","type":"OAuthException","code":130429}}""";
        var adapter = CreateAdapter(new StubHandler((HttpStatusCode)429, responseBody));

        var exception = await Assert.ThrowsAsync<ChannelSendException>(() => adapter.SendMessageAsync(
            new ChannelSendRequest(Guid.NewGuid(), "106540352242922", "16505551234", "hello", "token"),
            CancellationToken.None));

        Assert.Equal(ChannelSendErrorKind.RateLimited, exception.ErrorKind);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
