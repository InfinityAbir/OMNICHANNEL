namespace Omnichannel.Contracts.Notifications;

public sealed record NotificationResponse(
    Guid Id, string Type, string Title, string Body, Guid? ConversationId, bool Read, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record UnreadCountResponse(int Count);
