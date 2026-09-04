using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Automation;

/// <summary>A reusable canned response text (PRD §72 "saved replies") — shared across every agent
/// in the tenant, not per-user, matching how the rest of the product's shared config works.</summary>
public sealed class SavedReply : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SavedReply()
    {
    }

    public static SavedReply Create(Guid tenantId, string title, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        return new SavedReply
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Text = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string title, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        Title = title.Trim();
        Text = text.Trim();
        UpdatedAt = now;
    }
}
