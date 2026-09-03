using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Contacts;

/// <summary>Normalized customer record — PRD §14/§21. Never exposes more than needed (PRD §21).</summary>
public sealed class Contact : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastInteractionAt { get; private set; }

    private Contact()
    {
    }

    public static Contact Create(Guid tenantId, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        return new Contact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = displayName.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Rename(string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        DisplayName = displayName.Trim();
        UpdatedAt = now;
    }

    public void TouchLastInteraction(DateTimeOffset now)
    {
        LastInteractionAt = now;
        UpdatedAt = now;
    }
}
