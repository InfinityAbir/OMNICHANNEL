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
/// WhatsApp Business Platform (Cloud API) adapter — the first real <see cref="IChannelAdapter"/>
/// (Phase 7, PRD §66). Scope decisions and the official-documentation research behind them are
/// recorded in ADR-0017; summary: text-only outbound (no template/media send — the composer has
/// no media UI to drive it, and 24h-window/template rules make untemplated sends unreliable
/// outside the service window by design, not by omission), inbound accepts any message type but
/// only extracts text bodies (non-text content is recorded with a placeholder + the provider's
/// own metadata, not downloaded — matches Phase 6's own deferred media/attachment handling).
/// </summary>
public sealed class WhatsAppChannelAdapter(HttpClient httpClient, IOptions<WhatsAppOptions> options) : IChannelAdapter
{
    private readonly WhatsAppOptions _options = options.Value;

    public ChannelType Type => ChannelType.WhatsApp;

    public ChannelCapabilities Capabilities { get; } = new(
        MaxTextLength: 4096, SupportsMedia: false, SupportsDeliveryReceipts: true, SupportsReadReceipts: true, HasMessagingWindow: true);

    public Task<WebhookVerificationResult> VerifyWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        // GET handshake (Meta docs: "Set up webhooks") — one-time, at subscription setup.
        if (request.Query.TryGetValue("hub.mode", out var mode) && mode == "subscribe")
        {
            var tokenMatches = request.Query.TryGetValue("hub.verify_token", out var token)
                && !string.IsNullOrEmpty(_options.VerifyToken)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_options.VerifyToken));

            return Task.FromResult(tokenMatches && request.Query.TryGetValue("hub.challenge", out var challenge)
                ? WebhookVerificationResult.Valid(challenge)
                : WebhookVerificationResult.Invalid("hub.verify_token mismatch."));
        }

        // POST delivery — every notification is HMAC-SHA256 signed over the raw body with the
        // App Secret (Meta docs: "Graph API webhooks getting started"), header
        // X-Hub-Signature-256: sha256=<hex>.
        if (!request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader) ||
            !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return Task.FromResult(WebhookVerificationResult.Invalid("Missing X-Hub-Signature-256 header."));
        }

        if (string.IsNullOrEmpty(_options.AppSecret))
        {
            return Task.FromResult(WebhookVerificationResult.Invalid("WhatsApp App Secret is not configured."));
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

        foreach (var change in envelope?.Entry ?? [])
        {
            foreach (var item in change.Changes ?? [])
            {
                var value = item.Value;
                var phoneNumberId = value?.Metadata?.PhoneNumberId;
                if (string.IsNullOrEmpty(phoneNumberId))
                {
                    continue;
                }

                var contactName = value!.Contacts?.FirstOrDefault()?.Profile?.Name;

                foreach (var message in value.Messages ?? [])
                {
                    if (string.IsNullOrEmpty(message.Id) || string.IsNullOrEmpty(message.From))
                    {
                        continue;
                    }

                    events.Add(new NormalizedInboundEvent(
                        NormalizedInboundEventKind.Message,
                        phoneNumberId,
                        message.Id,
                        VisitorExternalId: message.From,
                        VisitorDisplayName: contactName,
                        Text: message.Text?.Body ?? $"[{message.Type ?? "unsupported"} message — media handling not yet implemented]",
                        ContentType: MapContentType(message.Type),
                        OccurredAt: ParseUnixTimestamp(message.Timestamp)));
                }

                foreach (var status in value.Statuses ?? [])
                {
                    if (string.IsNullOrEmpty(status.Id) || string.IsNullOrEmpty(status.Status))
                    {
                        continue;
                    }

                    var mappedStatus = MapStatus(status.Status);
                    if (mappedStatus is null)
                    {
                        continue;
                    }

                    events.Add(new NormalizedInboundEvent(
                        NormalizedInboundEventKind.StatusUpdate,
                        phoneNumberId,
                        status.Id,
                        Status: mappedStatus,
                        OccurredAt: ParseUnixTimestamp(status.Timestamp)));
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
        httpRequest.Content = JsonContent.Create(new SendMessagePayload(
            "whatsapp", "individual", request.RecipientExternalId, "text", new SendTextBody(request.Text)));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChannelSendException(ChannelSendErrorKind.Transient, $"Network error calling WhatsApp API: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChannelSendException(ChannelSendErrorKind.Transient, $"Timeout calling WhatsApp API: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var success = JsonSerializer.Deserialize<SendResponseSuccess>(body);
            var externalId = success?.Messages?.FirstOrDefault()?.Id;
            return string.IsNullOrEmpty(externalId)
                ? ChannelSendResult.Failed(ChannelSendErrorKind.Transient, "WhatsApp API returned 2xx with no message id.")
                : ChannelSendResult.Ok(externalId);
        }

        var errorPayload = TryDeserialize<SendResponseError>(body);
        var code = errorPayload?.Error?.Code;
        var kind = ClassifyError((int)response.StatusCode, code);
        var detail = errorPayload?.Error?.Message ?? $"WhatsApp API returned {(int)response.StatusCode}.";

        return kind is ChannelSendErrorKind.Transient or ChannelSendErrorKind.RateLimited
            ? throw new ChannelSendException(kind.Value, detail)
            : ChannelSendResult.Failed(kind ?? ChannelSendErrorKind.PermanentFailure, detail);
    }

    // Error codes per Meta's published Cloud API error reference (ADR-0017): 190 = expired/invalid
    // token; 4, 80007, 130429 = throughput/rate limits; 131047 = outside the 24h service window
    // (needs a template — not supported by this adapter yet, so it's a permanent failure for us,
    // not a retryable one); 131026 = recipient unreachable/not on WhatsApp; 5xx/429 = transient.
    private static ChannelSendErrorKind? ClassifyError(int httpStatus, int? errorCode) => (httpStatus, errorCode) switch
    {
        (401, _) or (_, 190) => ChannelSendErrorKind.AuthFailed,
        (429, _) or (_, 4) or (_, 80007) or (_, 130429) => ChannelSendErrorKind.RateLimited,
        (_, 131026) => ChannelSendErrorKind.InvalidRecipient,
        (_, 131047) => ChannelSendErrorKind.PermanentFailure,
        (>= 500, _) => ChannelSendErrorKind.Transient,
        _ => ChannelSendErrorKind.PermanentFailure,
    };

    private static MessageContentType MapContentType(string? whatsAppType) => whatsAppType switch
    {
        "image" => MessageContentType.Image,
        "document" => MessageContentType.Document,
        "audio" => MessageContentType.Audio,
        "video" => MessageContentType.Video,
        _ => MessageContentType.Text,
    };

    private static MessageDeliveryStatus? MapStatus(string status) => status switch
    {
        "sent" => MessageDeliveryStatus.Sent,
        "delivered" => MessageDeliveryStatus.Delivered,
        "read" => MessageDeliveryStatus.Read,
        "failed" => MessageDeliveryStatus.Failed,
        _ => null,
    };

    private static DateTimeOffset? ParseUnixTimestamp(string? seconds)
        => long.TryParse(seconds, out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;

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

    // ---- Wire DTOs (Meta Cloud API — see ADR-0017 for the documentation this was verified against) ----

    private sealed record WebhookEnvelope([property: JsonPropertyName("entry")] List<WebhookEntry>? Entry);

    private sealed record WebhookEntry([property: JsonPropertyName("changes")] List<WebhookChange>? Changes);

    private sealed record WebhookChange([property: JsonPropertyName("value")] WebhookChangeValue? Value);

    private sealed record WebhookChangeValue(
        [property: JsonPropertyName("metadata")] WebhookMetadata? Metadata,
        [property: JsonPropertyName("contacts")] List<WebhookContact>? Contacts,
        [property: JsonPropertyName("messages")] List<WebhookMessage>? Messages,
        [property: JsonPropertyName("statuses")] List<WebhookStatus>? Statuses);

    private sealed record WebhookMetadata([property: JsonPropertyName("phone_number_id")] string? PhoneNumberId);

    private sealed record WebhookContact([property: JsonPropertyName("profile")] WebhookProfile? Profile);

    private sealed record WebhookProfile([property: JsonPropertyName("name")] string? Name);

    private sealed record WebhookMessage(
        [property: JsonPropertyName("from")] string? From,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("timestamp")] string? Timestamp,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] WebhookTextBody? Text);

    private sealed record WebhookTextBody([property: JsonPropertyName("body")] string? Body);

    private sealed record WebhookStatus(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("timestamp")] string? Timestamp);

    private sealed record SendMessagePayload(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("recipient_type")] string RecipientType,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] SendTextBody Text);

    private sealed record SendTextBody([property: JsonPropertyName("body")] string Body);

    private sealed record SendResponseSuccess([property: JsonPropertyName("messages")] List<SendResponseMessageId>? Messages);

    private sealed record SendResponseMessageId([property: JsonPropertyName("id")] string? Id);

    private sealed record SendResponseError([property: JsonPropertyName("error")] SendResponseErrorDetail? Error);

    private sealed record SendResponseErrorDetail(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("code")] int? Code);
}
