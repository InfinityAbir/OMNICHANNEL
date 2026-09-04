using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Automation;

namespace Omnichannel.Application.Automation;

public sealed class TenantBusinessHoursService(IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public async Task<TenantBusinessHours> GetAsync(CancellationToken cancellationToken)
    {
        var hours = await db.TenantBusinessHours.SingleOrDefaultAsync(h => h.TenantId == tenantContext.TenantId, cancellationToken);
        if (hours is not null)
        {
            return hours;
        }

        // Every tenant gets this row at registration — this is only a safety net for tenants
        // that predate that, not the normal path (same pattern as AiAutoReplySettingsService).
        hours = TenantBusinessHours.CreateDefault(tenantContext.TenantId, timeProvider.GetUtcNow());
        db.TenantBusinessHours.Add(hours);
        await db.SaveChangesAsync(cancellationToken);
        return hours;
    }

    public async Task<TenantBusinessHours> UpdateAsync(
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>? businessHours,
        IReadOnlyCollection<DateOnly> holidays,
        CancellationToken cancellationToken)
    {
        var hours = await GetAsync(cancellationToken);
        hours.Configure(businessHours, holidays, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return hours;
    }
}
