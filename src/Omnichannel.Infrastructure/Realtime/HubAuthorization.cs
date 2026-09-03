using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Omnichannel.Infrastructure.Realtime;

public sealed class InboxHubAuthorizationHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            if (requirement is HubAuthorizationRequirement)
            {
                var httpContext = context.Resource as HttpContext;
                if (httpContext?.User?.Identity?.IsAuthenticated == true)
                {
                    var tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
                    if (!string.IsNullOrEmpty(tenantId) && Guid.TryParse(tenantId, out _))
                    {
                        context.Succeed(requirement);
                    }
                }
            }
        }
        return Task.CompletedTask;
    }
}

public sealed class HubAuthorizationRequirement : IAuthorizationRequirement
{
}
