namespace Omnichannel.Domain.Identity;

/// <summary>
/// Business-facing user profile. Deliberately separate from the credential/auth record
/// (Infrastructure's ApplicationUser : IdentityUser) so Domain stays framework-free —
/// linked 1:1 by sharing the same Id. Not tenant-owned: a user can belong to several
/// tenants via TenantMembership.
/// </summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private User()
    {
    }

    public static User Create(Guid id, string email, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        return new User
        {
            Id = id,
            Email = email.Trim().ToLowerInvariant(),
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

    /// <summary>
    /// Self-service account deletion (ADR-0030) scrubs the two PII fields this row carries —
    /// never a hard delete of the row itself, since other tables (audit logs, internal notes,
    /// conversation assignment, notifications) reference this Id and must keep resolving to
    /// *something* for historical/audit purposes. The credential record (email, password hash)
    /// is deleted separately, in Infrastructure's Identity store — this only scrubs the
    /// business-facing profile. Email becomes a per-user-unique placeholder (the column has a
    /// unique index) so a future signup can reuse the real address.
    /// </summary>
    public void Anonymize(DateTimeOffset now)
    {
        Email = $"deleted-{Id:N}@deleted.invalid";
        DisplayName = "Deleted user";
        UpdatedAt = now;
    }
}
