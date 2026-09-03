using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Conversations;

/// <summary>Normalized message — PRD §16. ExternalMessageId is null for manually-created messages (Phase 2); real channel adapters (Phase 6+) populate it for idempotent webhook ingestion (PRD §17).</summary>
public sealed class Message : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }

    /// <summary>Denormalized from the conversation at creation time — enables PRD §17's exact idempotency shape, UNIQUE(ChannelAccountId, ExternalMessageId), without a join.</summary>
    public Guid ChannelAccountId { get; private set; }

    public string? ExternalMessageId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public MessageSenderType SenderType { get; private set; }
    public MessageContentType ContentType { get; private set; } = MessageContentType.Text;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public MessageDeliveryStatus DeliveryStatus { get; private set; }
    public string? ProviderMetadata { get; private set; }

    private Message()
    {
    }

    public static Message CreateInbound(
        Guid tenantId, Guid conversationId, Guid channelAccountId, MessageSenderType senderType, string text, DateTimeOffset now,
        string? externalMessageId = null, string? providerMetadata = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text is required.", nameof(text));
        }

        return new Message
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            ChannelAccountId = channelAccountId,
            ExternalMessageId = externalMessageId,
            Direction = MessageDirection.Inbound,
            SenderType = senderType,
            ContentType = MessageContentType.Text,
            Text = text.Trim(),
            CreatedAt = now,
            ReceivedAt = now,
            DeliveryStatus = MessageDeliveryStatus.Delivered,
            ProviderMetadata = providerMetadata,
        };
    }

    public static Message CreateOutbound(
        Guid tenantId, Guid conversationId, Guid channelAccountId, MessageSenderType senderType, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text is required.", nameof(text));
        }

        return new Message
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            ChannelAccountId = channelAccountId,
            Direction = MessageDirection.Outbound,
            SenderType = senderType,
            ContentType = MessageContentType.Text,
            Text = text.Trim(),
            CreatedAt = now,
            DeliveryStatus = MessageDeliveryStatus.Queued,
        };
    }

    public void MarkSent(DateTimeOffset now, string? externalMessageId = null)
    {
        DeliveryStatus = MessageDeliveryStatus.Sent;
        SentAt = now;
        if (externalMessageId is not null)
        {
            ExternalMessageId = externalMessageId;
        }
    }

    public void MarkFailed() => DeliveryStatus = MessageDeliveryStatus.Failed;

    /// <summary>
    /// Applies a delivery-status update reported by a provider webhook (Phase 6+ — a status
    /// update never regresses an already-more-advanced status, since providers can redeliver
    /// status webhooks out of order).
    /// </summary>
    public void ApplyProviderStatus(MessageDeliveryStatus status, DateTimeOffset now)
    {
        if (status <= DeliveryStatus)
        {
            return;
        }

        DeliveryStatus = status;
        switch (status)
        {
            case MessageDeliveryStatus.Delivered:
                DeliveredAt = now;
                break;
            case MessageDeliveryStatus.Read:
                ReadAt = now;
                break;
        }
    }
}
