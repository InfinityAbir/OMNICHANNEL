using Microsoft.AspNetCore.Mvc;
using Omnichannel.Application.Audit;
using Omnichannel.Contracts.Audit;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", ListAsync).RequireAuthorization(PermissionKeys.AuditRead);
        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] int? page, [FromQuery] int? pageSize, AuditService audit, CancellationToken cancellationToken)
    {
        var result = await audit.ListAsync(page ?? 1, pageSize ?? 50, cancellationToken);
        var items = result.Items.Select(a => new AuditLogResponse(a.Id, a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.Timestamp)).ToList();
        return Results.Ok(new PagedResponse<AuditLogResponse>(items, result.TotalCount, result.Page, result.PageSize));
    }
}
