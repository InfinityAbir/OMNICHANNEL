using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Tenancy;

public enum MembershipStatus
{
    Active = 0,
    Removed = 1,
}

/// <summary>
/// Links a User to a Tenant with a Role. A user may hold several memberships (one per
/// tenant, or historically more than one if re-invited) — see PRD §11.
/// </summary>
public sealed class TenantMembership : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantMembership()
    {
    }

    public static TenantMembership Create(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            Status = MembershipStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Remove(DateTimeOffset now)
    {
        Status = MembershipStatus.Removed;
        UpdatedAt = now;
    }
}
