using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Notifications;

/// <summary>In-app notification feed entry (PRD §72 "notifications") — currently only ever
/// created by <see cref="Automation.AutomationRule"/>'s escalate action, but a general-purpose
/// per-user feed so later phases (e.g. AI escalation, assignment) can reuse it.</summary>
public sealed class Notification : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public Guid? ConversationId { get; private set; }
    public bool Read { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification()
    {
    }

    public static Notification Create(
        Guid tenantId, Guid userId, string type, string title, string body, Guid? conversationId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            ConversationId = conversationId,
            Read = false,
            CreatedAt = now,
        };

    public void MarkRead(DateTimeOffset now)
    {
        if (Read)
        {
            return;
        }

        Read = true;
        ReadAt = now;
    }
}
