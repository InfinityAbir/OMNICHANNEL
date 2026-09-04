namespace Omnichannel.Contracts.Analytics;

public sealed record ChannelMetricResponse(string ChannelType, int ConversationCount);

public sealed record AgentMetricResponse(Guid AgentUserId, string AgentDisplayName, int AssignedConversationCount, int ClosedConversationCount);

public sealed record AnalyticsSummaryResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalConversations,
    int OpenConversations,
    int PendingConversations,
    int EscalatedConversations,
    int ResolvedConversations,
    int ClosedConversations,
    double? AverageFirstResponseMinutes,
    double? AverageResolutionMinutes,
    double ResolutionRatePercent,
    int AiSuggestionsGenerated,
    double? AverageAiSuggestionConfidence,
    int AiAutoRepliesSent,
    IReadOnlyList<ChannelMetricResponse> ByChannel,
    IReadOnlyList<AgentMetricResponse> ByAgent);
