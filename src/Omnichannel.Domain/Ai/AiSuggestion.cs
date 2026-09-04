using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Ai;

/// <summary>
/// A single AI-generated reply suggestion for a conversation — doubles as the interaction log
/// PRD §69 requires (model, token usage, confidence, timestamp) and the record an agent reviews
/// before sending (Suggest mode: the AI proposes, a human decides — PRD §87, never auto-sent).
/// Stores the full suggested text (like every other message/note already persisted in this
/// system) — the Serilog "don't log message content" policy (docs/security.md) governs
/// structured application logs, not what's stored in the domain's own tables.
/// </summary>
public sealed class AiSuggestion : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public string SuggestedText { get; private set; } = string.Empty;
    public double Confidence { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int PromptTokens { get; private set; }
    public int CompletionTokens { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AiSuggestion()
    {
    }

    public static AiSuggestion Create(
        Guid tenantId, Guid conversationId, string suggestedText, double confidence,
        string model, int promptTokens, int completionTokens, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            SuggestedText = suggestedText,
            Confidence = Math.Clamp(confidence, 0, 1),
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CreatedAt = now,
        };
}
