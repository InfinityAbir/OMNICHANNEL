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
/// Facebook Messenger Platform adapter (Phase 9, PRD §68). Research and decisions in ADR-0019;
/// summary: same Graph API webhook envelope as Instagram (`{object, entry:[{id, messaging:[...]}]}`,
/// millisecond timestamps), but the Send API passes the access token as a **query-string
/// parameter**, not a Bearer header — genuinely different from both WhatsApp and Instagram, found
/// by this phase's own research rather than assumed identical to the other two. Delivery receipts
/// map when the webhook includes explicit `mids` (documented as not always present); read
/// receipts carry only a `watermark` timestamp with no per-message id, so they cannot be mapped to
/// a specific message under this pipeline's id-based status model — not implemented, not silently
/// guessed at.
/// </summary>
public sealed class MessengerChannelAdapter(HttpClient httpClient, IOptions<MessengerOptions> options) : IChannelAdapter
{
    private readonly MessengerOptions _options = options.Value;

    public ChannelType Type => ChannelType.Messenger;

    public ChannelCapabilities Capabilities { get; } = new(
        MaxTextLength: 2000, SupportsMedia: false, SupportsDeliveryReceipts: true, SupportsReadReceipts: false, HasMessagingWindow: true);

    public Task<WebhookVerificationResult> VerifyWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
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
            return Task.FromResult(WebhookVerificationResult.Invalid("Messenger App Secret is not configured."));
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
            var pageId = entry.Id;
            if (string.IsNullOrEmpty(pageId))
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
                        pageId,
                        messageId,
                        VisitorExternalId: senderId,
                        Text: item.Message.Text ?? (attachmentType is null
                            ? "[unsupported message content]"
                            : $"[{attachmentType} message — media handling not yet implemented]"),
                        ContentType: MapContentType(attachmentType),
                        OccurredAt: ParseUnixTimestampMilliseconds(item.Timestamp)));
                }

                // Delivery receipts sometimes include explicit mids (not guaranteed — Meta's own
                // docs note backward-compatibility gaps); when present, map them. "Read" events
                // carry only a watermark timestamp with no message id and are intentionally not
                // mapped (see class remarks) — a real documented limitation, not an oversight.
                foreach (var deliveredMid in item.Delivery?.Mids ?? [])
                {
                    events.Add(new NormalizedInboundEvent(
                        NormalizedInboundEventKind.StatusUpdate, pageId, deliveredMid,
                        Status: MessageDeliveryStatus.Delivered, OccurredAt: ParseUnixTimestampMilliseconds(item.Timestamp)));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<NormalizedInboundEvent>>(events);
    }

    public async Task<ChannelSendResult> SendMessageAsync(ChannelSendRequest request, CancellationToken cancellationToken)
    {
        // Send API auth is a query-string access_token, not a Bearer header — the one mechanic
        // that differs from both WhatsApp and Instagram (ADR-0019); found by checking rather than
        // assuming all three Meta channels share the same auth transport.
        var url = $"{_options.GraphApiBaseUrl}/{_options.GraphApiVersion}/{request.ExternalAccountId}/messages" +
            $"?access_token={Uri.EscapeDataString(request.DecryptedCredential)}";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                url, new SendMessagePayload(new SendRecipient(request.RecipientExternalId), "RESPONSE", new SendTextMessage(request.Text)), cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChannelSendException(ChannelSendErrorKind.Transient, $"Network error calling Messenger API: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChannelSendException(ChannelSendErrorKind.Transient, $"Timeout calling Messenger API: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var success = TryDeserialize<SendResponseSuccess>(body);
            return string.IsNullOrEmpty(success?.MessageId)
                ? ChannelSendResult.Failed(ChannelSendErrorKind.Transient, "Messenger API returned 2xx with no message id.")
                : ChannelSendResult.Ok(success.MessageId);
        }

        var errorPayload = TryDeserialize<SendResponseError>(body);
        var code = errorPayload?.Error?.Code;
        var kind = ClassifyError((int)response.StatusCode, code);
        var detail = errorPayload?.Error?.Message ?? $"Messenger API returned {(int)response.StatusCode}.";

        return kind is ChannelSendErrorKind.Transient or ChannelSendErrorKind.RateLimited
            ? throw new ChannelSendException(kind.Value, detail)
            : ChannelSendResult.Failed(kind ?? ChannelSendErrorKind.PermanentFailure, detail);
    }

    // Same Graph API error family confirmed for Instagram (ADR-0018), re-confirmed for Messenger
    // this phase: 190 = expired/invalid token; 4/17/32/613/HTTP 429 = rate/usage limits.
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

    // ---- Wire DTOs (Messenger Platform — see ADR-0019) ----

    private sealed record WebhookEnvelope([property: JsonPropertyName("entry")] List<WebhookEntry>? Entry);

    private sealed record WebhookEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("messaging")] List<WebhookMessagingItem>? Messaging);

    private sealed record WebhookMessagingItem(
        [property: JsonPropertyName("sender")] WebhookParty? Sender,
        [property: JsonPropertyName("recipient")] WebhookParty? Recipient,
        [property: JsonPropertyName("timestamp")] long? Timestamp,
        [property: JsonPropertyName("message")] WebhookMessage? Message,
        [property: JsonPropertyName("delivery")] WebhookDelivery? Delivery);

    private sealed record WebhookParty([property: JsonPropertyName("id")] string? Id);

    private sealed record WebhookMessage(
        [property: JsonPropertyName("mid")] string? Mid,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("attachments")] List<WebhookAttachment>? Attachments);

    private sealed record WebhookAttachment([property: JsonPropertyName("type")] string? Type);

    private sealed record WebhookDelivery([property: JsonPropertyName("mids")] List<string>? Mids);

    private sealed record SendMessagePayload(
        [property: JsonPropertyName("recipient")] SendRecipient Recipient,
        [property: JsonPropertyName("messaging_type")] string MessagingType,
        [property: JsonPropertyName("message")] SendTextMessage Message);

    private sealed record SendRecipient([property: JsonPropertyName("id")] string Id);

    private sealed record SendTextMessage([property: JsonPropertyName("text")] string Text);

    private sealed record SendResponseSuccess([property: JsonPropertyName("message_id")] string? MessageId);

    private sealed record SendResponseError([property: JsonPropertyName("error")] SendResponseErrorDetail? Error);

    private sealed record SendResponseErrorDetail(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("code")] int? Code);
}
