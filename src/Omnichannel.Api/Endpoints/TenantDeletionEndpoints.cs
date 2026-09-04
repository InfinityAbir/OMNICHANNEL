using Omnichannel.Application.Tenancy;
using Omnichannel.Contracts.Tenancy;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

/// <summary>Data retention / account deletion (ADR-0030) — the whole-business-account half. The
/// individual-user half (<c>DELETE /api/v1/users/me</c>) lives in <see cref="UsersEndpoints"/>.</summary>
public static class TenantDeletionEndpoints
{
    public static IEndpointRouteBuilder MapTenantDeletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/tenant/deletion", GetStatusAsync).RequireAuthorization(PermissionKeys.TenantRead);
        app.MapPost("/api/v1/tenant/deletion", RequestAsync).RequireAuthorization(PermissionKeys.TenantDelete);
        app.MapDelete("/api/v1/tenant/deletion", CancelAsync).RequireAuthorization(PermissionKeys.TenantDelete);

        return app;
    }

    private static async Task<IResult> GetStatusAsync(AccountDeletionService service, CancellationToken cancellationToken)
    {
        var status = await service.GetTenantDeletionStatusAsync(cancellationToken);
        return Results.Ok(new TenantDeletionStatusResponse(status.Status, status.ScheduledDeletionAt));
    }

    private static async Task<IResult> RequestAsync(AccountDeletionService service, CancellationToken cancellationToken)
    {
        var status = await service.RequestTenantDeletionAsync(cancellationToken);
        return Results.Ok(new TenantDeletionStatusResponse(status.Status, status.ScheduledDeletionAt));
    }

    private static async Task<IResult> CancelAsync(AccountDeletionService service, CancellationToken cancellationToken)
    {
        try
        {
            var status = await service.CancelTenantDeletionAsync(cancellationToken);
            return Results.Ok(new TenantDeletionStatusResponse(status.Status, status.ScheduledDeletionAt));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: "No deletion is currently scheduled.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
