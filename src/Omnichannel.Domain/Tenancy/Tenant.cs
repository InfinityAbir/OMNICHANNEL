namespace Omnichannel.Domain.Tenancy;

public enum TenantStatus
{
    Active = 0,
    Suspended = 1,

    /// <summary>Scheduled for permanent deletion at <see cref="Tenant.ScheduledDeletionAt"/> —
    /// still exists and its data is intact, but new logins/token refreshes for this tenant are
    /// refused (ADR-0030) and it can still be cancelled before that date.</summary>
    PendingDeletion = 2,

    /// <summary>Terminal state: the tenant's own operational data has been permanently purged
    /// (<c>TenantDataPurgeService</c>). The row itself and its audit trail are kept — see
    /// ADR-0030 for why a purged tenant isn't hard-deleted outright.</summary>
    Deleted = 3,
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

    /// <summary>Set only while <see cref="Status"/> is <see cref="TenantStatus.PendingDeletion"/> —
    /// when <see cref="TenantDataPurgeService"/> will permanently purge this tenant's data.</summary>
    public DateTimeOffset? ScheduledDeletionAt { get; private set; }

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

    public void ScheduleDeletion(DateTimeOffset scheduledAt, DateTimeOffset now)
    {
        Status = TenantStatus.PendingDeletion;
        ScheduledDeletionAt = scheduledAt;
        UpdatedAt = now;
    }

    public void CancelScheduledDeletion(DateTimeOffset now)
    {
        if (Status != TenantStatus.PendingDeletion)
        {
            throw new InvalidOperationException("Only a tenant pending deletion can have its deletion cancelled.");
        }

        Status = TenantStatus.Active;
        ScheduledDeletionAt = null;
        UpdatedAt = now;
    }

    /// <summary>Called only by the purge job once this tenant's operational data has actually
    /// been removed — a terminal state, never reversed.</summary>
    public void MarkDeleted(DateTimeOffset now)
    {
        Status = TenantStatus.Deleted;
        ScheduledDeletionAt = null;
        UpdatedAt = now;
    }
}
