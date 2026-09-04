using Omnichannel.Domain.Common;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Domain.Automation;

/// <summary>
/// A single keyword-triggered rule (PRD §72: "escalation rules" + "basic automation" — modeled as
/// one bounded concept rather than two, since both are "when inbound text matches X, do Y").
/// Deliberately a closed set of trigger types and actions, never arbitrary code — PRD §72's
/// security review explicitly requires rules "cannot execute arbitrary code."
/// </summary>
public sealed class AutomationRule : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool Enabled { get; private set; } = true;

    /// <summary>Case-insensitive substring match against the triggering inbound message text.</summary>
    public string Keyword { get; private set; } = string.Empty;

    public string? ApplyTagName { get; private set; }
    public ConversationPriority? SetPriority { get; private set; }

    /// <summary>Escalates the conversation (<see cref="ConversationStatus.Escalated"/>) and notifies the tenant's owners/admins — the same PRD §72 "escalation rules" concept, expressed as one of this rule's possible actions rather than a separate entity.</summary>
    public bool Escalate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AutomationRule()
    {
    }

    public static AutomationRule Create(
        Guid tenantId, string name, string keyword, string? applyTagName, ConversationPriority? setPriority, bool escalate, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("Keyword is required.", nameof(keyword));
        }

        if (applyTagName is null && setPriority is null && !escalate)
        {
            throw new ArgumentException("A rule must have at least one action.", nameof(escalate));
        }

        return new AutomationRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = string.IsNullOrWhiteSpace(name) ? keyword.Trim() : name.Trim(),
            Enabled = true,
            Keyword = keyword.Trim(),
            ApplyTagName = string.IsNullOrWhiteSpace(applyTagName) ? null : applyTagName.Trim(),
            SetPriority = setPriority,
            Escalate = escalate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAt = now;
    }

    /// <summary>Case-insensitive substring match, ordinal — no regex/scripting (PRD §72: no arbitrary code execution).</summary>
    public bool Matches(string messageText)
        => Enabled && !string.IsNullOrWhiteSpace(messageText)
            && messageText.Contains(Keyword, StringComparison.OrdinalIgnoreCase);
}
