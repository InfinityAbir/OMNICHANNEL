using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Channels;
using Omnichannel.Contracts.Channels;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Channels;

namespace Omnichannel.Api.Endpoints;

public static class ChannelWebhookEndpoints
{
    public static IEndpointRouteBuilder MapChannelWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Provider webhooks (public — no auth; the adapter's own signature/HMAC check is
        //      the boundary, per AGENTS.md's webhook safety rules). ----
        var webhooks = app.MapGroup("/webhooks");
        webhooks.MapGet("/{channelType}", HandleVerificationAsync).RequireRateLimiting("webhook");
        webhooks.MapPost("/{channelType}", HandleIngestAsync).RequireRateLimiting("webhook");

        // ---- Business (agent) channel configuration — reuses the same admin surface shape as
        //      the widget's own settings endpoints (ChannelsManage permission). ----
        var admin = app.MapGroup("/api/v1/channels");
        admin.MapGet("/{channelType}", GetAccountAsync).RequireAuthorization(PermissionKeys.ChannelsRead);
        admin.MapPut("/{channelType}/account", SetExternalAccountAsync).RequireAuthorization(PermissionKeys.ChannelsManage);
        admin.MapPut("/{channelType}/credentials", SetCredentialAsync).RequireAuthorization(PermissionKeys.ChannelsManage);
        admin.MapDelete("/{channelType}/credentials", DeleteCredentialAsync).RequireAuthorization(PermissionKeys.ChannelsManage);

        return app;
    }

    private static async Task<IResult> HandleVerificationAsync(
        string channelType, HttpRequest request, WebhookIngestionService ingestion, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channelType, ignoreCase: true, out var type))
        {
            return Results.NotFound();
        }

        var webhookRequest = new WebhookRequest(ToHeaderDictionary(request), ToQueryDictionary(request), string.Empty);
        var result = await ingestion.VerifyAsync(type, webhookRequest, cancellationToken);

        return result.Outcome switch
        {
            WebhookIngestOutcome.Unsupported => Results.NotFound(),
            WebhookIngestOutcome.Rejected => Results.Forbid(),
            _ => Results.Text(result.ChallengeResponse ?? string.Empty),
        };
    }

    private static async Task<IResult> HandleIngestAsync(
        string channelType, HttpRequest request, WebhookIngestionService ingestion, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channelType, ignoreCase: true, out var type))
        {
            return Results.NotFound();
        }

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var webhookRequest = new WebhookRequest(ToHeaderDictionary(request), ToQueryDictionary(request), body);

        var result = await ingestion.IngestAsync(type, webhookRequest, cancellationToken);
        return result.Outcome switch
        {
            WebhookIngestOutcome.Unsupported => Results.NotFound(),
            // Not 401/403: an invalid signature on a POST delivery is ack'd like any other
            // rejected delivery would be by a well-behaved receiver — providers must not be given
            // a signal that helps them distinguish "wrong secret" from "any other rejection"
            // (avoids leaking auth-relevant timing/behavior differences to a spoofing attempt).
            WebhookIngestOutcome.Rejected => Results.Forbid(),
            _ => Results.Ok(),
        };
    }

    private static async Task<IResult> GetAccountAsync(
        string channelType, ITenantContext tenantContext, IAppDbContext db, IChannelCredentialStore credentials, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channelType, ignoreCase: true, out var type))
        {
            return Results.NotFound();
        }

        var account = await db.ChannelAccounts.SingleOrDefaultAsync(a => a.TenantId == tenantContext.TenantId && a.Type == type, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        var configured = await credentials.ExistsAsync(account.Id, cancellationToken);
        return Results.Ok(ToResponse(account, configured));
    }

    private static async Task<IResult> SetExternalAccountAsync(
        string channelType,
        [Microsoft.AspNetCore.Mvc.FromBody] SetChannelExternalAccountRequest request,
        ITenantContext tenantContext,
        IAppDbContext db,
        IChannelCredentialStore credentials,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channelType, ignoreCase: true, out var type))
        {
            return Results.NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.ExternalAccountId))
        {
            return Results.Problem(title: "External account id is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var account = await GetOrCreateAccountAsync(db, tenantContext.TenantId, type, timeProvider, cancellationToken);
        account.SetExternalAccountId(request.ExternalAccountId, timeProvider.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique (Type, ExternalAccountId) violation — another tenant already connected this
            // provider account. Never confirm which tenant (would leak account existence).
            return Results.Problem(title: "This provider account is already connected to a channel.", statusCode: StatusCodes.Status409Conflict);
        }

        var configured = await credentials.ExistsAsync(account.Id, cancellationToken);
        return Results.Ok(ToResponse(account, configured));
    }

    private static async Task<IResult> SetCredentialAsync(
        string channelType,
        [Microsoft.AspNetCore.Mvc.FromBody] SetChannelCredentialRequest request,
        ITenantContext tenantContext,
        IAppDbContext db,
        IChannelCredentialStore credentials,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channelType, ignoreCase: true, out var type))
        {
            return Results.NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            return Results.Problem(title: "Secret is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var account = await GetOrCreateAccountAsync(db, tenantContext.TenantId, type, timeProvider, cancellationToken);
        await credentials.SetAsync(account.Id, request.Secret, cancellationToken);

        return Results.Ok(ToResponse(account, credentialConfigured: true));
    }

    private static async Task<IResult> DeleteCredentialAsync(
        string channelType, ITenantContext tenantContext, IAppDbContext db, IChannelCredentialStore credentials, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channelType, ignoreCase: true, out var type))
        {
            return Results.NotFound();
        }

        var account = await db.ChannelAccounts.SingleOrDefaultAsync(a => a.TenantId == tenantContext.TenantId && a.Type == type, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        await credentials.DeleteAsync(account.Id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<ChannelAccount> GetOrCreateAccountAsync(
        IAppDbContext db, Guid tenantId, ChannelType type, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.SingleOrDefaultAsync(a => a.TenantId == tenantId && a.Type == type, cancellationToken);
        if (account is not null)
        {
            return account;
        }

        account = ChannelAccount.Create(tenantId, type, type.ToString(), timeProvider.GetUtcNow());
        db.ChannelAccounts.Add(account);
        return account;
    }

    private static ChannelAccountAdminResponse ToResponse(ChannelAccount account, bool credentialConfigured)
        => new(account.Id, account.Type.ToString(), account.DisplayName, account.Status.ToString(), account.ExternalAccountId, credentialConfigured);

    private static Dictionary<string, string> ToHeaderDictionary(HttpRequest request)
        => request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> ToQueryDictionary(HttpRequest request)
        => request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
