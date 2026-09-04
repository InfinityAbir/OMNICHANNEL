using Omnichannel.Application.Ai;
using Omnichannel.Application.Conversations;
using Omnichannel.Contracts.Ai;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Api.Endpoints;

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/conversations/{id:guid}/ai-suggestions", GenerateSuggestionAsync)
            .RequireAuthorization(PermissionKeys.AiRead);

        app.MapPut("/api/v1/conversations/{id:guid}/ai-mode", SetConversationAiModeAsync)
            .RequireAuthorization(PermissionKeys.AiConfigure);

        app.MapGet("/api/v1/ai/auto-reply-settings", GetAutoReplySettingsAsync)
            .RequireAuthorization(PermissionKeys.AiRead);

        app.MapPut("/api/v1/ai/auto-reply-settings", UpdateAutoReplySettingsAsync)
            .RequireAuthorization(PermissionKeys.AiConfigure);

        return app;
    }

    private static async Task<IResult> GenerateSuggestionAsync(Guid id, AiSuggestionService service, CancellationToken cancellationToken)
    {
        var result = await service.GetSuggestionAsync(id, cancellationToken);

        return result.Outcome switch
        {
            AiSuggestionOutcome.ConversationNotFound => Results.NotFound(),
            AiSuggestionOutcome.LimitReached => Results.Problem(
                title: "AI suggestion limit reached.",
                detail: "This tenant's daily AI suggestion limit has been reached. Please reply manually.",
                statusCode: StatusCodes.Status429TooManyRequests),
            AiSuggestionOutcome.ProviderUnavailable => Results.Problem(
                title: "AI suggestion unavailable.",
                detail: "The AI provider could not be reached. Please reply manually.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Ok(new AiSuggestionResponse(result.SuggestionId!.Value, result.SuggestedText!, result.Confidence!.Value, DateTimeOffset.UtcNow)),
        };
    }

    private static async Task<IResult> SetConversationAiModeAsync(
        Guid id, SetConversationAiModeRequest request, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ConversationAiMode>(request.AiMode, ignoreCase: true, out var aiMode))
        {
            return Results.Problem(
                title: "Invalid AI mode.",
                detail: $"'{request.AiMode}' is not a valid AI mode. Valid values: {string.Join(", ", Enum.GetNames<ConversationAiMode>())}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var updated = await conversations.SetAiModeAsync(id, aiMode, cancellationToken);
        return updated ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetAutoReplySettingsAsync(AiAutoReplySettingsService service, CancellationToken cancellationToken)
    {
        var settings = await service.GetAsync(cancellationToken);
        return Results.Ok(ToResponse(settings));
    }

    private static async Task<IResult> UpdateAutoReplySettingsAsync(
        UpdateAiAutoReplySettingsRequest request, AiAutoReplySettingsService service, CancellationToken cancellationToken)
    {
        // Hardening (Phase 15): the stored column is bounded (character varying(4000)), so an
        // oversized payload would otherwise surface as an unhandled Postgres data-length error
        // (a 500) instead of a clean validation failure.
        if (request.BusinessHours is { Count: > 7 } || request.BusinessHours?.Values.Any(w => w.Count > 20) == true)
        {
            return Results.Problem(title: "Business hours payload too large.", statusCode: StatusCodes.Status400BadRequest);
        }

        Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowValue>>? businessHours = null;
        if (request.BusinessHours is not null)
        {
            businessHours = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowValue>>();
            foreach (var (day, windows) in request.BusinessHours)
            {
                var parsedWindows = new List<BusinessHoursWindowValue>();
                foreach (var window in windows)
                {
                    if (!TimeOnly.TryParse(window.Start, out var start) || !TimeOnly.TryParse(window.End, out var end) || start >= end)
                    {
                        return Results.Problem(
                            title: "Invalid business hours window.",
                            detail: $"'{window.Start}'-'{window.End}' on {day} is not a valid time window. Use \"HH:mm\", start before end.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    parsedWindows.Add(new BusinessHoursWindowValue(start, end));
                }

                businessHours[day] = parsedWindows;
            }
        }

        var settings = await service.UpdateAsync(request.Enabled, request.ConfidenceThreshold, request.DailyLimit, businessHours, cancellationToken);
        return Results.Ok(ToResponse(settings));
    }

    private static AiAutoReplySettingsResponse ToResponse(Omnichannel.Domain.Ai.AiAutoReplySettings settings)
    {
        var businessHours = settings.GetBusinessHours().ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<BusinessHoursWindowRequest>)kv.Value
                .Select(w => new BusinessHoursWindowRequest(
                    w.Start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    w.End.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList());

        return new AiAutoReplySettingsResponse(settings.Enabled, settings.ConfidenceThreshold, settings.DailyLimit, businessHours);
    }
}
