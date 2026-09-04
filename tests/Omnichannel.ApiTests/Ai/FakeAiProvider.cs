using Omnichannel.Application.Abstractions;

namespace Omnichannel.ApiTests.Ai;

/// <summary>Test-only IAiProvider — records the context it was called with (for asserting what does/doesn't get sent to "the AI") and returns a configurable canned result, same pattern as Channels' FakeChannelAdapter.</summary>
public sealed class FakeAiProvider : IAiProvider
{
    public AiPromptContext? LastContext { get; private set; }

    public string SuggestionToReturn { get; set; } = "Thanks for reaching out — how can I help?";

    public double ConfidenceToReturn { get; set; } = 0.85;

    public bool RequiresHumanToReturn { get; set; }

    public string? EscalationReasonToReturn { get; set; }

    public bool ThrowOnNextCall { get; set; }

    public int CallCount { get; private set; }

    public Task<AiCompletionResult> GenerateSuggestionAsync(AiPromptContext context, CancellationToken cancellationToken)
    {
        CallCount++;
        LastContext = context;

        if (ThrowOnNextCall)
        {
            ThrowOnNextCall = false;
            throw new AiProviderException("Simulated provider failure.");
        }

        return Task.FromResult(new AiCompletionResult(
            SuggestionToReturn, ConfidenceToReturn, "fake-model", 10, 10, RequiresHumanToReturn, EscalationReasonToReturn));
    }
}
