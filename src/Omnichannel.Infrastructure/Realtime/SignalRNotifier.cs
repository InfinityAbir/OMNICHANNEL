using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Realtime;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Infrastructure.Realtime;

public sealed class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<InboxHub> _hubContext;
    private readonly IHubContext<WidgetHub> _widgetHubContext;

    public SignalRNotifier(IHubContext<InboxHub> hubContext, IHubContext<WidgetHub> widgetHubContext)
    {
        _hubContext = hubContext;
        _widgetHubContext = widgetHubContext;
    }

    public Task NotifyNewMessageAsync(
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
        CancellationToken cancellationToken)
    {
        var @event = new NewMessageEvent(
            ConversationId: conversationId,
            MessageId: messageId,
            Direction: direction.ToString(),
            SenderType: senderType.ToString(),
            ContentType: contentType.ToString(),
            Text: text,
            CreatedAt: createdAt,
            DeliveryStatus: deliveryStatus.ToString(),
            ExternalMessageId: externalMessageId);

        return _hubContext.Clients.Group($"tenant:{tenantId}")
            .SendAsync(InboxHubEventTypes.NewMessage, @event, cancellationToken);
    }

    public Task NotifyConversationUpdateAsync(
        Guid tenantId,
        Guid conversationId,
        ConversationStatus? status,
        ConversationPriority? priority,
        ConversationAiMode? aiMode,
        DateTimeOffset? lastMessageAt,
        string? lastMessagePreview,
        Guid? assignedUserId,
        CancellationToken cancellationToken)
    {
        var @event = new ConversationUpdateEvent(
            ConversationId: conversationId,
            Status: status?.ToString(),
            Priority: priority?.ToString(),
            AiMode: aiMode?.ToString(),
            LastMessageAt: lastMessageAt,
            LastMessagePreview: lastMessagePreview,
            AssignedUserId: assignedUserId);

        return _hubContext.Clients.Group($"tenant:{tenantId}")
            .SendAsync(InboxHubEventTypes.ConversationUpdate, @event, cancellationToken);
    }

    public Task NotifyAssignmentUpdateAsync(
        Guid tenantId,
        Guid conversationId,
        Guid? assignedUserId,
        string assignedUserName,
        CancellationToken cancellationToken)
    {
        var @event = new AssignmentUpdateEvent(
            ConversationId: conversationId,
            AssignedUserId: assignedUserId,
            AssignedUserName: assignedUserName);

        return _hubContext.Clients.Group($"tenant:{tenantId}")
            .SendAsync(InboxHubEventTypes.AssignmentUpdate, @event, cancellationToken);
    }

    public Task NotifyMessageStatusAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        MessageDeliveryStatus deliveryStatus,
        DateTimeOffset? sentAt,
        DateTimeOffset? deliveredAt,
        DateTimeOffset? readAt,
        CancellationToken cancellationToken)
    {
        var @event = new MessageStatusEvent(
            ConversationId: conversationId,
            MessageId: messageId,
            DeliveryStatus: deliveryStatus.ToString(),
            SentAt: sentAt,
            DeliveredAt: deliveredAt,
            ReadAt: readAt);

        return _hubContext.Clients.Group($"tenant:{tenantId}")
            .SendAsync(InboxHubEventTypes.MessageStatus, @event, cancellationToken);
    }

    public Task NotifyHighPriorityAlertAsync(
        Guid tenantId,
        Guid conversationId,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var @event = new NotificationEvent(
            ConversationId: conversationId,
            Type: NotificationTypes.HighPriorityAlert,
            Title: title,
            Body: body,
            Severity: NotificationSeverity.Critical);

        return _hubContext.Clients.Group($"tenant:{tenantId}")
            .SendAsync(InboxHubEventTypes.Notification, @event, cancellationToken);
    }

    public Task NotifyVisitorMessageAsync(
        Guid conversationId,
        Guid messageId,
        string direction,
        string senderType,
        string contentType,
        string text,
        DateTimeOffset createdAt,
        string deliveryStatus,
        CancellationToken cancellationToken)
    {
        var @event = new NewMessageEvent(
            ConversationId: conversationId,
            MessageId: messageId,
            Direction: direction,
            SenderType: senderType,
            ContentType: contentType,
            Text: text,
            CreatedAt: createdAt,
            DeliveryStatus: deliveryStatus);

        return _widgetHubContext.Clients.Group($"conversation:{conversationId}")
            .SendAsync(InboxHubEventTypes.NewMessage, @event, cancellationToken);
    }
}

public static class SignalRServiceCollectionExtensions
{
    public static IServiceCollection AddSignalRNotifier(this IServiceCollection services)
    {
        services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
        return services;
    }
}