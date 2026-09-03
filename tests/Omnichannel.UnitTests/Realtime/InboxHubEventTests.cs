using Omnichannel.Contracts.Realtime;

namespace Omnichannel.UnitTests.Realtime;

public class InboxHubEventTests
{
    [Fact]
    public void NewMessageEvent_EventId_ReturnsMessageId()
    {
        var messageId = Guid.NewGuid();
        var @event = new NewMessageEvent(
            ConversationId: Guid.NewGuid(),
            MessageId: messageId,
            Direction: "Inbound",
            SenderType: "Customer",
            ContentType: "Text",
            Text: "hello",
            CreatedAt: DateTimeOffset.UtcNow,
            DeliveryStatus: "Delivered");

        Assert.Equal(messageId.ToString(), @event.EventId);
    }

    [Fact]
    public void ConversationUpdateEvent_EventId_ReturnsConversationId()
    {
        var conversationId = Guid.NewGuid();
        var @event = new ConversationUpdateEvent(
            ConversationId: conversationId,
            Status: "Open");

        Assert.Equal(conversationId.ToString(), @event.EventId);
    }

    [Fact]
    public void AssignmentUpdateEvent_EventId_ReturnsConversationId()
    {
        var conversationId = Guid.NewGuid();
        var @event = new AssignmentUpdateEvent(
            ConversationId: conversationId,
            AssignedUserId: Guid.NewGuid(),
            AssignedUserName: "Agent A");

        Assert.Equal(conversationId.ToString(), @event.EventId);
    }

    [Fact]
    public void MessageStatusEvent_EventId_ReturnsMessageId()
    {
        var messageId = Guid.NewGuid();
        var @event = new MessageStatusEvent(
            ConversationId: Guid.NewGuid(),
            MessageId: messageId,
            DeliveryStatus: "Sent");

        Assert.Equal(messageId.ToString(), @event.EventId);
    }

    [Fact]
    public void NotificationEvent_EventId_ContainsConversationIdAndType()
    {
        var conversationId = Guid.NewGuid();
        var @event = new NotificationEvent(
            ConversationId: conversationId,
            Type: NotificationTypes.HighPriorityAlert,
            Title: "Alert",
            Body: "Urgent",
            Severity: NotificationSeverity.Critical);

        Assert.Contains(conversationId.ToString(), @event.EventId);
        Assert.Contains(NotificationTypes.HighPriorityAlert, @event.EventId);
    }

    [Fact]
    public void InboxHubEventTypes_HasAllFiveEvents()
    {
        Assert.Equal(5, new[]
        {
            InboxHubEventTypes.NewMessage,
            InboxHubEventTypes.ConversationUpdate,
            InboxHubEventTypes.AssignmentUpdate,
            InboxHubEventTypes.MessageStatus,
            InboxHubEventTypes.Notification,
        }.Distinct().Count());
    }

    [Fact]
    public void NotificationSeverity_HasAllLevels()
    {
        Assert.False(string.IsNullOrEmpty(NotificationSeverity.Info));
        Assert.False(string.IsNullOrEmpty(NotificationSeverity.Warning));
        Assert.False(string.IsNullOrEmpty(NotificationSeverity.Critical));
    }
}
