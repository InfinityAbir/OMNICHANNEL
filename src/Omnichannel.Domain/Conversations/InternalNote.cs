using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Conversations;

/// <summary>Staff-only annotation on a conversation — never sent to the customer.</summary>
public sealed class InternalNote : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private InternalNote()
    {
    }

    public static InternalNote Create(Guid tenantId, Guid conversationId, Guid authorUserId, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Note text is required.", nameof(text));
        }

        return new InternalNote
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            AuthorUserId = authorUserId,
            Text = text.Trim(),
            CreatedAt = now,
        };
    }
}
