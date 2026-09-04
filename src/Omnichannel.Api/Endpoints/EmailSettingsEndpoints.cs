using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Notifications;
using Omnichannel.Contracts.Notifications;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class EmailSettingsEndpoints
{
    public static IEndpointRouteBuilder MapEmailSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/tenant/email-settings", GetAsync).RequireAuthorization(PermissionKeys.TenantRead);
        app.MapPut("/api/v1/tenant/email-settings", UpdateAsync).RequireAuthorization(PermissionKeys.TenantUpdate);
        app.MapDelete("/api/v1/tenant/email-settings", ClearAsync).RequireAuthorization(PermissionKeys.TenantUpdate);
        app.MapPost("/api/v1/tenant/email-settings/test", TestAsync).RequireAuthorization(PermissionKeys.TenantUpdate);

        return app;
    }

    private static async Task<IResult> GetAsync(EmailSettingsService service, CancellationToken cancellationToken)
    {
        var (settings, hasPassword) = await service.GetAsync(cancellationToken);
        return Results.Ok(new EmailSettingsResponse(settings.Host, settings.Port, settings.Username, settings.FromAddress, settings.FromName, settings.IsConfigured, hasPassword));
    }

    private static async Task<IResult> UpdateAsync(UpdateEmailSettingsRequest request, EmailSettingsService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.FromAddress))
        {
            return Results.Problem(title: "Host, username, and from-address are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Port is <= 0 or > 65535)
        {
            return Results.Problem(title: "Port must be between 1 and 65535.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var (settings, hasPassword) = await service.UpdateAsync(request.Host, request.Port, request.Username, request.FromAddress, request.FromName, request.Password, cancellationToken);
            return Results.Ok(new EmailSettingsResponse(settings.Host, settings.Port, settings.Username, settings.FromAddress, settings.FromName, settings.IsConfigured, hasPassword));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "Invalid email settings.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ClearAsync(EmailSettingsService service, CancellationToken cancellationToken)
    {
        await service.ClearAsync(cancellationToken);
        return Results.NoContent();
    }

    // Sends the test email to the calling user's own address — a self-directed connection check,
    // not a message to a third party, matching how a "test email" button works in any mail client.
    private static async Task<IResult> TestAsync(ITenantContext tenantContext, IAppDbContext db, EmailSettingsService service, CancellationToken cancellationToken)
    {
        var user = await db.UserProfiles.Where(u => u.Id == tenantContext.UserId).Select(u => new { u.Email, u.DisplayName }).SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.TestAsync(user.Email, user.DisplayName, cancellationToken);
        return Results.Ok(new EmailTestResponse(result.Success, result.Message));
    }
}
