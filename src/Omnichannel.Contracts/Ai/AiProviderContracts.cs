namespace Omnichannel.Contracts.Ai;

public sealed record AiProviderSettingsResponse(string ProviderKind, string? BaseUrl, string Model, bool HasApiKey);

public sealed record UpdateAiProviderSettingsRequest(string ProviderKind, string? BaseUrl, string Model, string? ApiKey);

public sealed record AiProviderTestResponse(bool Success, string Message);

public sealed record DetectAiProviderRequest(string ApiKey, string? ProviderKind, string? BaseUrl);

public sealed record DetectAiProviderResponse(
    bool Success, string Message, string ProviderKind, string? BaseUrl, IReadOnlyList<string> AvailableModels, string? SuggestedModel);
