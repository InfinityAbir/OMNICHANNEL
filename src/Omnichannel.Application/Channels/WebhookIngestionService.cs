using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Ai;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Automation;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Contacts;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Channels;

public enum WebhookIngestOutcome
{
    Accepted,
    Rejected,
    Unsupported,
}

public sealed record WebhookIngestResult(WebhookIngestOutcome Outcome, string? ChallengeResponse = null, string? Reason = null);

/// <summary>
/// The generic webhook processing pipeline (PRD §65): verify → parse → resolve tenant/account →
/// idempotent persist → realtime notify. Provider-specific work (signature format, payload
/// schema) lives entirely behind <see cref="IChannelAdapter"/>; nothing here knows about any one
/// provider. One malformed event in a batch is skipped, not fatal to the whole delivery
/// (AGENTS.md: "safely handle retries, duplicates, out-of-order delivery, and partial
/// failures").
/// </summary>
public sealed class WebhookIngestionService(
    IAppDbContext db,
    TimeProvider timeProvider,
    AuditService audit,
    IRealtimeNotifier realtime,
    IChannelAdapterRegistry registry,
    AiAutoReplyService autoReply,
    AutomationRuleService automationRules)
{
    public async Task<WebhookIngestResult> VerifyAsync(ChannelType type, WebhookRequest request, CancellationToken cancellationToken)
    {
        var adapter = registry.Resolve(type);
        if (adapter is null)
        {
            return new WebhookIngestResult(WebhookIngestOutcome.Unsupported, Reason: "No adapter registered for this channel.");
        }

        var verification = await adapter.VerifyWebhookAsync(request, cancellationToken);
        return verification.IsValid
            ? new WebhookIngestResult(WebhookIngestOutcome.Accepted, verification.ChallengeResponse)
            : new WebhookIngestResult(WebhookIngestOutcome.Rejected, Reason: verification.FailureReason);
    }

    public async Task<WebhookIngestResult> IngestAsync(ChannelType type, WebhookRequest request, CancellationToken cancellationToken)
    {
        var adapter = registry.Resolve(type);
        if (adapter is null)
        {
            return new WebhookIngestResult(WebhookIngestOutcome.Unsupported, Reason: "No adapter registered for this channel.");
        }

        var verification = await adapter.VerifyWebhookAsync(request, cancellationToken);
        if (!verification.IsValid)
        {
            return new WebhookIngestResult(WebhookIngestOutcome.Rejected, Reason: verification.FailureReason);
        }

        var events = await adapter.ParseWebhookAsync(request, cancellationToken);
        foreach (var @event in events)
        {
            try
            {
                await ProcessEventAsync(type, @event, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent delivery of the same event (provider retry) raced us on the
                // (ChannelAccountId, ExternalMessageId) unique index — benign, the other
                // delivery already persisted it. Same pattern as RoleSeeder's seed race.
                db.ClearChangeTracker();
            }
        }

        return new WebhookIngestResult(WebhookIngestOutcome.Accepted);
    }

    private async Task ProcessEventAsync(ChannelType type, NormalizedInboundEvent @event, CancellationToken cancellationToken)
    {
        // Webhook callers are unauthenticated (no tenant JWT — the provider calls us directly),
        // so account resolution must run unfiltered and then scope everything downstream by the
        // tenant it resolves to. This is the third documented exception to the global tenant
        // filter, alongside AuthService's login/refresh lookup and WidgetService's origin/slug
        // resolution — all three share the same shape: a public, pre-authentication lookup that
        // *establishes* tenant context rather than assuming it (ADR-0005).
        var account = await db.ChannelAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.Type == type && a.ExternalAccountId == @event.ProviderAccountExternalId, cancellationToken);
        if (account is null)
        {
            // Unmapped provider account — no tenant has connected it. Not an error: silently
            // drop (nothing to route it to), matching "safely handle... partial failures".
            return;
        }

        if (@event.Kind == NormalizedInboundEventKind.StatusUpdate)
        {
            await ApplyStatusUpdateAsync(account, @event, cancellationToken);
            return;
        }

        await ApplyInboundMessageAsync(account, @event, cancellationToken);
    }

    private async Task ApplyStatusUpdateAsync(ChannelAccount account, NormalizedInboundEvent @event, CancellationToken cancellationToken)
    {
        var message = await db.Messages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                m => m.ChannelAccountId == account.Id && m.ExternalMessageId == @event.ExternalMessageId,
                cancellationToken);
        if (message is null || @event.Status is null)
        {
            return;
        }

        message.ApplyProviderStatus(@event.Status.Value, @event.OccurredAt ?? timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyMessageStatusAsync(
            account.TenantId, message.ConversationId, message.Id, message.DeliveryStatus,
            message.SentAt, message.DeliveredAt, message.ReadAt, cancellationToken);
    }

    private async Task ApplyInboundMessageAsync(ChannelAccount account, NormalizedInboundEvent @event, CancellationToken cancellationToken)
    {
        // Idempotency check first (PRD §17) — a provider retrying an already-processed delivery
        // must never create a duplicate message. The unique index is the authoritative guard
        // (race handled by the caller's DbUpdateException catch); this check just avoids the
        // round-trip in the common (non-race) case.
        var alreadyProcessed = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.ChannelAccountId == account.Id && m.ExternalMessageId == @event.ExternalMessageId, cancellationToken);
        if (alreadyProcessed)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var occurredAt = @event.OccurredAt ?? now;
        var visitorKey = @event.VisitorExternalId;

        Guid contactId;
        if (!string.IsNullOrWhiteSpace(visitorKey))
        {
            var identifier = await db.ContactIdentifiers.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    i => i.TenantId == account.TenantId && i.ChannelType == account.Type && i.Value == visitorKey,
                    cancellationToken);
            contactId = identifier?.ContactId ?? CreateContact(account.TenantId, @event.VisitorDisplayName, visitorKey, account.Type, now);
        }
        else
        {
            contactId = CreateContact(account.TenantId, @event.VisitorDisplayName, null, account.Type, now);
        }

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                c => c.TenantId == account.TenantId && c.ContactId == contactId && c.ChannelAccountId == account.Id && c.Status != ConversationStatus.Closed,
                cancellationToken);
        if (conversation is null)
        {
            conversation = Conversation.Create(account.TenantId, contactId, account.Id, occurredAt);
            db.Conversations.Add(conversation);
        }

        var message = Message.CreateInbound(
            account.TenantId, conversation.Id, account.Id, MessageSenderType.Customer,
            @event.Text ?? string.Empty, occurredAt, @event.ExternalMessageId);
        db.Messages.Add(message);
        conversation.TouchLastMessage(occurredAt, message.Text);

        audit.Record(account.TenantId, null, "message.received", nameof(Message), message.Id,
            new { channel = account.Type.ToString(), externalMessageId = @event.ExternalMessageId });

        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyNewMessageAsync(
            account.TenantId, conversation.Id, message.Id, message.Direction, message.SenderType,
            message.ContentType, message.Text, message.CreatedAt, message.DeliveryStatus,
            message.ExternalMessageId, cancellationToken);
        await realtime.NotifyConversationUpdateAsync(
            account.TenantId, conversation.Id, conversation.Status, conversation.Priority,
            conversation.AiMode, conversation.LastMessageAt, conversation.LastMessagePreview,
            conversation.AssignedUserId, cancellationToken);

        // AI auto-reply (Phase 12) and automation rules (Phase 13) — this is always a genuine
        // inbound customer message (the provider only sends us the customer side of the
        // conversation), so no direction/sender guard is needed here unlike ConversationService's
        // agent-facing AddMessageAsync.
        await automationRules.EvaluateAsync(account.TenantId, conversation.Id, message.Text, cancellationToken);
        await autoReply.EvaluateAsync(account.TenantId, conversation.Id, cancellationToken);
    }

    private Guid CreateContact(Guid tenantId, string? displayName, string? visitorKey, ChannelType channelType, DateTimeOffset now)
    {
        var contact = Contact.Create(tenantId, string.IsNullOrWhiteSpace(displayName) ? $"{channelType} contact" : displayName.Trim(), now);
        db.Contacts.Add(contact);

        if (!string.IsNullOrWhiteSpace(visitorKey))
        {
            db.ContactIdentifiers.Add(ContactIdentifier.Create(tenantId, contact.Id, channelType, visitorKey, now));
        }

        return contact.Id;
    }
}
