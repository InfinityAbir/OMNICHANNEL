using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Common;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Contacts;

namespace Omnichannel.Application.Contacts;

public sealed class ContactService(IAppDbContext db, AuditService audit, ITenantContext tenantContext, TimeProvider timeProvider)
{
    private const int MaxPageSize = 100;

    public async Task<Contact> CreateAsync(string displayName, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var contact = Contact.Create(tenantContext.TenantId, displayName, now);
        db.Contacts.Add(contact);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "contact.created", nameof(Contact), contact.Id);
        await db.SaveChangesAsync(cancellationToken);
        return contact;
    }

    /// <summary>Finds a contact by an existing channel identifier, or creates both. Used by the
    /// manual-conversation-creation flow (Phase 2's stand-in for real channel ingestion).</summary>
    public async Task<Contact> FindOrCreateByIdentifierAsync(
        ChannelType channelType, string identifierValue, string displayName, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await db.ContactIdentifiers
            .Where(i => i.ChannelType == channelType && i.Value == identifierValue)
            .Select(i => i.ContactId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != Guid.Empty)
        {
            return await db.Contacts.SingleAsync(c => c.Id == existing, cancellationToken);
        }

        var contact = Contact.Create(tenantContext.TenantId, displayName, now);
        var identifier = ContactIdentifier.Create(tenantContext.TenantId, contact.Id, channelType, identifierValue, now);
        db.Contacts.Add(contact);
        db.ContactIdentifiers.Add(identifier);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "contact.created", nameof(Contact), contact.Id);
        await db.SaveChangesAsync(cancellationToken);
        return contact;
    }

    public Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => db.Contacts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<PagedResult<Contact>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Contacts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();

            // This lambda is translated to SQL by EF Core, never executed as CLR code — the
            // culture/StringComparison analyzers (CA1304/CA1311/CA1862) don't know that and
            // would otherwise push toward an overload Npgsql's LINQ provider can't translate.
#pragma warning disable CA1304, CA1311, CA1862
            query = query.Where(c => c.DisplayName.ToLower().Contains(normalizedSearch));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Contact>(items, totalCount, page, pageSize);
    }
}
