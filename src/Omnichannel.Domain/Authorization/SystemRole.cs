namespace Omnichannel.Domain.Authorization;

/// <summary>The 4 fixed roles from PRD §12. Not tenant-owned — shared, seeded catalog.</summary>
public enum SystemRole
{
    Owner = 0,
    Admin = 1,
    Agent = 2,
    Viewer = 3,
}

public sealed class Role
{
    public Guid Id { get; private set; }
    public SystemRole SystemRole { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// A small, seeded lookup catalog (4 fixed system roles) — kept as a plain settable list
    /// rather than a wrapped read-only view, since the extra indirection would only complicate
    /// the EF Core mapping without protecting a real invariant here.
    /// </summary>
    public List<string> Permissions { get; private set; } = [];

    private Role()
    {
    }

    public static Role Create(SystemRole systemRole, string name, IEnumerable<string> permissions)
        => new()
        {
            Id = Guid.NewGuid(),
            SystemRole = systemRole,
            Name = name,
            Permissions = permissions.Distinct(StringComparer.Ordinal).ToList(),
        };

    /// <summary>Reconciles this already-seeded role's permission set to the current catalog
    /// definition — <c>RoleSeeder</c> calls this on every startup, not just when a role is first
    /// created, so a permission added to (or removed from) a role in code (e.g. ADR-0030's
    /// tenant.delete) actually reaches an already-seeded database on redeploy, not just a fresh
    /// one.</summary>
    public bool ReconcilePermissions(IEnumerable<string> currentPermissions)
    {
        var updated = currentPermissions.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var existing = Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (updated.SequenceEqual(existing, StringComparer.Ordinal))
        {
            return false;
        }

        Permissions = updated;
        return true;
    }
}
