namespace Omnichannel.Domain.Conversations;

/// <summary>PRD §16.</summary>
public enum MessageDirection
{
    Inbound = 0,
    Outbound = 1,
}

public enum MessageSenderType
{
    Customer = 0,
    Agent = 1,
    Ai = 2,
    System = 3,
}

public enum MessageContentType
{
    Text = 0,
    Image = 1,
    Document = 2,
    Audio = 3,
    Video = 4,
}

/// <summary>PRD §19.</summary>
public enum MessageDeliveryStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
}
