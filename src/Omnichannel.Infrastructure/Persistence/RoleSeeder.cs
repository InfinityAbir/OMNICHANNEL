using Microsoft.EntityFrameworkCore;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Infrastructure.Persistence;

/// <summary>
/// Idempotently seeds the 4 fixed system roles (PRD §12) with their permission sets. Owner and
/// Admin currently share an identical permission set — the permission catalog has no
/// owner-exclusive action yet (e.g. billing, ownership transfer); differentiate them once one
/// exists rather than inventing a distinction now.
/// </summary>
public static class RoleSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Roles.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Roles.AddRange(BuildSeedRoles());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another process seeded the same fixed rows between our check and this insert
            // (e.g. two WebApplicationFactory hosts starting concurrently in tests) — the
            // unique index on SystemRole guarantees no duplicate, so this is a benign race,
            // not a real failure. Clear the change tracker so the failed insert attempts are
            // detached; otherwise EF will keep re-sending them on the next SaveChangesAsync.
            db.ChangeTracker.Clear();
        }
    }

    private static Role[] BuildSeedRoles() =>
    [
        Role.Create(SystemRole.Owner, "Owner", PermissionKeys.All),
        Role.Create(SystemRole.Admin, "Admin", PermissionKeys.All),
        Role.Create(SystemRole.Agent, "Agent",
        [
            PermissionKeys.TenantRead,
            PermissionKeys.ConversationsRead,
            PermissionKeys.ConversationsReply,
            PermissionKeys.ConversationsAssign,
            PermissionKeys.ConversationsClose,
            PermissionKeys.ChannelsRead,
            PermissionKeys.AiRead,
            PermissionKeys.KnowledgeRead,
        ]),
        Role.Create(SystemRole.Viewer, "Viewer",
        [
            PermissionKeys.TenantRead,
            PermissionKeys.UsersRead,
            PermissionKeys.ConversationsRead,
            PermissionKeys.ChannelsRead,
            PermissionKeys.AiRead,
            PermissionKeys.KnowledgeRead,
            PermissionKeys.AnalyticsRead,
            PermissionKeys.AuditRead,
        ]),
    ];
}
