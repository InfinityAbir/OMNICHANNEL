namespace Omnichannel.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one tenant. AppDbContext applies a global
/// EF Core query filter to every implementer (see ADR-0005) so a missing explicit
/// tenant predicate in application code cannot leak cross-tenant rows by omission.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
