using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Ai;

/// <summary>
/// Counts today's (UTC) AiSuggestion rows for the tenant against a configured daily cap — reuses
/// the interaction log itself as the usage record rather than a separate counter table, since the
/// log already has everything needed and a soft cost-control cap doesn't need the concurrency
/// guarantees a hard limit would (a small race under heavy concurrent use is an acceptable
/// trade-off for not adding locking to a cost-control feature).
/// </summary>
public sealed class AiUsageLimiter(IAppDbContext db, TimeProvider timeProvider, IOptions<AiOptions> options) : IAiUsageLimiter
{
    public async Task<bool> TryConsumeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var limit = options.Value.DailySuggestionLimitPerTenant;
        if (limit <= 0)
        {
            return true; // 0/negative means "unlimited" — an explicit deployer choice, not a silent default.
        }

        // Must stay a DateTimeOffset with Offset=0 — DateTimeOffset.Date returns a bare DateTime,
        // which EF/Npgsql would then reinterpret using the machine's *local* timezone offset when
        // comparing against the "timestamp with time zone" column, and Npgsql only accepts UTC
        // (Offset=0) values for that type. A DateTime here silently breaks on any machine whose
        // local offset isn't zero (caught by this exact failure on a UTC+6 dev machine).
        var now = timeProvider.GetUtcNow();
        var startOfDayUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var count = await db.AiSuggestions
            .Where(s => s.TenantId == tenantId && s.CreatedAt >= startOfDayUtc)
            .CountAsync(cancellationToken);

        return count < limit;
    }
}
