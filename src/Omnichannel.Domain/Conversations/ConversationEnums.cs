namespace Omnichannel.Domain.Conversations;

/// <summary>PRD §15.</summary>
public enum ConversationStatus
{
    Open = 0,
    Pending = 1,
    WaitingForCustomer = 2,
    WaitingForAgent = 3,
    Escalated = 4,
    Resolved = 5,
    Closed = 6,
}

public enum ConversationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

/// <summary>
/// PRD §15. Not enforced yet — Phase 10/12 add the actual AI decision pipeline. Stored now so
/// the field exists and defaults safely (Disabled) rather than being retrofitted later.
/// </summary>
public enum ConversationAiMode
{
    Disabled = 0,
    SuggestOnly = 1,
    AutoReply = 2,
    AutoReplyWithEscalation = 3,
}
