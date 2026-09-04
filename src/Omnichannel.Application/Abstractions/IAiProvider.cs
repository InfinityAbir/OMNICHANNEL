namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Provider-agnostic AI abstraction (PRD §69/§87, recorded ahead of time in docs/ai.md) — the
/// application must not depend on a specific AI vendor. One role only for now (Phase 10 Suggest
/// mode): draft a reply suggestion from conversation context; the human decides whether to send
/// it, edit it, or discard it. No AI output is ever sent to a customer without that human step.
/// </summary>
public interface IAiProvider
{
    Task<AiCompletionResult> GenerateSuggestionAsync(AiPromptContext context, CancellationToken cancellationToken);
}

/// <summary>One prior message in the conversation transcript given to the model — Role is "user" (customer/system-originated) or "assistant" (agent/AI-originated), never anything richer; message text is passed as data, never concatenated into instruction text (prompt-injection defense, PRD §37).</summary>
public sealed record AiTranscriptMessage(string Role, string Text);

/// <summary>A retrieved knowledge-base snippet offered as reference material — attributed to its source document, and treated by GroqAiProvider's prompt as untrusted reference content, never an instruction (PRD §70's "source attribution" + the same prompt-injection discipline applied to documents, not just conversation history).</summary>
public sealed record AiKnowledgeSnippet(string DocumentTitle, string Text);

public sealed record AiPromptContext(
    string BusinessName,
    IReadOnlyList<AiTranscriptMessage> History,
    IReadOnlyList<AiKnowledgeSnippet>? KnowledgeSnippets = null);

/// <summary>
/// <paramref name="RequiresHuman"/> is the model's own self-assessment that this exchange needs a
/// human regardless of confidence — refunds, complaints, anything outside what the business's
/// knowledge covers, or anything risky/sensitive (PRD §71's example table: "Refund -> human",
/// "High risk -> human"). Auto-reply mode (Phase 12) treats this as authoritative and never
/// auto-sends when it's true; Suggest mode (Phase 10) ignores it today but stores it.
/// </summary>
public sealed record AiCompletionResult(
    string SuggestedText,
    double Confidence,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    bool RequiresHuman = false,
    string? EscalationReason = null);

/// <summary>Thrown by an IAiProvider when the call itself fails (network, auth, malformed provider response) — distinct from a low-confidence-but-successful completion, so callers can apply the "safe fallback to human handling" rule (docs/ai.md) instead of surfacing a raw provider error.</summary>
public sealed class AiProviderException(string message, Exception? innerException = null) : Exception(message, innerException);
