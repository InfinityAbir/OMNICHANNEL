using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Infrastructure.Channels;

/// <summary>
/// Instagram Messaging API adapter (Phase 8, PRD §67) — "Instagram API with Instagram Login"
/// model (graph.instagram.com, IG-scoped ids, Instagram User access token), Meta's current
/// recommended path for new integrations. Scope decisions and documentation research in
/// ADR-0018; summary: text-only outbound (same reasoning as WhatsApp — no media-upload UI
/// exists), no Human Agent tag (7-day-window extension) support yet — a genuine but separate
/// feature deferred rather than half-implemented, inbound accepts any message type but only
/// extracts text (attachments are typed correctly, not downloaded).
/// </summary>
public sealed class InstagramChannelAdapter(HttpClient httpClient, IOptions<InstagramOptions> options) : IChannelAdapter
{
    private readonly InstagramOptions _options = options.Value;

    public ChannelType Type => ChannelType.Instagram;

    public ChannelCapabilities Capabilities { get; } = new(
        MaxTextLength: 1000, SupportsMedia: false, SupportsDeliveryReceipts: true, SupportsReadReceipts: true, HasMessagingWindow: true);

    public Task<WebhookVerificationResult> VerifyWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        // Same Graph API webhook mechanics as WhatsApp (ADR-0017's "consequences" — confirmed by
        // this phase's own research, not assumed): GET handshake for one-time subscription setup,
        // HMAC-SHA256 signature over the raw body for every POST delivery.
        if (request.Query.TryGetValue("hub.mode", out var mode) && mode == "subscribe")
        {
            var tokenMatches = request.Query.TryGetValue("hub.verify_token", out var token)
                && !string.IsNullOrEmpty(_options.VerifyToken)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_options.VerifyToken));

            return Task.FromResult(tokenMatches && request.Query.TryGetValue("hub.challenge", out var challenge)
                ? WebhookVerificationResult.Valid(challenge)
                : WebhookVerificationResult.Invalid("hub.verify_token mismatch."));
        }

        if (!request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader) ||
            !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return Task.FromResult(WebhookVerificationResult.Invalid("Missing X-Hub-Signature-256 header."));
        }

        if (string.IsNullOrEmpty(_options.AppSecret))
        {
            return Task.FromResult(WebhookVerificationResult.Invalid("Instagram App Secret is not configured."));
        }

        var expected = ComputeHmacHex(_options.AppSecret, request.Body);
        var provided = signatureHeader["sha256=".Length..];
        var valid = provided.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));

        return Task.FromResult(valid ? WebhookVerificationResult.Valid() : WebhookVerificationResult.Invalid("Signature mismatch."));
    }

    public Task<IReadOnlyList<NormalizedInboundEvent>> ParseWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var events = new List<NormalizedInboundEvent>();

        WebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WebhookEnvelope>(request.Body);
        }
        catch (JsonException)
        {
            return Task.FromResult<IReadOnlyList<NormalizedInboundEvent>>(events);
        }

        foreach (var entry in envelope?.Entry ?? [])
        {
            // entry.id is the receiving Instagram account's own IG-scoped id — the account this
            // webhook event is addressed to (analogous to WhatsApp's phone_number_id).
            var receivingAccountId = entry.Id;
            if (string.IsNullOrEmpty(receivingAccountId))
            {
                continue;
            }

            foreach (var item in entry.Messaging ?? [])
            {
                var senderId = item.Sender?.Id;
                var messageId = item.Message?.Mid;

                if (item.Message is not null && !string.IsNullOrEmpty(messageId) && !string.IsNullOrEmpty(senderId))
                {
                    var attachmentType = item.Message.Attachments?.FirstOrDefault()?.Type;
                    events.Add(new NormalizedInboundEvent(
                        NormalizedInboundEventKind.Message,
                        receivingAccountId,
                        messageId,
                        VisitorExternalId: senderId,
                        Text: item.Message.Text ?? (attachmentType is null
                            ? "[unsupported message content]"
                            : $"[{attachmentType} message — media handling not yet implemented]"),
                        ContentType: MapContentType(attachmentType),
                        OccurredAt: ParseUnixTimestampMilliseconds(item.Timestamp)));
                }

                if (item.Delivery is not null)
                {
                    foreach (var deliveredMid in item.Delivery.Mids ?? [])
                    {
                        events.Add(new NormalizedInboundEvent(
                            NormalizedInboundEventKind.StatusUpdate, receivingAccountId, deliveredMid,
                            Status: MessageDeliveryStatus.Delivered, OccurredAt: ParseUnixTimestampMilliseconds(item.Timestamp)));
                    }
                }

                if (item.Read?.Mid is { } readMid)
                {
                    events.Add(new NormalizedInboundEvent(
                        NormalizedInboundEventKind.StatusUpdate, receivingAccountId, readMid,
                        Status: MessageDeliveryStatus.Read, OccurredAt: ParseUnixTimestampMilliseconds(item.Timestamp)));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<NormalizedInboundEvent>>(events);
    }

    public async Task<ChannelSendResult> SendMessageAsync(ChannelSendRequest request, CancellationToken cancellationToken)
    {
        var url = $"{_options.GraphApiBaseUrl}/{_options.GraphApiVersion}/{request.ExternalAccountId}/messages";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.DecryptedCredential);
        httpRequest.Content = JsonContent.Create(new SendMessagePayload(new SendRecipient(request.RecipientExternalId), new SendTextMessage(request.Text)));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChannelSendException(ChannelSendErrorKind.Transient, $"Network error calling Instagram API: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChannelSendException(ChannelSendErrorKind.Transient, $"Timeout calling Instagram API: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var success = TryDeserialize<SendResponseSuccess>(body);
            return string.IsNullOrEmpty(success?.MessageId)
                ? ChannelSendResult.Failed(ChannelSendErrorKind.Transient, "Instagram API returned 2xx with no message id.")
                : ChannelSendResult.Ok(success.MessageId);
        }

        var errorPayload = TryDeserialize<SendResponseError>(body);
        var code = errorPayload?.Error?.Code;
        var kind = ClassifyError((int)response.StatusCode, code);
        var detail = errorPayload?.Error?.Message ?? $"Instagram API returned {(int)response.StatusCode}.";

        return kind is ChannelSendErrorKind.Transient or ChannelSendErrorKind.RateLimited
            ? throw new ChannelSendException(kind.Value, detail)
            : ChannelSendResult.Failed(kind ?? ChannelSendErrorKind.PermanentFailure, detail);
    }

    // Error codes per ADR-0018's research: 190 = expired/invalid token (shared OAuth error code
    // across Meta's Graph API family, same as WhatsApp); 4/17/32/613/HTTP 429 = rate/usage
    // limits. No Instagram-specific "invalid recipient" code was confirmed during research (unlike
    // WhatsApp's documented 131026) — an unrecognized code defaults to PermanentFailure rather
    // than guessing it's safe to retry, per AGENTS.md's "default behavior should be conservative."
    private static ChannelSendErrorKind? ClassifyError(int httpStatus, int? errorCode) => (httpStatus, errorCode) switch
    {
        (401, _) or (_, 190) => ChannelSendErrorKind.AuthFailed,
        (429, _) or (_, 4) or (_, 17) or (_, 32) or (_, 613) => ChannelSendErrorKind.RateLimited,
        (>= 500, _) => ChannelSendErrorKind.Transient,
        _ => ChannelSendErrorKind.PermanentFailure,
    };

    private static MessageContentType MapContentType(string? attachmentType) => attachmentType switch
    {
        "image" => MessageContentType.Image,
        "video" => MessageContentType.Video,
        "audio" => MessageContentType.Audio,
        "file" => MessageContentType.Document,
        _ => MessageContentType.Text,
    };

    private static DateTimeOffset? ParseUnixTimestampMilliseconds(long? milliseconds)
        => milliseconds.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value) : null;

    private static string ComputeHmacHex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // ---- Wire DTOs (Instagram Messaging API — Instagram Login model; see ADR-0018) ----

    private sealed record WebhookEnvelope([property: JsonPropertyName("entry")] List<WebhookEntry>? Entry);

    private sealed record WebhookEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("messaging")] List<WebhookMessagingItem>? Messaging);

    private sealed record WebhookMessagingItem(
        [property: JsonPropertyName("sender")] WebhookParty? Sender,
        [property: JsonPropertyName("recipient")] WebhookParty? Recipient,
        [property: JsonPropertyName("timestamp")] long? Timestamp,
        [property: JsonPropertyName("message")] WebhookMessage? Message,
        [property: JsonPropertyName("delivery")] WebhookDelivery? Delivery,
        [property: JsonPropertyName("read")] WebhookRead? Read);

    private sealed record WebhookParty([property: JsonPropertyName("id")] string? Id);

    private sealed record WebhookMessage(
        [property: JsonPropertyName("mid")] string? Mid,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("attachments")] List<WebhookAttachment>? Attachments);

    private sealed record WebhookAttachment([property: JsonPropertyName("type")] string? Type);

    private sealed record WebhookDelivery([property: JsonPropertyName("mids")] List<string>? Mids);

    private sealed record WebhookRead([property: JsonPropertyName("mid")] string? Mid);

    private sealed record SendMessagePayload(
        [property: JsonPropertyName("recipient")] SendRecipient Recipient,
        [property: JsonPropertyName("message")] SendTextMessage Message);

    private sealed record SendRecipient([property: JsonPropertyName("id")] string Id);

    private sealed record SendTextMessage([property: JsonPropertyName("text")] string Text);

    private sealed record SendResponseSuccess([property: JsonPropertyName("message_id")] string? MessageId);

    private sealed record SendResponseError([property: JsonPropertyName("error")] SendResponseErrorDetail? Error);

    private sealed record SendResponseErrorDetail(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("code")] int? Code);
}
