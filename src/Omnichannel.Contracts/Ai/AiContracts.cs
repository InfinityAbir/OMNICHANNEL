namespace Omnichannel.Contracts.Ai;

public sealed record AiSuggestionResponse(Guid Id, string SuggestedText, double Confidence, DateTimeOffset CreatedAt);

/// <summary>One open time window on a given day, tenant-local time, "HH:mm" 24-hour. Same-day only.</summary>
public sealed record BusinessHoursWindowRequest(string Start, string End);

public sealed record AiAutoReplySettingsResponse(
    bool Enabled,
    double ConfidenceThreshold,
    int DailyLimit,
    IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>> BusinessHours);

public sealed record UpdateAiAutoReplySettingsRequest(
    bool Enabled,
    double ConfidenceThreshold,
    int DailyLimit,
    IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>>? BusinessHours);

public sealed record SetConversationAiModeRequest(string AiMode);
