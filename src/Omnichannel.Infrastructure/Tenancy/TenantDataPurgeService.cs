using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Omnichannel.Domain.Audit;
using Omnichannel.Domain.Common;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Infrastructure.Persistence;

namespace Omnichannel.Infrastructure.Tenancy;

/// <summary>
/// Permanently purges a tenant's operational data once its grace period has elapsed
/// (<c>Tenant.ScheduledDeletionAt</c>, ADR-0030). Deletes every row across every
/// <see cref="ITenantOwned"/> entity type generically (reflection over the EF model, the same
/// technique <c>AppDbContext.OnModelCreating</c> already uses to apply the tenant query filter to
/// every such type) — new tenant-owned entities are covered automatically, nothing to remember to
/// add here when a future phase introduces one. <see cref="AuditLog"/> is the one deliberate
/// exception: kept so the tenant's own deletion (and everything that happened before it) stays in
/// the audit trail, rather than the purge erasing the record of itself. The <c>Tenant</c> row
/// itself is kept too (marked <see cref="TenantStatus.Deleted"/>, not removed) so those audit
/// entries — and anything else referencing the tenant id — still resolve to something.
/// </summary>
public sealed partial class TenantDataPurgeService(
    IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<TenantDataPurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private static readonly MethodInfo PurgeEntityTypeMethod =
        typeof(TenantDataPurgeService).GetMethod(nameof(PurgeEntityTypeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            await PurgeDueTenantsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Public and idempotent so it's directly testable (same seam as
    /// <c>JwtSigningKeyRefreshService.RefreshAsync</c>) instead of waiting on — or racing — the
    /// hourly timer.</summary>
    public async Task PurgeDueTenantsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = timeProvider.GetUtcNow();

            var dueTenantIds = await db.Tenants
                .Where(t => t.Status == TenantStatus.PendingDeletion && t.ScheduledDeletionAt != null && t.ScheduledDeletionAt <= now)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach (var tenantId in dueTenantIds)
            {
                await PurgeOneTenantAsync(db, tenantId, now, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A purge run failing must never crash the host — the next scheduled tick tries
            // again, and nothing time-sensitive depends on this running to the second.
            LogPurgeRunFailed(logger, ex);
        }
    }

    private async Task PurgeOneTenantAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenantOwnedTypes = db.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t) && t != typeof(AuditLog));

        foreach (var entityType in tenantOwnedTypes)
        {
            await (Task<int>)PurgeEntityTypeMethod.MakeGenericMethod(entityType).Invoke(null, [db, tenantId, cancellationToken])!;
        }

        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken);
        tenant.MarkDeleted(now);
        await db.SaveChangesAsync(cancellationToken);

        LogTenantPurged(logger, tenantId);
    }

    private static Task<int> PurgeEntityTypeAsync<TEntity>(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
        where TEntity : class, ITenantOwned
        => db.Set<TEntity>().IgnoreQueryFilters().Where(e => e.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tenant data purge run failed.")]
    private static partial void LogPurgeRunFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Permanently purged operational data for tenant {TenantId} (deletion grace period elapsed).")]
    private static partial void LogTenantPurged(ILogger logger, Guid tenantId);
}
