using Microsoft.AspNetCore.Http;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Identity;

/// <summary>
/// Resolves tenant/user identity from the authenticated request's JWT claims only — the
/// "tenant_id" claim is set server-side at login/refresh time (see JwtAccessTokenGenerator),
/// never accepted from a client-supplied header/route/body value (ADR-0005).
/// </summary>
public sealed class ScopedTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private System.Security.Claims.ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid TenantId => IsAuthenticated && Guid.TryParse(User!.FindFirst("tenant_id")?.Value, out var id)
        ? id
        : Guid.Empty;

    public Guid UserId => IsAuthenticated && Guid.TryParse(User!.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id
        : Guid.Empty;
}
