namespace Omnichannel.Domain.Tenancy;

public enum TenantStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>
/// A business/customer account. Not itself tenant-owned — it IS the tenant boundary.
/// </summary>
public sealed class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    /// <summary>IANA time zone id (e.g. "Asia/Dhaka"). Business-hours logic must use this, never server local time.</summary>
    public string TimeZone { get; private set; } = "UTC";

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Tenant()
    {
    }

    public static Tenant Create(string name, string slug, string timeZone, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tenant name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Tenant slug is required.", nameof(slug));
        }

        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone,
            Status = TenantStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Suspend(DateTimeOffset now)
    {
        Status = TenantStatus.Suspended;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        Status = TenantStatus.Active;
        UpdatedAt = now;
    }
}
