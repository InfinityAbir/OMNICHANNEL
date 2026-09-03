using Omnichannel.Domain.Conversations;

namespace Omnichannel.UnitTests.Domain;

public class ConversationTests
{
    [Fact]
    public void Create_SetsOpenStatusAndNormalPriority()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(ConversationStatus.Open, conversation.Status);
        Assert.Equal(ConversationPriority.Normal, conversation.Priority);
        Assert.Null(conversation.AssignedUserId);
    }

    [Fact]
    public void ChangeStatus_ToClosed_SetsClosedAt()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        conversation.ChangeStatus(ConversationStatus.Closed, now);

        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Equal(now, conversation.ClosedAt);
    }

    [Fact]
    public void ChangeStatus_ReopenAfterClosed_ClearsClosedAt()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        conversation.ChangeStatus(ConversationStatus.Closed, DateTimeOffset.UtcNow);

        conversation.ChangeStatus(ConversationStatus.Open, DateTimeOffset.UtcNow);

        Assert.Null(conversation.ClosedAt);
    }

    [Fact]
    public void AssignTo_SetsAssignedUserId()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var userId = Guid.NewGuid();

        conversation.AssignTo(userId, DateTimeOffset.UtcNow);

        Assert.Equal(userId, conversation.AssignedUserId);
    }

    [Fact]
    public void Unassign_ClearsAssignedUserId()
    {
        var conversation = Conversation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        conversation.AssignTo(Guid.NewGuid(), DateTimeOffset.UtcNow);

        conversation.Unassign(DateTimeOffset.UtcNow);

        Assert.Null(conversation.AssignedUserId);
    }
}
