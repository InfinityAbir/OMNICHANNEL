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
}
