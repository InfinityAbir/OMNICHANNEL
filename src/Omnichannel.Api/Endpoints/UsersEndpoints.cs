using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Tenancy;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Tenancy;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users/me", GetCurrentUserAsync).RequireAuthorization();

        // Data retention / account deletion (ADR-0030), the individual-user half — no special
        // permission required beyond being authenticated, since this only ever acts on the
        // caller's own account. The whole-business-account half lives in
        // TenantDeletionEndpoints, which IS permission-gated (Owner-only).
        app.MapDelete("/api/v1/users/me", DeleteCurrentUserAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetCurrentUserAsync(ITenantContext tenantContext, IAppDbContext db, CancellationToken cancellationToken)
    {
        // Belt-and-braces: RequireAuthorization already enforces this, but a tenant-scoped
        // read must never proceed against an unresolved tenant (ADR-0005).
        if (!tenantContext.IsAuthenticated || tenantContext.TenantId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        // Single projected/joined query instead of 4 sequential round-trips.
        var result = await (
            from user in db.UserProfiles
            where user.Id == tenantContext.UserId
            join membership in db.Memberships.Where(m => m.Status == MembershipStatus.Active)
                on new { user.Id, TenantId = tenantContext.TenantId } equals new { Id = membership.UserId, membership.TenantId }
            join tenant in db.Tenants on membership.TenantId equals tenant.Id
            join role in db.Roles on membership.RoleId equals role.Id
            select new CurrentUserResponse(user.Id, user.Email, user.DisplayName, tenant.Id, tenant.Name, role.Name, role.Permissions)
        ).SingleOrDefaultAsync(cancellationToken);

        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }

    private static async Task<IResult> DeleteCurrentUserAsync(AccountDeletionService service, CancellationToken cancellationToken)
    {
        var outcome = await service.DeleteMyAccountAsync(cancellationToken);
        return outcome.Succeeded
            ? Results.Ok(new DeleteMyAccountResponse(true, null))
            : Results.Problem(title: "Account cannot be deleted yet.", detail: outcome.Error, statusCode: StatusCodes.Status409Conflict);
    }
}
