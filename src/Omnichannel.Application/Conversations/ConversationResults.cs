using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Conversations;

public sealed record TagRef(Guid Id, string Name);

public sealed record ConversationSummary(
    Guid Id, Guid ContactId, string ContactDisplayName, Guid ChannelAccountId, ChannelType ChannelType,
    ConversationStatus Status, ConversationPriority Priority, Guid? AssignedUserId,
    DateTimeOffset LastMessageAt, string? LastMessagePreview, IReadOnlyList<TagRef> Tags);

public sealed record ConversationDetail(
    Guid Id, Guid ContactId, string ContactDisplayName, Guid ChannelAccountId, ChannelType ChannelType,
    ConversationStatus Status, ConversationPriority Priority, Guid? AssignedUserId,
    ConversationAiMode AiMode, DateTimeOffset LastMessageAt, DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt, IReadOnlyList<TagRef> Tags);

public sealed record MessageSummary(
    Guid Id, MessageDirection Direction, MessageSenderType SenderType, MessageContentType ContentType,
    string Text, DateTimeOffset CreatedAt, MessageDeliveryStatus DeliveryStatus);

public sealed record NoteSummary(Guid Id, Guid AuthorUserId, string Text, DateTimeOffset CreatedAt);
