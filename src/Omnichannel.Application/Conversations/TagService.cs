using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Conversations;

public sealed class TagService(IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public Task<List<Tag>> ListAsync(CancellationToken cancellationToken)
        => db.Tags.OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<Tag> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        var existing = await db.Tags.SingleOrDefaultAsync(t => t.Name == normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var tag = Tag.Create(tenantContext.TenantId, normalized, timeProvider.GetUtcNow());
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);
        return tag;
    }
}
