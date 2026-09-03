using Microsoft.EntityFrameworkCore;

namespace Omnichannel.Infrastructure.Persistence;

/// <summary>
/// Root EF Core context. No entities yet — Phase 1 introduces Tenant/User/Membership
/// and the tenant-scoped global query filter convention (see ADR-0005).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
