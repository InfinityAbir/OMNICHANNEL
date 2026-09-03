using System.ComponentModel.DataAnnotations;

namespace Omnichannel.Contracts.Conversations;

public sealed class CreateContactRequest
{
    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record ContactResponse(Guid Id, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset? LastInteractionAt);

public sealed class CreateConversationRequest
{
    public Guid? ContactId { get; init; }

    [MaxLength(200)]
    public string? NewContactDisplayName { get; init; }

    [MaxLength(4000)]
    public string? InitialMessageText { get; init; }
}

public sealed class AddMessageRequest
{
    [Required]
    public string Direction { get; init; } = "Outbound";

    [MaxLength(4000)]
    public string SenderType { get; init; } = "Agent";

    [Required, MaxLength(4000)]
    public string Text { get; init; } = string.Empty;
}

public sealed class AssignConversationRequest
{
    [Required]
    public Guid UserId { get; init; }
}

public sealed class ChangeStatusRequest
{
    [Required]
    public string Status { get; init; } = string.Empty;
}

public sealed class SetPriorityRequest
{
    [Required]
    public string Priority { get; init; } = string.Empty;
}

public sealed class AddTagRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = string.Empty;
}

public sealed class AddNoteRequest
{
    [Required, MaxLength(4000)]
    public string Text { get; init; } = string.Empty;
}

public sealed record ConversationSummaryResponse(
    Guid Id, Guid ContactId, string ContactDisplayName, Guid ChannelAccountId,
    string Status, string Priority, Guid? AssignedUserId, DateTimeOffset LastMessageAt,
    string? LastMessagePreview, IReadOnlyList<TagResponse> Tags);

public sealed record ConversationDetailResponse(
    Guid Id, Guid ContactId, string ContactDisplayName, Guid ChannelAccountId,
    string Status, string Priority, Guid? AssignedUserId, string AiMode,
    DateTimeOffset LastMessageAt, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt,
    IReadOnlyList<TagResponse> Tags);

public sealed record MessageResponse(
    Guid Id, string Direction, string SenderType, string ContentType, string Text,
    DateTimeOffset CreatedAt, string DeliveryStatus);

public sealed record NoteResponse(Guid Id, Guid AuthorUserId, string Text, DateTimeOffset CreatedAt);

public sealed record TagResponse(Guid Id, string Name);

public sealed record KeysetPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
