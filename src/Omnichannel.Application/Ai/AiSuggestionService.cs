using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Ai;

public enum AiSuggestionOutcome
{
    Generated,
    ConversationNotFound,
    LimitReached,
    ProviderUnavailable,
}

public sealed record AiSuggestionResult(
    AiSuggestionOutcome Outcome, Guid? SuggestionId = null, string? SuggestedText = null, double? Confidence = null);

/// <summary>
/// Suggest-mode workflow (PRD §69): builds a bounded, tenant-scoped context, calls the AI
/// provider, logs the interaction, and always fails safe — a provider error or exhausted usage
/// limit falls back to "ask a human" rather than surfacing a raw error or silently retrying
/// forever (docs/ai.md's "safe fallback to human handling" constraint).
/// </summary>
public sealed class AiSuggestionService(
    IAppDbContext db,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    AuditService audit,
    IAiProviderResolver aiProviderResolver,
    IAiUsageLimiter usageLimiter,
    IKnowledgeRetrievalService knowledgeRetrieval)
{
    private const int HistoryWindowSize = 10;
    private const int KnowledgeTopK = 3;

    public async Task<AiSuggestionResult> GetSuggestionAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return new AiSuggestionResult(AiSuggestionOutcome.ConversationNotFound);
        }

        if (!await usageLimiter.TryConsumeAsync(tenantContext.TenantId, cancellationToken))
        {
            return new AiSuggestionResult(AiSuggestionOutcome.LimitReached);
        }

        // Bounded window, chronological order, and — critically — internal notes are never
        // included: they're agent-only/confidential by design (PRD §18) and must never leave the
        // system to a third-party AI provider (AGENTS.md: "sensitive data sent to AI" is an
        // explicit security review focus). Only the customer-visible message thread is context.
        var history = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(HistoryWindowSize)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new AiTranscriptMessage(
                m.SenderType == MessageSenderType.Agent || m.SenderType == MessageSenderType.Ai ? "assistant" : "user",
                m.Text))
            .ToListAsync(cancellationToken);

        var tenantName = await db.Tenants
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "the business";

        // Retrieve knowledge relevant to the customer's latest message (PRD §70+§69 tie-in) — a
        // best-effort enhancement, not a hard dependency: an empty/failed lookup still lets the
        // suggestion proceed with conversation history alone, same "safe fallback" discipline as
        // everything else in this method.
        var latestCustomerMessage = history.LastOrDefault(h => h.Role == "user")?.Text;
        IReadOnlyList<AiKnowledgeSnippet>? knowledgeSnippets = null;
        if (!string.IsNullOrWhiteSpace(latestCustomerMessage))
        {
            var retrieved = await knowledgeRetrieval.RetrieveAsync(tenantContext.TenantId, latestCustomerMessage, KnowledgeTopK, cancellationToken);
            if (retrieved.Count > 0)
            {
                knowledgeSnippets = retrieved.Select(r => new AiKnowledgeSnippet(r.DocumentTitle, r.ChunkText)).ToList();
            }
        }

        AiCompletionResult completion;
        try
        {
            var aiProvider = await aiProviderResolver.ResolveAsync(tenantContext.TenantId, cancellationToken);
            completion = await aiProvider.GenerateSuggestionAsync(new AiPromptContext(tenantName, history, knowledgeSnippets), cancellationToken);
        }
        catch (AiProviderException)
        {
            return new AiSuggestionResult(AiSuggestionOutcome.ProviderUnavailable);
        }

        var now = timeProvider.GetUtcNow();
        var suggestion = AiSuggestion.Create(
            tenantContext.TenantId, conversationId, completion.SuggestedText, completion.Confidence,
            completion.Model, completion.PromptTokens, completion.CompletionTokens, now);
        db.AiSuggestions.Add(suggestion);

        audit.Record(tenantContext.TenantId, tenantContext.UserId, "ai.suggestion.generated", nameof(AiSuggestion), suggestion.Id,
            new { conversationId, model = completion.Model, confidence = completion.Confidence });

        await db.SaveChangesAsync(cancellationToken);

        return new AiSuggestionResult(AiSuggestionOutcome.Generated, suggestion.Id, completion.SuggestedText, completion.Confidence);
    }
}
