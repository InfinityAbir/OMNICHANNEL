namespace Omnichannel.Contracts.Tenancy;

public sealed record TenantDeletionStatusResponse(string Status, DateTimeOffset? ScheduledDeletionAt);

public sealed record DeleteMyAccountResponse(bool Succeeded, string? Error);
