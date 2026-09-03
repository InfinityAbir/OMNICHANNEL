namespace Omnichannel.Contracts.Realtime;

public sealed record NewMessageEvent(
    Guid ConversationId,
    Guid MessageId,
    string Direction,
    string SenderType,
    string ContentType,
    string Text,
    DateTimeOffset CreatedAt,
    string DeliveryStatus,
    string? ExternalMessageId = null)
{
    public string EventId => MessageId.ToString();
}

public sealed record ConversationUpdateEvent(
    Guid ConversationId,
    string? Status = null,
    string? Priority = null,
    string? AiMode = null,
    DateTimeOffset? LastMessageAt = null,
    string? LastMessagePreview = null,
    Guid? AssignedUserId = null)
{
    public string EventId => ConversationId.ToString();
}

public sealed record AssignmentUpdateEvent(
    Guid ConversationId,
    Guid? AssignedUserId,
    string AssignedUserName)
{
    public string EventId => ConversationId.ToString();
}

public sealed record MessageStatusEvent(
    Guid ConversationId,
    Guid MessageId,
    string DeliveryStatus,
    DateTimeOffset? SentAt = null,
    DateTimeOffset? DeliveredAt = null,
    DateTimeOffset? ReadAt = null)
{
    public string EventId => MessageId.ToString();
}

public sealed record NotificationEvent(
    Guid ConversationId,
    string Type,
    string Title,
    string Body,
    string Severity)
{
    public string EventId => $"{ConversationId}-{Type}-{DateTimeOffset.UtcNow.Ticks}";
}

public static class InboxHubEventTypes
{
    public const string NewMessage = "new_message";
    public const string ConversationUpdate = "conversation_update";
    public const string AssignmentUpdate = "assignment_update";
    public const string MessageStatus = "message_status";
    public const string Notification = "notification";
}

public static class NotificationTypes
{
    public const string HighPriorityAlert = "high_priority_alert";
}

public static class NotificationSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}
