namespace Omnichannel.Infrastructure.Ai;

/// <summary>
/// Platform-level AI provider configuration. <see cref="Model"/> defaults to a model confirmed
/// live against Groq's own `/openai/v1/models` endpoint with the actual deployment key (not
/// guessed) at the time this phase was built — Groq's catalog changes over time, so this is a
/// deliberately overridable default, not a hardcoded assumption (see ADR-0020).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai:Groq";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    public string Model { get; set; } = "openai/gpt-oss-120b";

    /// <summary>0 or negative means unlimited — an explicit deployer choice, not a silent default (docs/ai.md's usage-limit constraint).</summary>
    public int DailySuggestionLimitPerTenant { get; set; } = 200;
}
