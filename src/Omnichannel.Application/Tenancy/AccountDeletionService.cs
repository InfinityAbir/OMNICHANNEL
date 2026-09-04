using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.Application.Tenancy;

public sealed record TenantDeletionStatus(string Status, DateTimeOffset? ScheduledDeletionAt);

public sealed record AccountDeletionOutcome(bool Succeeded, string? Error);

/// <summary>
/// Data retention / account deletion (ADR-0030): a tenant Owner can schedule (and cancel) the
/// whole business account's deletion, and any user can delete their own account. Both are
/// grace-period flows, never instant — see the class's own methods for why.
/// </summary>
public sealed class AccountDeletionService(
    IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider,
    IEmailSender emailSender, IIdentityService identity, IRefreshTokenStore refreshTokens, AuditService audit)
{
    // A fixed business rule, not a per-tenant config value — how long a business gets to change
    // its mind is a product decision, not something that benefits from per-tenant tuning (unlike
    // e.g. AI confidence thresholds). Comfortably longer than a "delete" click, short of a
    // customer-visible "we're indefinitely keeping data we said we'd remove."
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(14);

    public async Task<TenantDeletionStatus> GetTenantDeletionStatusAsync(CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        return new TenantDeletionStatus(tenant.Status.ToString(), tenant.ScheduledDeletionAt);
    }

    /// <summary>Schedules the current tenant for deletion <see cref="GracePeriod"/> from now —
    /// never immediate, so an accidental click (or a moment of frustration) doesn't destroy a
    /// business's data with no way back. Blocks new logins/token refreshes for this tenant
    /// starting immediately (<c>AuthService.GetActiveTenantContextAsync</c>), but doesn't touch
    /// already-issued sessions or any data until <c>TenantDataPurgeService</c> actually runs.</summary>
    public async Task<TenantDeletionStatus> RequestTenantDeletionAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantContext.TenantId, cancellationToken);
        var scheduledAt = now + GracePeriod;

        tenant.ScheduleDeletion(scheduledAt, now);
        audit.Record(tenant.Id, tenantContext.UserId, "tenant.deletion_scheduled", nameof(Tenant), tenant.Id, new { scheduledAt });
        await db.SaveChangesAsync(cancellationToken);

        var requester = await db.UserProfiles.SingleAsync(u => u.Id == tenantContext.UserId, cancellationToken);
        await emailSender.SendTenantDeletionScheduledAsync(tenant.Id, requester.Email, requester.DisplayName, tenant.Name, scheduledAt, cancellationToken);

        return new TenantDeletionStatus(tenant.Status.ToString(), tenant.ScheduledDeletionAt);
    }

    public async Task<TenantDeletionStatus> CancelTenantDeletionAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantContext.TenantId, cancellationToken);

        tenant.CancelScheduledDeletion(now);
        audit.Record(tenant.Id, tenantContext.UserId, "tenant.deletion_cancelled", nameof(Tenant), tenant.Id);
        await db.SaveChangesAsync(cancellationToken);

        return new TenantDeletionStatus(tenant.Status.ToString(), tenant.ScheduledDeletionAt);
    }

    /// <summary>
    /// Deletes the calling user's own account across every tenant they belong to. Blocked (no
    /// partial effect — validated fully before anything is mutated) if they're the sole Owner of
    /// a tenant that still has other active members: there'd be no one left who could ever manage
    /// that business, so they must transfer ownership or delete the business account first. If
    /// they're the sole Owner of a tenant with no other members at all, deleting their account
    /// also schedules that now-ownerless tenant for deletion (same grace period, same reasoning).
    /// </summary>
    public async Task<AccountDeletionOutcome> DeleteMyAccountAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var userId = tenantContext.UserId;

        // IgnoreQueryFilters: a user can belong to several tenants, but the ambient tenant filter
        // only ever scopes to the one tenant the current session is authenticated against — the
        // same documented exception this codebase uses wherever a user's cross-tenant footprint
        // must be seen in full (AuthService.GetActiveTenantContextAsync being the nearest example).
        var memberships = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken);

        var tenantsToSchedule = new List<Tenant>();
        foreach (var membership in memberships)
        {
            var role = await db.Roles.SingleAsync(r => r.Id == membership.RoleId, cancellationToken);
            if (role.SystemRole != SystemRole.Owner)
            {
                continue;
            }

            var otherActiveOwners = await db.Memberships.IgnoreQueryFilters().CountAsync(
                m => m.TenantId == membership.TenantId && m.UserId != userId && m.Status == MembershipStatus.Active, cancellationToken);
            var tenant = await db.Tenants.SingleAsync(t => t.Id == membership.TenantId, cancellationToken);

            if (otherActiveOwners > 0)
            {
                return new AccountDeletionOutcome(false,
                    $"You're the sole owner of \"{tenant.Name}\", which has other members. Transfer ownership or delete the business account first.");
            }

            tenantsToSchedule.Add(tenant);
        }

        // Nothing mutated above — validation only. Apply now that every membership has been checked.
        var scheduledAt = now + GracePeriod;
        foreach (var tenant in tenantsToSchedule)
        {
            tenant.ScheduleDeletion(scheduledAt, now);
            audit.Record(tenant.Id, userId, "tenant.deletion_scheduled", nameof(Tenant), tenant.Id,
                new { reason = "sole owner deleted their own account", scheduledAt });
        }

        foreach (var membership in memberships)
        {
            membership.Remove(now);
            audit.Record(membership.TenantId, userId, "user.self_deleted", "User", userId);
        }

        var user = await db.UserProfiles.SingleAsync(u => u.Id == userId, cancellationToken);
        user.Anonymize(now);
        await db.SaveChangesAsync(cancellationToken);

        await refreshTokens.RevokeAllForUserAsync(userId, now, cancellationToken);
        await identity.DeleteUserAsync(userId, cancellationToken);

        return new AccountDeletionOutcome(true, null);
    }
}
