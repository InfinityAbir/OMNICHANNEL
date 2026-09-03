using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Audit;

/// <summary>
/// PRD §35. Append-only by convention (no update/delete methods) — metadata must never contain
/// secrets or full sensitive message content (AGENTS.md).
/// </summary>
public sealed class AuditLog : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public DateTimeOffset Timestamp { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? Metadata { get; private set; }

    private AuditLog()
    {
    }

    public static AuditLog Create(
        Guid tenantId, Guid? actorUserId, string action, string entityType, string entityId,
        DateTimeOffset now, string? ipAddress = null, string? userAgent = null, string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action is required.", nameof(action));
        }

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Timestamp = now,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = metadata,
        };
    }
}
