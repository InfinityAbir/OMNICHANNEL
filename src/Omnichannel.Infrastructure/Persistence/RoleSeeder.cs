using Microsoft.EntityFrameworkCore;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Infrastructure.Persistence;

/// <summary>
/// Idempotently seeds the 4 fixed system roles (PRD §12) with their permission sets. Owner and
/// Admin share every permission except <see cref="PermissionKeys.TenantDelete"/> (ADR-0030) —
/// the first genuinely owner-exclusive action in the catalog (scheduling/cancelling deletion of
/// the whole business account), deliberately withheld from Admin.
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
            var seedRoles = BuildSeedRoles();
            var existingRoles = await db.Roles.ToDictionaryAsync(r => r.SystemRole, cancellationToken);

            if (existingRoles.Count == 0)
            {
                db.Roles.AddRange(seedRoles);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            // Roles already exist (not a fresh database) — reconcile each one's permission set to
            // the current catalog rather than leaving it frozen at whatever it was first seeded
            // with. Without this, a permission added to a role in code (e.g. ADR-0030's
            // tenant.delete added to Owner) would never reach an already-seeded database, since
            // the "insert only if empty" check above would just skip it forever.
            var anyChanged = false;
            foreach (var seedRole in seedRoles)
            {
                if (existingRoles.TryGetValue(seedRole.SystemRole, out var existing) && existing.ReconcilePermissions(seedRole.Permissions))
                {
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
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
        Role.Create(SystemRole.Admin, "Admin", [.. PermissionKeys.All.Where(p => p != PermissionKeys.TenantDelete)]),
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
