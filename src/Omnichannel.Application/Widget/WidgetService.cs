using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Ai;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Automation;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Contacts;
using Omnichannel.Domain.Conversations;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Domain.Widget;

namespace Omnichannel.Application.Widget;

public sealed record OpenSessionResult(WidgetChannelSettings? Settings, string? Error = null, bool OriginBlocked = false);

public sealed class WidgetService(
    IAppDbContext db,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    AuditService audit,
    IRealtimeNotifier realtime,
    IWidgetSessionTokenGenerator tokenGenerator,
    AiAutoReplyService autoReply,
    AutomationRuleService automationRules)
{
    /// <summary>Opens a widget session for an anonymous visitor. Public (pre-auth): resolves the
    /// tenant by its public slug, validates the request Origin against the tenant's allowlist, and
    /// only then issues a signed session token.</summary>
    public async Task<OpenSessionResult> ValidateOpenOriginAsync(string tenantSlug, string? origin)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == tenantSlug);
        if (tenant is null)
        {
            return new OpenSessionResult(null, "Unknown site.");
        }

        var settings = await db.WidgetSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(w => w.TenantId == tenant.Id);

        if (settings is null || !settings.Enabled || !settings.IsOriginAllowed(origin))
        {
            return new OpenSessionResult(settings, "Origin not allowed.", OriginBlocked: true);
        }

        return new OpenSessionResult(settings);
    }

    public async Task<Guid> EnsureVisitorConversationAsync(
        Guid tenantId, Guid channelAccountId, string? visitorKey, string? visitorName, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        Guid contactId;
        if (!string.IsNullOrWhiteSpace(visitorKey))
        {
            // Open is unauthenticated so the global tenant filter is Guid.Empty; bypass it and
            // scope explicitly by tenantId (the open path has already resolved the tenant from the
            // slug + origin-checked widget settings).
            var identifier = await db.ContactIdentifiers.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    i => i.TenantId == tenantId && i.ChannelType == ChannelType.WebsiteChat && i.Value == visitorKey.Trim(),
                    cancellationToken);
            if (identifier is not null)
            {
                contactId = identifier.ContactId;
            }
            else
            {
                contactId = CreateContact(tenantId, visitorName ?? "Website visitor", now);
                db.ContactIdentifiers.Add(ContactIdentifier.Create(tenantId, contactId, ChannelType.WebsiteChat, visitorKey.Trim(), now));
            }
        }
        else
        {
            contactId = CreateContact(tenantId, visitorName ?? "Website visitor", now);
        }

        // Reuse an existing OPEN conversation for this visitor+channel if one exists.
        var existing = await db.Conversations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.ContactId == contactId && c.ChannelAccountId == channelAccountId && c.Status != ConversationStatus.Closed,
                cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var conversation = Conversation.Create(tenantId, contactId, channelAccountId, now);
        db.Conversations.Add(conversation);
        audit.Record(tenantId, tenantContext.UserId, "conversation.created", nameof(Conversation), conversation.Id);
        await db.SaveChangesAsync(cancellationToken);
        return conversation.Id;
    }

    public Task<string> IssueTokenAsync(Guid tenantId, Guid visitorId, Guid conversationId, DateTimeOffset now, CancellationToken cancellationToken)
        => tokenGenerator.GenerateAsync(tenantId, visitorId, Guid.NewGuid(), conversationId, now, cancellationToken);

    public async Task<(bool Ok, Message? Message)> SendInboundAsync(
        Guid tenantId, Guid conversationId, Guid channelAccountId, string text, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var conversation = await db.Conversations
            .SingleOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenantId, cancellationToken);
        if (conversation is null)
        {
            return (false, null);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var message = Message.CreateInbound(tenantId, conversation.Id, channelAccountId, MessageSenderType.Customer, text.Trim(), now);
            db.Messages.Add(message);
            conversation.TouchLastMessage(now, text.Trim());
            await db.SaveChangesAsync(cancellationToken);

            await realtime.NotifyNewMessageAsync(
                tenantId, conversation.Id, message.Id, message.Direction, message.SenderType,
                message.ContentType, message.Text, message.CreatedAt, message.DeliveryStatus,
                message.ExternalMessageId, cancellationToken);
            await realtime.NotifyConversationUpdateAsync(
                tenantId, conversation.Id, conversation.Status, conversation.Priority,
                conversation.AiMode, conversation.LastMessageAt, conversation.LastMessagePreview,
                conversation.AssignedUserId, cancellationToken);

            // AI auto-reply (Phase 12) and automation rules (Phase 13) — a widget visitor
            // message is always genuine inbound customer content.
            await automationRules.EvaluateAsync(tenantId, conversation.Id, message.Text, cancellationToken);
            await autoReply.EvaluateAsync(tenantId, conversation.Id, cancellationToken);

            return (true, message);
        }

        return (true, null);
    }

    public async Task<IReadOnlyCollection<Message>> GetThreadAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId && m.TenantId == tenantId)
            .OrderBy(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return messages;
    }

    public async Task<Guid?> ResolveChannelAccountIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await db.WidgetSettings
            .SingleOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);
        return settings?.ChannelAccountId;
    }

    public async Task<Guid> ResolveContactIdAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var contactId = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.Id == conversationId && c.TenantId == tenantId)
            .Select(c => (Guid?)c.ContactId)
            .SingleOrDefaultAsync(cancellationToken);
        return contactId ?? Guid.Empty;
    }

    private Guid CreateContact(Guid tenantId, string displayName, DateTimeOffset now)
    {
        var contact = Contact.Create(tenantId, displayName, now);
        db.Contacts.Add(contact);
        return contact.Id;
    }
}
