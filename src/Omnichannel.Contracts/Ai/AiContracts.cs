namespace Omnichannel.Contracts.Ai;

public sealed record AiSuggestionResponse(Guid Id, string SuggestedText, double Confidence, DateTimeOffset CreatedAt);
