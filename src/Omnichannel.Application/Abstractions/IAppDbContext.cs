using Microsoft.EntityFrameworkCore;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Typed EF Core surface exposed to Application. Deliberately not a generic Repository&lt;T&gt;
/// wrapper — AGENTS.md warns against abstractions that hide useful EF Core capabilities;
/// Application queries these DbSets directly via LINQ.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<User> UserProfiles { get; }

    DbSet<TenantMembership> Memberships { get; }

    DbSet<Role> Roles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
