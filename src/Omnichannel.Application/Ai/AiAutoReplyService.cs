using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Channels;
using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Ai;

public enum AiAutoReplyOutcome
{
    /// <summary>The AI drafted and sent a reply.</summary>
    Replied,

    /// <summary>The conversation's own AiMode doesn't call for auto-reply (Disabled/SuggestOnly) — not this feature's concern, no action taken.</summary>
    SkippedModeDisabled,

    /// <summary>The tenant hasn't turned auto-reply on at all — a second, tenant-wide gate independent of any one conversation's mode.</summary>
    SkippedSettingsDisabled,

    /// <summary>Outside the tenant's configured business hours (or none configured) — eligible for escalation.</summary>
    SkippedOutsideBusinessHours,

    /// <summary>The tenant's daily auto-reply cap is already spent — eligible for escalation.</summary>
    SkippedDailyLimitReached,

    /// <summary>The AI's own confidence fell below the tenant's configured threshold — eligible for escalation.</summary>
    SkippedLowConfidence,

    /// <summary>The AI flagged this exchange as needing a human regardless of confidence (refund, complaint, high-risk, etc.) — eligible for escalation.</summary>
    SkippedRequiresHuman,

    /// <summary>The AI provider call failed — eligible for escalation.</summary>
    SkippedProviderUnavailable,

    /// <summary>A human agent replied (or the mode was turned off) while the AI call was in flight — never escalated, a human is already on it.</summary>
    SkippedHumanTookOver,
}

public sealed record AiAutoReplyResult(AiAutoReplyOutcome Outcome, Guid? MessageId = null, double? Confidence = null);

