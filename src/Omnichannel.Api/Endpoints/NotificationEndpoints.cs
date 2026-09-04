using Omnichannel.Application.Notifications;
using Omnichannel.Contracts.Notifications;
using Omnichannel.Domain.Notifications;

namespace Omnichannel.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapGet("/unread-count", UnreadCountAsync);
        group.MapPost("/{id:guid}/read", MarkReadAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        bool? unreadOnly, int? pageSize, NotificationService notifications, CancellationToken cancellationToken)
    {
        var list = await notifications.ListAsync(unreadOnly ?? false, pageSize ?? 50, cancellationToken);
        return Results.Ok(list.Select(ToResponse).ToList());
    }

    private static async Task<IResult> UnreadCountAsync(NotificationService notifications, CancellationToken cancellationToken)
        => Results.Ok(new UnreadCountResponse(await notifications.UnreadCountAsync(cancellationToken)));

    private static async Task<IResult> MarkReadAsync(Guid id, NotificationService notifications, CancellationToken cancellationToken)
        => await notifications.MarkReadAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static NotificationResponse ToResponse(Notification n)
        => new(n.Id, n.Type, n.Title, n.Body, n.ConversationId, n.Read, n.CreatedAt, n.ReadAt);
}
