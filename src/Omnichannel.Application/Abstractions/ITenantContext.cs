namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Resolves the current tenant from authenticated identity only — never from a client-supplied
/// route/header/body value (ADR-0005). Implemented in the Api layer, where it reads the
/// "tenant_id" claim set by the login/refresh flow.
/// </summary>
public interface ITenantContext
{
    bool IsAuthenticated { get; }

    Guid TenantId { get; }

    Guid UserId { get; }
}
