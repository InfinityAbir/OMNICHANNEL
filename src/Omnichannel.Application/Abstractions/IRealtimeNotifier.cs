using Omnichannel.Contracts.Realtime;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Abstractions;

public interface IRealtimeNotifier
{
    Task NotifyNewMessageAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        MessageDirection direction,
        MessageSenderType senderType,
        MessageContentType contentType,
        string text,
        DateTimeOffset createdAt,
        MessageDeliveryStatus deliveryStatus,
        string? externalMessageId,
        CancellationToken cancellationToken);

    Task NotifyConversationUpdateAsync(
        Guid tenantId,
        Guid conversationId,
        ConversationStatus? status,
        ConversationPriority? priority,
        ConversationAiMode? aiMode,
        DateTimeOffset? lastMessageAt,
        string? lastMessagePreview,
        Guid? assignedUserId,
        CancellationToken cancellationToken);

    Task NotifyAssignmentUpdateAsync(
        Guid tenantId,
        Guid conversationId,
        Guid? assignedUserId,
        string assignedUserName,
        CancellationToken cancellationToken);

    Task NotifyMessageStatusAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        MessageDeliveryStatus deliveryStatus,
        DateTimeOffset? sentAt,
        DateTimeOffset? deliveredAt,
        DateTimeOffset? readAt,
        CancellationToken cancellationToken);

    Task NotifyHighPriorityAlertAsync(
        Guid tenantId,
        Guid conversationId,
        string title,
        string body,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pushes a conversation message to the visitor-facing widget hub (group
    /// <c>conversation:{conversationId}</c>). Used to stream agent outbound replies straight to the
    /// website-chat widget without the visitor joining the tenant's agent group.
    /// </summary>
    Task NotifyVisitorMessageAsync(
        Guid conversationId,
        Guid messageId,
        string direction,
        string senderType,
        string contentType,
        string text,
        DateTimeOffset createdAt,
        string deliveryStatus,
        CancellationToken cancellationToken);
}