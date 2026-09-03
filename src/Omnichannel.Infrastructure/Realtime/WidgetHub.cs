using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Omnichannel.Contracts.Widget;

namespace Omnichannel.Infrastructure.Realtime;

/// <summary>
/// Visitor-facing SignalR hub for the website-chat widget. The widget connects using its
/// short-lived session token (audience "widget") and is placed in the group
/// <c>conversation:{conversationId}</c> taken from that token — the conversation id is never
/// accepted from the client, so a visitor can only ever receive updates for their own
/// conversation. Agents receive the same conversation's messages via the agent-facing
/// <see cref="InboxHub"/> tenant group; the two planes never overlap.
/// </summary>
[Authorize(Policy = "WidgetHub")]
public sealed class WidgetHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var conversationId = GetConversationId();
        if (conversationId == Guid.Empty)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var conversationId = GetConversationId();
        if (conversationId != Guid.Empty)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Guid GetConversationId()
    {
        var value = Context.User?.FindFirst(WidgetClaimNames.ConversationId)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
