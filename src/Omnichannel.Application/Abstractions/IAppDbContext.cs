using Microsoft.EntityFrameworkCore;
using Omnichannel.Domain.Audit;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Contacts;
using Omnichannel.Domain.Conversations;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Domain.Widget;

namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Typed EF Core surface exposed to Application. Deliberately not a generic Repository&lt;T&gt;
/// wrapper — AGENTS.md warns against abstractions that hide useful EF Core capabilities;
/// Application queries these DbSets directly via LINQ.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<User> UserProfiles { get; }

    DbSet<TenantMembership> Memberships { get; }

    DbSet<Role> Roles { get; }

    DbSet<ChannelAccount> ChannelAccounts { get; }

    DbSet<WidgetChannelSettings> WidgetSettings { get; }

    DbSet<Contact> Contacts { get; }

    DbSet<ContactIdentifier> ContactIdentifiers { get; }

    DbSet<Conversation> Conversations { get; }

    DbSet<Message> Messages { get; }

    DbSet<Tag> Tags { get; }

    DbSet<ConversationTag> ConversationTags { get; }

    DbSet<InternalNote> InternalNotes { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<ChannelCredential> ChannelCredentials { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Detaches all tracked entities — used to recover from a benign concurrent-insert race (e.g. two webhook deliveries of the same event) so the next SaveChangesAsync doesn't keep re-sending the failed attempt. Same pattern as RoleSeeder's seed race.</summary>
    void ClearChangeTracker();
}
