namespace Omnichannel.Contracts.Audit;

public sealed record AuditLogResponse(
    Guid Id, Guid? ActorUserId, string Action, string EntityType, string EntityId, DateTimeOffset Timestamp);
