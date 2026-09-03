using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Conversations;

public sealed class ConversationTag : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid TagId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ConversationTag()
    {
    }

    public static ConversationTag Create(Guid tenantId, Guid conversationId, Guid tagId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            TagId = tagId,
            CreatedAt = now,
        };
}
