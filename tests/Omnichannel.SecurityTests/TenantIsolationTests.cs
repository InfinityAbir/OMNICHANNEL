using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Infrastructure.Persistence;

namespace Omnichannel.SecurityTests;

/// <summary>
/// Proves the EF Core global query filter (ADR-0005) actually isolates tenant-owned rows at
/// the data-access layer — not just "the API happens not to expose it today". Two tenants'
/// memberships live in the same physical table; a context scoped to Tenant A must never see
/// Tenant B's rows, even via a broad LINQ query with no explicit TenantId predicate.
/// </summary>
public class TenantIsolationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("OMNICHANNEL_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=omnichannel;Username=omnichannel;Password=omnichannel_dev_only";

    [Fact]
    public async Task Memberships_ForTenantA_NeverIncludeTenantBsRows()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options;

        Guid tenantAId, tenantBId;

        // Seed both tenants' data through an unfiltered (unauthenticated) context — inserts are
        // never filtered, only reads are, so this reflects real seeding/migration code paths.
        await using (var seedDb = new AppDbContext(options, new FixedTenantContext(false, Guid.Empty)))
        {
            var role = await seedDb.Roles.FirstAsync();
            var userA = User.Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.test", "User A", DateTimeOffset.UtcNow);
            var userB = User.Create(Guid.NewGuid(), $"{Guid.NewGuid():N}@example.test", "User B", DateTimeOffset.UtcNow);
            var tenantA = Tenant.Create("Tenant A", $"tenant-a-{Guid.NewGuid():N}", "UTC", DateTimeOffset.UtcNow);
            var tenantB = Tenant.Create("Tenant B", $"tenant-b-{Guid.NewGuid():N}", "UTC", DateTimeOffset.UtcNow);
            tenantAId = tenantA.Id;
            tenantBId = tenantB.Id;

            seedDb.UserProfiles.AddRange(userA, userB);
            seedDb.Tenants.AddRange(tenantA, tenantB);
            seedDb.Memberships.AddRange(
                TenantMembership.Create(tenantA.Id, userA.Id, role.Id, DateTimeOffset.UtcNow),
                TenantMembership.Create(tenantB.Id, userB.Id, role.Id, DateTimeOffset.UtcNow));
            await seedDb.SaveChangesAsync(CancellationToken.None);
        }

        await using var tenantAScopedDb = new AppDbContext(options, new FixedTenantContext(true, tenantAId));

        var visibleTenantIds = await tenantAScopedDb.Memberships
            .Select(m => m.TenantId)
            .Distinct()
            .ToListAsync();

        Assert.DoesNotContain(tenantBId, visibleTenantIds);
        Assert.True(visibleTenantIds.Count <= 1);
    }

    private sealed class FixedTenantContext(bool isAuthenticated, Guid tenantId) : ITenantContext
    {
        public bool IsAuthenticated => isAuthenticated;

        public Guid TenantId => tenantId;

        public Guid UserId => Guid.Empty;
    }
}
