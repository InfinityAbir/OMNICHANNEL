using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Widget;
using Omnichannel.Contracts.Widget;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Widget;

namespace Omnichannel.Api.Endpoints;

public static class WidgetEndpoints
{
    private const int MaxMessageLength = 4000;

    public static IEndpointRouteBuilder MapWidgetEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Public embed endpoints (no auth; the session token is issued only after the
        //      Origin header passes the tenant's widget allowlist). ----
        var embed = app.MapGroup("/widget");
        embed.MapPost("/{slug}/session", OpenSessionAsync)
            .RequireCors("WidgetEmbed")
            .RequireRateLimiting("widget");

        // ---- Visitor-facing endpoints (authenticated via the "Widget" bearer scheme). ----
        var visitor = app.MapGroup("/widget");
        visitor.MapPost("/conversations/{conversationId:guid}/messages", SendMessageAsync)
            .RequireAuthorization("WidgetSession")
            .RequireCors("WidgetEmbed")
            .RequireRateLimiting("widget");
        visitor.MapGet("/conversations/{conversationId:guid}/messages", GetThreadAsync)
            .RequireAuthorization("WidgetSession")
            .RequireCors("WidgetEmbed");

        // ---- Business (agent) configuration endpoints. ----
        var admin = app.MapGroup("/api/v1/channels/widget");
        admin.MapGet("/", GetSettingsAsync).RequireAuthorization(PermissionKeys.ChannelsManage);
        admin.MapPut("/origins", UpdateOriginsAsync).RequireAuthorization(PermissionKeys.ChannelsManage);

        return app;
    }

    private static async Task<IResult> OpenSessionAsync(
        string slug,
        [Microsoft.AspNetCore.Mvc.FromBody] WidgetSessionOpenRequest request,
        WidgetService widget,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var validation = await widget.ValidateOpenOriginAsync(slug, http.Request.Headers.Origin.ToString());
        if (validation.Settings is null)
        {
            return Results.Problem(title: "Unknown site.", statusCode: StatusCodes.Status404NotFound);
        }
        if (validation.OriginBlocked || !validation.Settings.Enabled)
        {
            return Results.Problem(title: "Origin not allowed.", statusCode: StatusCodes.Status403Forbidden);
        }

        var tenantId = validation.Settings.TenantId;
        var conversationId = await widget.EnsureVisitorConversationAsync(
            tenantId, validation.Settings.ChannelAccountId, request.VisitorKey, request.VisitorName, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var contactId = await widget.ResolveContactIdAsync(tenantId, conversationId, cancellationToken);
        var token = await widget.IssueTokenAsync(tenantId, contactId, conversationId, now, cancellationToken);

        var connectionUrl = $"{http.Request.Scheme}://{http.Request.Host}/hubs/widget";
        return Results.Ok(new WidgetSessionResponse(
            token,
            Guid.Empty, // session id is embedded in the token; not separately surfaced
            conversationId,
            validation.Settings.ChannelAccountId,
            connectionUrl,
            now.AddMinutes(30)));
    }

    private static async Task<IResult> SendMessageAsync(
        Guid conversationId,
        [Microsoft.AspNetCore.Mvc.FromBody] WidgetSendRequest request,
        ITenantContext tenantContext,
        HttpContext http,
        WidgetService widget,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsAuthenticated || tenantContext.TenantId == Guid.Empty)
        {
            return Results.Unauthorized();
        }
        if (!TokenConversationMatches(http, conversationId))
        {
            // Never confirm another visitor's conversation exists (matches ADR-0012's
            // cross-tenant convention: 404, not 403) — the widget token is scoped to exactly one
            // conversation via its own conversation_id claim, checked here since the route value
            // is otherwise client-supplied and would let any tenant visitor read/write any other
            // visitor's conversation by guessing its id.
            return Results.NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxMessageLength)
        {
            return Results.Problem(title: "Invalid message.", detail: "Message must be non-empty and at most 4000 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var channelAccountId = await widget.ResolveChannelAccountIdAsync(tenantContext.TenantId, cancellationToken);
        if (channelAccountId is null)
        {
            return Results.NotFound();
        }

        var (ok, message) = await widget.SendInboundAsync(
            tenantContext.TenantId, conversationId, channelAccountId.Value, request.Text, cancellationToken);
        if (!ok)
        {
            return Results.NotFound();
        }

        return Results.Ok(new WidgetMessageResponse(
            message!.Id, message.Direction.ToString(), message.SenderType.ToString(),
            message.ContentType.ToString(), message.Text, message.CreatedAt, message.DeliveryStatus.ToString()));
    }

    private static async Task<IResult> GetThreadAsync(
        Guid conversationId,
        ITenantContext tenantContext,
        HttpContext http,
        WidgetService widget,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsAuthenticated || tenantContext.TenantId == Guid.Empty)
        {
            return Results.Unauthorized();
        }
        if (!TokenConversationMatches(http, conversationId))
        {
            return Results.NotFound();
        }

        var messages = await widget.GetThreadAsync(tenantContext.TenantId, conversationId, cancellationToken);
        var mapped = messages
            .Select(m => new WidgetMessageResponse(
                m.Id, m.Direction.ToString(), m.SenderType.ToString(),
                m.ContentType.ToString(), m.Text, m.CreatedAt, m.DeliveryStatus.ToString()))
            .ToList();

        return Results.Ok(new WidgetThreadResponse(conversationId, mapped));
    }

    private static async Task<IResult> GetSettingsAsync(ITenantContext tenantContext, IAppDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsAuthenticated || tenantContext.TenantId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var slug = await db.Tenants.Where(t => t.Id == tenantContext.TenantId).Select(t => t.Slug).SingleOrDefaultAsync(cancellationToken);
        var settings = await db.WidgetSettings.SingleOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
        if (settings is null)
        {
            return Results.NotFound();
        }

        var embedBase = $"{http.Request.Scheme}://{http.Request.Host}/widget";
        var snippet = $"<script src=\"{embedBase}/embed.js\" data-slug=\"{slug}\" defer></script>";
        return Results.Ok(new WidgetSettingsResponse(
            settings.ChannelAccountId, settings.Enabled, settings.GetAllowedOrigins(), slug!, snippet));
    }

    private static async Task<IResult> UpdateOriginsAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] WidgetOriginsUpdateRequest request,
        ITenantContext tenantContext,
        IAppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsAuthenticated || tenantContext.TenantId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var settings = await db.WidgetSettings.SingleOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
        if (settings is null)
        {
            return Results.NotFound();
        }

        var origins = request.Origins
            .Where(o => Uri.TryCreate(o, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .Select(o => o.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        settings.SetAllowedOrigins(origins, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new WidgetSettingsResponse(
            settings.ChannelAccountId, settings.Enabled, settings.GetAllowedOrigins(), string.Empty, string.Empty));
    }

    private static bool TokenConversationMatches(HttpContext http, Guid routeConversationId)
    {
        var claim = http.User.FindFirst(WidgetClaimNames.ConversationId)?.Value;
        return Guid.TryParse(claim, out var tokenConversationId) && tokenConversationId == routeConversationId;
    }
}
