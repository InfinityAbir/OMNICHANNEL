using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Automation;

namespace Omnichannel.Application.Automation;

public sealed class SavedReplyService(IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public Task<List<SavedReply>> ListAsync(CancellationToken cancellationToken)
        => db.SavedReplies.OrderBy(r => r.Title).ToListAsync(cancellationToken);

    public async Task<SavedReply> CreateAsync(string title, string text, CancellationToken cancellationToken)
    {
        var reply = SavedReply.Create(tenantContext.TenantId, title, text, timeProvider.GetUtcNow());
        db.SavedReplies.Add(reply);
        await db.SaveChangesAsync(cancellationToken);
        return reply;
    }

    public async Task<SavedReply?> UpdateAsync(Guid id, string title, string text, CancellationToken cancellationToken)
    {
        var reply = await db.SavedReplies.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reply is null)
        {
            return null;
        }

        reply.Update(title, text, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return reply;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var reply = await db.SavedReplies.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reply is null)
        {
            return false;
        }

        db.SavedReplies.Remove(reply);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
