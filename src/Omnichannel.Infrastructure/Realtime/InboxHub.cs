using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Omnichannel.Infrastructure.Realtime;

[Authorize(Policy = "RealtimeHub")]
public sealed class InboxHub : Hub
{
    private const string TenantIdClaim = "tenant_id";
    private const string UserIdClaim = "sub";

    public override async Task OnConnectedAsync()
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();

        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenantId = GetTenantId();
        if (tenantId != Guid.Empty)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();

        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new HubException("Unauthorized");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
    }

    private Guid GetTenantId()
    {
        var claim = Context.User?.FindFirst(TenantIdClaim)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private Guid GetUserId()
    {
        var claim = Context.User?.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
