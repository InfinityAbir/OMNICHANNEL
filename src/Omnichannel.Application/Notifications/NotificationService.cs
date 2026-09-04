using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Notifications;

namespace Omnichannel.Application.Notifications;

/// <summary>Every method here is implicitly scoped to the caller's own <see cref="ITenantContext.UserId"/> — a personal notification feed, not a tenant-wide management surface, so no extra permission check beyond authentication is needed (same reasoning as `GET /api/v1/users/me`).</summary>
public sealed class NotificationService(IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    private const int MaxPageSize = 100;

    public async Task<List<Notification>> ListAsync(bool unreadOnly, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = db.Notifications.Where(n => n.UserId == tenantContext.UserId);
        if (unreadOnly)
        {
            query = query.Where(n => !n.Read);
        }

        return await query.OrderByDescending(n => n.CreatedAt).Take(pageSize).ToListAsync(cancellationToken);
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken)
        => db.Notifications.CountAsync(n => n.UserId == tenantContext.UserId && !n.Read, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications
            .SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == tenantContext.UserId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.MarkRead(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
