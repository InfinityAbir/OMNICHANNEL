using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Application.Ai;

public sealed record BusinessHoursWindowValue(TimeOnly Start, TimeOnly End);

/// <summary>
/// Tenant-facing configuration for AI auto-reply (PRD §71) — a thin CRUD layer over
/// <see cref="AiAutoReplySettings"/>, kept separate from <see cref="AiAutoReplyService"/> (the
/// decision pipeline) so the two responsibilities — "what's configured" vs. "what to do about an
/// inbound message right now" — don't get tangled.
/// </summary>
public sealed class AiAutoReplySettingsService(IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider, AuditService audit)
{
    public async Task<AiAutoReplySettings> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.AiAutoReplySettings.SingleOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        // Every tenant gets this row at registration (AuthService) — this is only a safety net
        // for tenants that predate that, not the normal path.
        settings = AiAutoReplySettings.CreateDefault(tenantContext.TenantId, timeProvider.GetUtcNow());
        db.AiAutoReplySettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<AiAutoReplySettings> UpdateAsync(
        bool enabled,
        double confidenceThreshold,
        int dailyLimit,
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowValue>>? businessHours,
        CancellationToken cancellationToken)
    {
        var settings = await GetAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        var domainSchedule = businessHours?.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<BusinessHoursWindow>)kv.Value.Select(w => new BusinessHoursWindow(w.Start, w.End)).ToList());

        settings.Configure(enabled, confidenceThreshold, dailyLimit, domainSchedule, now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "ai.autoreply.settings_updated", nameof(AiAutoReplySettings), settings.TenantId,
            new { enabled, confidenceThreshold, dailyLimit });

        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
