using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Audit;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Common;
using Omnichannel.Domain.Contacts;
using Omnichannel.Domain.Conversations;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Domain.Widget;
using Omnichannel.Infrastructure.Identity;

namespace Omnichannel.Infrastructure.Persistence;

/// <summary>
/// IdentityUserContext (not IdentityDbContext) — Identity's own Role/Claim tables are unused;
/// authorization runs on our own Role/Permission/TenantMembership model instead (ADR-0007).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
    : IdentityUserContext<ApplicationUser, Guid>(options), IAppDbContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod = typeof(AppDbContext)
        .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> UserProfiles => Set<User>();

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ChannelAccount> ChannelAccounts => Set<ChannelAccount>();

    public DbSet<WidgetChannelSettings> WidgetSettings => Set<WidgetChannelSettings>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactIdentifier> ContactIdentifiers => Set<ContactIdentifier>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<ConversationTag> ConversationTags => Set<ConversationTag>();

    public DbSet<InternalNote> InternalNotes => Set<InternalNote>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Referenced (not captured as a closure constant) from the query filter lambdas built
    /// below — EF Core re-evaluates member access on the DbContext instance against whichever
    /// context is actually executing the query, not the one present when the model was first
    /// built and cached. This is the standard EF Core multi-tenancy pattern (ADR-0005): every
    /// ITenantOwned entity gets this filter automatically, so a missing explicit TenantId
    /// predicate in application code cannot leak cross-tenant rows by omission.
    /// </summary>
    private Guid CurrentTenantId => tenantContext.IsAuthenticated ? tenantContext.TenantId : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, ITenantOwned
        => builder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
