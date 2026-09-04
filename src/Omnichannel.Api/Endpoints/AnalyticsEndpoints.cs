using Omnichannel.Application.Analytics;
using Omnichannel.Contracts.Analytics;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/analytics/summary", GetSummaryAsync).RequireAuthorization(PermissionKeys.AnalyticsRead);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        DateTimeOffset? from, DateTimeOffset? to, AnalyticsService analytics, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rangeTo = to ?? now;
        var rangeFrom = from ?? rangeTo.AddDays(-30);

        if (rangeFrom > rangeTo)
        {
            return Results.Problem(title: "Invalid date range.", detail: "'from' must not be after 'to'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var summary = await analytics.GetSummaryAsync(rangeFrom, rangeTo, cancellationToken);
        return Results.Ok(new AnalyticsSummaryResponse(
            summary.From, summary.To, summary.TotalConversations, summary.OpenConversations, summary.PendingConversations,
            summary.EscalatedConversations, summary.ResolvedConversations, summary.ClosedConversations,
            summary.AverageFirstResponseMinutes, summary.AverageResolutionMinutes, summary.ResolutionRatePercent,
            summary.AiSuggestionsGenerated, summary.AverageAiSuggestionConfidence, summary.AiAutoRepliesSent,
            summary.ByChannel.Select(c => new ChannelMetricResponse(c.ChannelType, c.ConversationCount)).ToList(),
            summary.ByAgent.Select(a => new AgentMetricResponse(a.AgentUserId, a.AgentDisplayName, a.AssignedConversationCount, a.ClosedConversationCount)).ToList()));
    }
}