/// <summary>
/// Auto-reply decision pipeline (PRD §71): only after Suggest mode (Phase 10) is stable, and only
/// within business hours/eligibility/confidence rules — the AI never bypasses them. Conservative
/// by construction: every uncertain path (missing settings, missing business-hours config,
/// provider failure, low confidence, the AI's own "requiresHuman" flag) falls back to leaving the
/// conversation for a human, optionally escalated (PRD §71's default example table).
///
/// Deliberately takes an explicit <c>tenantId</c> rather than <see cref="ITenantContext"/> and
/// queries everything via <see cref="IAppDbContext"/>'s <c>IgnoreQueryFilters()</c> + an explicit
/// <c>TenantId ==</c> predicate: this service is invoked from three different call contexts — an
/// authenticated agent request (Manual channel), an authenticated-but-non-tenant widget-visitor
/// request (website chat), and a fully unauthenticated provider webhook (WhatsApp/Instagram/
/// Messenger) — and only the explicit-filter shape is correct in all three. Same documented
/// exception to the ambient tenant filter as <see cref="Channels.WebhookIngestionService"/>
/// (ADR-0005).
/// </summary>
public sealed class AiAutoReplyService(
    IAppDbContext db,
    TimeProvider timeProvider,
    AuditService audit,
    IRealtimeNotifier realtime,
    IAiProviderResolver aiProviderResolver,
    IKnowledgeRetrievalService knowledgeRetrieval,
    ChannelSendService channelSend)
{
    private const int HistoryWindowSize = 10;
    private const int KnowledgeTopK = 3;

    public async Task<AiAutoReplyResult> EvaluateAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var evaluationStartedAt = timeProvider.GetUtcNow();

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == conversationId, cancellationToken);
        if (conversation is null || conversation.AiMode is not (ConversationAiMode.AutoReply or ConversationAiMode.AutoReplyWithEscalation))
        {
            return new AiAutoReplyResult(AiAutoReplyOutcome.SkippedModeDisabled);
        }

        var settings = await db.AiAutoReplySettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (settings is null || !settings.Enabled)
        {
            return new AiAutoReplyResult(AiAutoReplyOutcome.SkippedSettingsDisabled);
        }

        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || !settings.IsWithinBusinessHours(evaluationStartedAt, tenant.TimeZone))
        {
            return await EscalateAsync(conversation, AiAutoReplyOutcome.SkippedOutsideBusinessHours, cancellationToken);
        }

        var startOfDayUtc = new DateTimeOffset(evaluationStartedAt.Year, evaluationStartedAt.Month, evaluationStartedAt.Day, 0, 0, 0, TimeSpan.Zero);
        var sentToday = await db.Messages.IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == tenantId && m.SenderType == MessageSenderType.Ai
                && m.Direction == MessageDirection.Outbound && m.CreatedAt >= startOfDayUtc, cancellationToken);
        if (sentToday >= settings.DailyLimit)
        {
            return await EscalateAsync(conversation, AiAutoReplyOutcome.SkippedDailyLimitReached, cancellationToken);
        }

        var history = await db.Messages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(HistoryWindowSize)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new AiTranscriptMessage(
                m.SenderType == MessageSenderType.Agent || m.SenderType == MessageSenderType.Ai ? "assistant" : "user",
                m.Text))
            .ToListAsync(cancellationToken);

        var latestCustomerMessage = history.LastOrDefault(h => h.Role == "user")?.Text;
        IReadOnlyList<AiKnowledgeSnippet>? knowledgeSnippets = null;
        if (!string.IsNullOrWhiteSpace(latestCustomerMessage))
        {
            var retrieved = await knowledgeRetrieval.RetrieveAsync(tenantId, latestCustomerMessage, KnowledgeTopK, cancellationToken);
            if (retrieved.Count > 0)
            {
                knowledgeSnippets = retrieved.Select(r => new AiKnowledgeSnippet(r.DocumentTitle, r.ChunkText)).ToList();
            }
        }

        AiCompletionResult completion;
        try
        {
            var aiProvider = await aiProviderResolver.ResolveAsync(tenantId, cancellationToken);
            completion = await aiProvider.GenerateSuggestionAsync(new AiPromptContext(tenant.Name, history, knowledgeSnippets), cancellationToken);
        }
        catch (AiProviderException)
        {
            return await EscalateAsync(conversation, AiAutoReplyOutcome.SkippedProviderUnavailable, cancellationToken);
        }

        if (completion.RequiresHuman)
        {
            return await EscalateAsync(conversation, AiAutoReplyOutcome.SkippedRequiresHuman, cancellationToken, completion.EscalationReason);
        }

        if (completion.Confidence < settings.ConfidenceThreshold)
        {
            return await EscalateAsync(conversation, AiAutoReplyOutcome.SkippedLowConfidence, cancellationToken);
        }

        // Human-takeover race guard (PRD §71 security focus: "human takeover race conditions"). The
        // AI provider call above is a real network round-trip; re-check right before sending that
        // no agent replied and the mode wasn't turned off while we were waiting on it. Re-query
        // rather than reload — IAppDbContext deliberately doesn't expose ChangeTracker.Entry
        // (AGENTS.md: no leaking EF Core internals through the Application-layer abstraction).
        var currentAiMode = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Id == conversationId)
            .Select(c => c.AiMode)
            .SingleOrDefaultAsync(cancellationToken);
        var humanRepliedSince = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ConversationId == conversationId
                && m.SenderType == MessageSenderType.Agent && m.CreatedAt >= evaluationStartedAt, cancellationToken);
        if (humanRepliedSince || currentAiMode is not (ConversationAiMode.AutoReply or ConversationAiMode.AutoReplyWithEscalation))
        {
            return new AiAutoReplyResult(AiAutoReplyOutcome.SkippedHumanTookOver);
        }

        var now = timeProvider.GetUtcNow();
        var message = Message.CreateOutbound(tenantId, conversation.Id, conversation.ChannelAccountId, MessageSenderType.Ai, completion.SuggestedText, now);
        await RouteOutboundAsync(conversation, message, completion.SuggestedText, now, cancellationToken);

        db.Messages.Add(message);
        conversation.TouchLastMessage(now, completion.SuggestedText);

        audit.Record(tenantId, null, "ai.autoreply.sent", nameof(Message), message.Id,
            new { conversationId, model = completion.Model, confidence = completion.Confidence });

        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyNewMessageAsync(
            tenantId, conversation.Id, message.Id, message.Direction, message.SenderType,
            message.ContentType, message.Text, message.CreatedAt, message.DeliveryStatus,
            message.ExternalMessageId, cancellationToken);
        await realtime.NotifyConversationUpdateAsync(
            tenantId, conversation.Id, conversation.Status, conversation.Priority, conversation.AiMode,
            conversation.LastMessageAt, conversation.LastMessagePreview, conversation.AssignedUserId, cancellationToken);

        return new AiAutoReplyResult(AiAutoReplyOutcome.Replied, message.Id, completion.Confidence);
    }

    /// <summary>Mirrors <see cref="Conversations.ConversationService"/>'s private RouteOutboundAsync exactly (recipient resolution + provider send + delivery-status marking) — small enough, and specific enough to each caller's own message-persistence flow, that a shared abstraction isn't worth the added indirection (AGENTS.md: no premature abstraction).</summary>
    private async Task RouteOutboundAsync(Conversation conversation, Message message, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == conversation.ChannelAccountId, cancellationToken);
        if (account is null)
        {
            message.MarkFailed();
            return;
        }

        var recipientExternalId = await db.ContactIdentifiers.IgnoreQueryFilters()
            .Where(i => i.ContactId == conversation.ContactId && i.ChannelType == account.Type)
            .Select(i => i.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var sendResult = recipientExternalId is null
            ? null
            : await channelSend.TrySendAsync(account, recipientExternalId, text, cancellationToken);

        if (sendResult is null)
        {
            message.MarkSent(now);
        }
        else if (sendResult.Success)
        {
            message.MarkSent(now, sendResult.ExternalMessageId);
        }
        else
        {
            message.MarkFailed();
        }
    }

    /// <summary>
    /// AutoReplyWithEscalation's whole reason to exist: when the AI would in principle have
    /// handled this but couldn't right now, flag the conversation for priority human attention
    /// (PRD §71) rather than leaving it to sit unnoticed like plain AutoReply mode does. Reuses
    /// the existing <see cref="ConversationStatus.Escalated"/> value, unused until now. Plain
    /// AutoReply mode takes no extra action here — the message just sits for a human to notice
    /// through the normal inbox flow, same as before this feature existed.
    /// </summary>
    private async Task<AiAutoReplyResult> EscalateAsync(
        Conversation conversation, AiAutoReplyOutcome outcome, CancellationToken cancellationToken, string? reason = null)
    {
        if (conversation.AiMode == ConversationAiMode.AutoReplyWithEscalation && conversation.Status != ConversationStatus.Escalated)
        {
            conversation.ChangeStatus(ConversationStatus.Escalated, timeProvider.GetUtcNow());
            audit.Record(conversation.TenantId, null, "ai.autoreply.escalated", nameof(Conversation), conversation.Id,
                new { reason = outcome.ToString(), detail = reason });

            await db.SaveChangesAsync(cancellationToken);

            await realtime.NotifyConversationUpdateAsync(
                conversation.TenantId, conversation.Id, conversation.Status, conversation.Priority, conversation.AiMode,
                conversation.LastMessageAt, conversation.LastMessagePreview, conversation.AssignedUserId, cancellationToken);
        }

        return new AiAutoReplyResult(outcome);
    }
}
