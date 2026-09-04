using Omnichannel.Application.Ai;
using Omnichannel.Contracts.Ai;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/conversations/{id:guid}/ai-suggestions", GenerateSuggestionAsync)
            .RequireAuthorization(PermissionKeys.AiRead);

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
}
