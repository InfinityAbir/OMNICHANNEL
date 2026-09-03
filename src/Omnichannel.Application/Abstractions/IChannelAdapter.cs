using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Stable, provider-agnostic seam every external channel (WhatsApp, Instagram, Messenger, ...)
/// implements (AGENTS.md: "isolate each provider behind a channel adapter and a stable
/// application-level interface"). Phase 6 defines this interface and the pipeline that drives
/// it; no production implementation exists until Phase 7 registers the first one (PRD §65: "do
/// not implement all providers at once"). Manual and WebsiteChat are NOT adapters — they predate
/// this abstraction (ADR-0012, ADR-0015) and keep their own bespoke service paths; this interface
/// only needs to cover channels with a real external provider behind a webhook + send API.
/// </summary>
public interface IChannelAdapter
{
    ChannelType Type { get; }

    ChannelCapabilities Capabilities { get; }

    /// <summary>
    /// Verifies an inbound webhook request is genuinely from the provider — signature/HMAC
    /// check, and (for providers that use one, e.g. Meta's GET handshake) a challenge-response
    /// check. Must run before <see cref="ParseWebhookAsync"/> is ever called (AGENTS.md: "verify
    /// webhook signatures, timestamps/nonces when supported... before processing").
    /// </summary>
    Task<WebhookVerificationResult> VerifyWebhookAsync(WebhookRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Normalizes an already-verified webhook payload into zero or more provider-agnostic
    /// events. Must not throw on a single malformed sub-item within a batch payload — skip and
    /// return the rest, so one bad event can't drop an entire delivery.
    /// </summary>
    Task<IReadOnlyList<NormalizedInboundEvent>> ParseWebhookAsync(WebhookRequest request, CancellationToken cancellationToken);

    /// <summary>Sends one outbound message through the provider's API using this account's stored credential.</summary>
    Task<ChannelSendResult> SendMessageAsync(ChannelSendRequest request, CancellationToken cancellationToken);
}

/// <summary>What a channel can do — lets generic code (composer limits, retry policy) stay adapter-agnostic.</summary>
public sealed record ChannelCapabilities(
    int MaxTextLength,
    bool SupportsMedia,
    bool SupportsDeliveryReceipts,
    bool SupportsReadReceipts,
    bool HasMessagingWindow);

/// <summary>A raw inbound HTTP request, provider-agnostic. <see cref="Headers"/> keys are case-insensitive.</summary>
public sealed record WebhookRequest(
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query,
    string Body);

public sealed record WebhookVerificationResult(bool IsValid, string? ChallengeResponse = null, string? FailureReason = null)
{
    public static WebhookVerificationResult Valid(string? challengeResponse = null) => new(true, challengeResponse);

    public static WebhookVerificationResult Invalid(string reason) => new(false, FailureReason: reason);
}

public enum NormalizedInboundEventKind
{
    Message,
    StatusUpdate,
}

/// <summary>
/// One provider-agnostic inbound event. <see cref="ProviderAccountExternalId"/> is the provider's
/// id for the receiving account (e.g. WhatsApp phone_number_id) — the only thing the webhook
/// pipeline uses to resolve which tenant/<see cref="ChannelAccount"/> this belongs to; it is
/// never trusted as a tenant id itself (AGENTS.md: never trust a channel account id supplied by
/// an external, unauthenticated caller without server-side resolution).
/// </summary>
public sealed record NormalizedInboundEvent(
    NormalizedInboundEventKind Kind,
    string ProviderAccountExternalId,
    string ExternalMessageId,
    string? VisitorExternalId = null,
    string? VisitorDisplayName = null,
    string? Text = null,
    MessageContentType ContentType = MessageContentType.Text,
    DateTimeOffset? OccurredAt = null,
    MessageDeliveryStatus? Status = null);

public sealed record ChannelSendRequest(
    Guid ChannelAccountId,
    string ExternalAccountId,
    string RecipientExternalId,
    string Text,
    string DecryptedCredential);

public enum ChannelSendErrorKind
{
    Transient,
    RateLimited,
    AuthFailed,
    InvalidRecipient,
    PermanentFailure,
}

public sealed record ChannelSendResult(bool Success, string? ExternalMessageId = null, ChannelSendErrorKind? ErrorKind = null, string? ErrorDetail = null)
{
    public static ChannelSendResult Ok(string externalMessageId) => new(true, externalMessageId);

    public static ChannelSendResult Failed(ChannelSendErrorKind kind, string detail) => new(false, ErrorKind: kind, ErrorDetail: detail);
}

/// <summary>Thrown by an adapter's SendMessageAsync instead of returning a failed result, when it needs to signal an exceptional (not merely business-level) failure. ChannelSendService treats this identically to a failed ChannelSendResult.</summary>
public sealed class ChannelSendException(ChannelSendErrorKind errorKind, string message) : Exception(message)
{
    public ChannelSendErrorKind ErrorKind { get; } = errorKind;
}
