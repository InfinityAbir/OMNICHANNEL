using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Omnichannel.Contracts.Widget;

namespace Omnichannel.Infrastructure.Realtime;

/// <summary>
/// Requirement honored for the visitor-facing widget hub. The handler succeeds only when the
/// principal is authenticated (widget scheme) and carries both a tenant_id and a conversation_id,
/// both of which come from the server-issued widget session token — never from client input.
/// </summary>
public sealed class WidgetHubAuthorizationRequirement : IAuthorizationRequirement
{
}

public sealed class WidgetHubAuthorizationHandler : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements)
        {
            if (requirement is not WidgetHubAuthorizationRequirement)
            {
                continue;
            }

            var httpContext = context.Resource as HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated == true
                && Guid.TryParse(httpContext.User.FindFirst(WidgetClaimNames.TenantId)?.Value, out _)
                && Guid.TryParse(httpContext.User.FindFirst(WidgetClaimNames.ConversationId)?.Value, out _))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
