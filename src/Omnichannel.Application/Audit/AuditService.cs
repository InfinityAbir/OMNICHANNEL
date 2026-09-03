using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Common;
using Omnichannel.Domain.Audit;

namespace Omnichannel.Application.Audit;

/// <summary>
/// Thin write helper over AuditLog — not behind an interface since there's nothing to swap
/// (it's a direct, tenant-scoped append to our own table, not an external dependency).
/// Metadata must never contain secrets or full message content (AGENTS.md).
/// </summary>
public sealed class AuditService(IAppDbContext db, TimeProvider timeProvider)
{
    private const int MaxPageSize = 200;

    public void Record(Guid tenantId, Guid? actorUserId, string action, string entityType, Guid entityId, object? metadata = null)
    {
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);
        db.AuditLogs.Add(AuditLog.Create(
            tenantId, actorUserId, action, entityType, entityId.ToString(), timeProvider.GetUtcNow(), metadata: metadataJson));
    }

    public async Task<PagedResult<AuditLog>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.AuditLogs.OrderByDescending(a => a.Timestamp);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<AuditLog>(items, totalCount, page, pageSize);
    }
}
