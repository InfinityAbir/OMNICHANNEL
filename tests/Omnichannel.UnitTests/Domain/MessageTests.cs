using Omnichannel.Domain.Conversations;

namespace Omnichannel.UnitTests.Domain;

public class MessageTests
{
    [Fact]
    public void CreateInbound_SetsDeliveredStatus()
    {
        var message = Message.CreateInbound(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MessageSenderType.Customer, "hi", DateTimeOffset.UtcNow);

        Assert.Equal(MessageDirection.Inbound, message.Direction);
        Assert.Equal(MessageDeliveryStatus.Delivered, message.DeliveryStatus);
    }

    [Fact]
    public void CreateOutbound_SetsQueuedStatus()
    {
        var message = Message.CreateOutbound(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MessageSenderType.Agent, "hello", DateTimeOffset.UtcNow);

        Assert.Equal(MessageDirection.Outbound, message.Direction);
        Assert.Equal(MessageDeliveryStatus.Queued, message.DeliveryStatus);
    }

    [Fact]
    public void CreateOutbound_WithEmptyText_Throws()
    {
        Assert.Throws<ArgumentException>(() => Message.CreateOutbound(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MessageSenderType.Agent, "  ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkSent_SetsSentAtAndStatus()
    {
        var message = Message.CreateOutbound(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MessageSenderType.Agent, "hello", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        message.MarkSent(now);

        Assert.Equal(MessageDeliveryStatus.Sent, message.DeliveryStatus);
        Assert.Equal(now, message.SentAt);
    }
}
