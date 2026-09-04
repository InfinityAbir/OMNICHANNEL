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
    // Arbitrary fixed key for the session-level advisory lock below — only needs to be unique
    // within this database, and nothing else in the codebase takes an advisory lock.
    private const long SeedLockKey = 872364501;

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // A plain "check, then insert" race across concurrent processes (e.g. multiple
        // WebApplicationFactory hosts starting at once in the test suite, all hitting the same
        // shared database) produced both a duplicate-key violation and a Postgres deadlock in
        // real CI runs — the unique index alone wasn't enough to make this safe. A session-level
        // advisory lock serializes the whole check-then-insert across connections; role seeding
        // is a rare startup-time operation, so losing concurrency here costs nothing.
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.CommandText = $"SELECT pg_advisory_lock({SeedLockKey})";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            if (await db.Roles.AnyAsync(cancellationToken))
            {
                return;
            }

            db.Roles.AddRange(BuildSeedRoles());
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            await using var unlockCommand = connection.CreateCommand();
            unlockCommand.CommandText = $"SELECT pg_advisory_unlock({SeedLockKey})";
            await unlockCommand.ExecuteNonQueryAsync(cancellationToken);

            if (openedHere)
            {
                await connection.CloseAsync();
            }
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
