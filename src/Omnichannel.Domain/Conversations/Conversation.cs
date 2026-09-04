using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Conversations;

/// <summary>Normalized internal representation of a customer interaction — PRD §15.</summary>
public sealed class Conversation : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid ChannelAccountId { get; private set; }
    public ConversationStatus Status { get; private set; } = ConversationStatus.Open;
    public ConversationPriority Priority { get; private set; } = ConversationPriority.Normal;
    public Guid? AssignedUserId { get; private set; }
    public ConversationAiMode AiMode { get; private set; } = ConversationAiMode.Disabled;
    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>Truncated preview of the most recent message — denormalized so the inbox list
    /// query never needs a per-row join to the messages table.</summary>
    public string? LastMessagePreview { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    private Conversation()
    {
    }

    public static Conversation Create(Guid tenantId, Guid contactId, Guid channelAccountId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            ChannelAccountId = channelAccountId,
            Status = ConversationStatus.Open,
            Priority = ConversationPriority.Normal,
            AiMode = ConversationAiMode.Disabled,
            LastMessageAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void TouchLastMessage(DateTimeOffset now, string messageText)
    {
        LastMessageAt = now;
        LastMessagePreview = messageText.Length > 140 ? string.Concat(messageText.AsSpan(0, 140), "…") : messageText;
        UpdatedAt = now;
    }

    public void AssignTo(Guid userId, DateTimeOffset now)
    {
        AssignedUserId = userId;
        UpdatedAt = now;
    }

    public void Unassign(DateTimeOffset now)
    {
        AssignedUserId = null;
        UpdatedAt = now;
    }

    public void ChangeStatus(ConversationStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;

        if (status == ConversationStatus.Closed)
        {
            ClosedAt = now;
        }
        else
        {
            ClosedAt = null;
        }
    }

    public void SetPriority(ConversationPriority priority, DateTimeOffset now)
    {
        Priority = priority;
        UpdatedAt = now;
    }

    public void SetAiMode(ConversationAiMode mode, DateTimeOffset now)
    {
        AiMode = mode;
        UpdatedAt = now;
    }
}
