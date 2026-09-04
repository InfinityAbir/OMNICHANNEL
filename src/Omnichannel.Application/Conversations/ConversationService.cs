using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Ai;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Automation;
using Omnichannel.Application.Channels;
using Omnichannel.Application.Common;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Conversations;

public sealed class ConversationService(
    IAppDbContext db,
    AuditService audit,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IRealtimeNotifier realtime,
    ChannelSendService channelSend,
    AiAutoReplyService autoReply,
    AutomationRuleService automationRules)
{
    private const int MaxPageSize = 100;

    public async Task<Conversation> CreateManualAsync(Guid contactId, string? initialMessageText, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var manualChannel = await db.ChannelAccounts.SingleAsync(c => c.Type == ChannelType.Manual, cancellationToken);

        var conversation = Conversation.Create(tenantContext.TenantId, contactId, manualChannel.Id, now);
        db.Conversations.Add(conversation);

        Message? initialMessage = null;
        if (!string.IsNullOrWhiteSpace(initialMessageText))
        {
            initialMessage = Message.CreateInbound(
                tenantContext.TenantId, conversation.Id, manualChannel.Id, MessageSenderType.Customer, initialMessageText, now);
            db.Messages.Add(initialMessage);
            conversation.TouchLastMessage(now, initialMessageText);
        }

        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.created", nameof(Conversation), conversation.Id);
        await db.SaveChangesAsync(cancellationToken);

        if (initialMessage is not null)
        {
            await realtime.NotifyNewMessageAsync(
                tenantContext.TenantId,
                conversation.Id,
                initialMessage.Id,
                initialMessage.Direction,
                initialMessage.SenderType,
                initialMessage.ContentType,
                initialMessage.Text,
                initialMessage.CreatedAt,
                initialMessage.DeliveryStatus,
                initialMessage.ExternalMessageId,
                cancellationToken);

            await realtime.NotifyConversationUpdateAsync(
                tenantContext.TenantId,
                conversation.Id,
                null, // status
                null, // priority
                null, // aiMode
                conversation.LastMessageAt,
                conversation.LastMessagePreview,
                conversation.AssignedUserId,
                cancellationToken);
        }

        return conversation;
    }

    public async Task<Message?> AddMessageAsync(
        Guid conversationId, MessageDirection direction, MessageSenderType senderType, string text, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var message = direction == MessageDirection.Outbound
            ? Message.CreateOutbound(tenantContext.TenantId, conversation.Id, conversation.ChannelAccountId, senderType, text, now)
            : Message.CreateInbound(tenantContext.TenantId, conversation.Id, conversation.ChannelAccountId, senderType, text, now);

        if (direction == MessageDirection.Outbound)
        {
            await RouteOutboundAsync(conversation, message, text, now, cancellationToken);
        }

        db.Messages.Add(message);
        conversation.TouchLastMessage(now, text);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "message.sent", nameof(Message), message.Id,
            new { direction = direction.ToString() });

        await db.SaveChangesAsync(cancellationToken);

        // Emit realtime events
        await realtime.NotifyNewMessageAsync(
            tenantContext.TenantId,
            conversation.Id,
            message.Id,
            message.Direction,
            message.SenderType,
            message.ContentType,
            message.Text,
            message.CreatedAt,
            message.DeliveryStatus,
            message.ExternalMessageId,
            cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            null, // status
            null, // priority
            null, // aiMode
            conversation.LastMessageAt,
            conversation.LastMessagePreview,
            conversation.AssignedUserId,
            cancellationToken);

        // For outbound messages, also emit the actual resulting status — Sent for the common
        // case, but Failed when RouteOutboundAsync's provider send didn't succeed, so the agent
        // sees the real outcome rather than an optimistic "Sent" that never happened.
        if (direction == MessageDirection.Outbound)
        {
            await realtime.NotifyMessageStatusAsync(
                tenantContext.TenantId,
                conversation.Id,
                message.Id,
                message.DeliveryStatus,
                message.SentAt,
                null, // deliveredAt
                null, // readAt
                cancellationToken);
        }

        // Stream outbound agent replies to the website-chat widget (visitor-facing hub) when this
        // conversation belongs to the WebsiteChat channel. The visitor never joins the agent tenant
        // group; it receives only its own conversation's messages.
        if (direction == MessageDirection.Outbound)
        {
            var channelType = await db.ChannelAccounts
                .Where(c => c.Id == conversation.ChannelAccountId)
                .Select(c => c.Type)
                .SingleOrDefaultAsync(cancellationToken);

            if (channelType == ChannelType.WebsiteChat)
            {
                await realtime.NotifyVisitorMessageAsync(
                    conversation.Id,
                    message.Id,
                    message.Direction.ToString(),
                    message.SenderType.ToString(),
                    message.ContentType.ToString(),
                    message.Text,
                    message.CreatedAt,
                    message.DeliveryStatus.ToString(),
                    cancellationToken);
            }
        }

        // High priority alert notification
        if (conversation.Priority == ConversationPriority.High || conversation.Priority == ConversationPriority.Urgent)
        {
            await realtime.NotifyHighPriorityAlertAsync(
                tenantContext.TenantId,
                conversation.Id,
                "High Priority Conversation",
                $"New message in high-priority conversation: {text[..Math.Min(100, text.Length)]}",
                cancellationToken);
        }

        // AI auto-reply (Phase 12): only ever considered for an actual inbound customer message
        // (e.g. an agent logging what a customer said over the phone on the Manual channel) —
        // never for an agent's own outbound reply, which would be an infinite-loop risk (PRD §71
        // security focus: "infinite reply loops"). AiAutoReplyService itself re-checks the
        // conversation's mode and the tenant's settings, so this call is cheap/no-op whenever
        // auto-reply isn't actually configured.
        if (direction == MessageDirection.Inbound && senderType == MessageSenderType.Customer)
        {
            await automationRules.EvaluateAsync(tenantContext.TenantId, conversation.Id, text, cancellationToken);
            await autoReply.EvaluateAsync(tenantContext.TenantId, conversation.Id, cancellationToken);
        }

        return message;
    }

    /// <summary>
    /// Routes an outbound message through the channel's provider adapter, if one is registered
    /// (Phase 6+; nothing is registered before Phase 7). Manual/WebsiteChat have none, so this
    /// preserves their exact prior behavior — mark sent immediately, no provider round-trip.
    /// </summary>
    private async Task RouteOutboundAsync(Conversation conversation, Message message, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.SingleOrDefaultAsync(a => a.Id == conversation.ChannelAccountId, cancellationToken);
        if (account is null)
        {
            message.MarkFailed();
            return;
        }

        var recipientExternalId = await db.ContactIdentifiers
            .Where(i => i.ContactId == conversation.ContactId && i.ChannelType == account.Type)
            .Select(i => i.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var sendResult = recipientExternalId is null
            ? null
            : await channelSend.TrySendAsync(account, recipientExternalId, text, cancellationToken);

        if (sendResult is null)
        {
            // No adapter registered for this channel (Manual/WebsiteChat) — same behavior as
            // before Phase 6 existed.
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

    public async Task<bool> AssignAsync(Guid conversationId, Guid assigneeUserId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var assigneeName = await db.UserProfiles
            .Where(u => u.Id == assigneeUserId)
            .Select(u => u.DisplayName)
            .SingleOrDefaultAsync(cancellationToken) ?? "Unknown";

        conversation.AssignTo(assigneeUserId, now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.assigned", nameof(Conversation), conversationId);
        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyAssignmentUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            conversation.AssignedUserId,
            assigneeName,
            cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            null, // status
            null, // priority
            null, // aiMode
            null, // lastMessageAt
            null, // lastMessagePreview
            conversation.AssignedUserId,
            cancellationToken);

        return true;
    }

    public async Task<bool> UnassignAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        conversation.Unassign(now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.unassigned", nameof(Conversation), conversationId);
        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyAssignmentUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            conversation.AssignedUserId,
            "Unassigned",
            cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            null, // status
            null, // priority
            null, // aiMode
            null, // lastMessageAt
            null, // lastMessagePreview
            conversation.AssignedUserId,
            cancellationToken);

        return true;
    }

    public async Task<bool> ChangeStatusAsync(Guid conversationId, ConversationStatus status, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        conversation.ChangeStatus(status, now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.status_changed", nameof(Conversation), conversationId);
        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            conversation.Status,
            null, // priority
            null, // aiMode
            null, // lastMessageAt
            null, // lastMessagePreview
            conversation.AssignedUserId,
            cancellationToken);

        return true;
    }

    public async Task<bool> SetPriorityAsync(Guid conversationId, ConversationPriority priority, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        conversation.SetPriority(priority, now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.priority_changed", nameof(Conversation), conversationId);
        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            null, // status
            conversation.Priority,
            null, // aiMode
            null, // lastMessageAt
            null, // lastMessagePreview
            conversation.AssignedUserId,
            cancellationToken);

        // High priority alert if priority was set to High or Urgent
        if (priority == ConversationPriority.High || priority == ConversationPriority.Urgent)
        {
            await realtime.NotifyHighPriorityAlertAsync(
                tenantContext.TenantId,
                conversation.Id,
                "Priority Escalated",
                $"Conversation priority changed to {priority}",
                cancellationToken);
        }

        return true;
    }

    public async Task<bool> SetAiModeAsync(Guid conversationId, ConversationAiMode aiMode, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        conversation.SetAiMode(aiMode, now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.ai_mode_changed", nameof(Conversation), conversationId,
            new { aiMode = aiMode.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantContext.TenantId,
            conversation.Id,
            null, // status
            null, // priority
            conversation.AiMode,
            null, // lastMessageAt
            null, // lastMessagePreview
            conversation.AssignedUserId,
            cancellationToken);

        return true;
    }

    public async Task<NoteSummary?> AddNoteAsync(Guid conversationId, string text, CancellationToken cancellationToken)
    {
        var exists = await db.Conversations.AnyAsync(c => c.Id == conversationId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var note = InternalNote.Create(tenantContext.TenantId, conversationId, tenantContext.UserId, text, now);
        db.InternalNotes.Add(note);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "note.added", nameof(InternalNote), note.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new NoteSummary(note.Id, note.AuthorUserId, note.Text, note.CreatedAt);
    }

    public Task<List<NoteSummary>> ListNotesAsync(Guid conversationId, CancellationToken cancellationToken)
        => db.InternalNotes
            .Where(n => n.ConversationId == conversationId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteSummary(n.Id, n.AuthorUserId, n.Text, n.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> AddTagAsync(Guid conversationId, string tagName, CancellationToken cancellationToken)
    {
        var exists = await db.Conversations.AnyAsync(c => c.Id == conversationId, cancellationToken);
        if (!exists)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var normalizedName = tagName.Trim();
        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Name == normalizedName, cancellationToken);
        if (tag is null)
        {
            tag = Tag.Create(tenantContext.TenantId, normalizedName, now);
            db.Tags.Add(tag);
        }

        var alreadyTagged = await db.ConversationTags
            .AnyAsync(ct => ct.ConversationId == conversationId && ct.TagId == tag.Id, cancellationToken);
        if (!alreadyTagged)
        {
            db.ConversationTags.Add(ConversationTag.Create(tenantContext.TenantId, conversationId, tag.Id, now));
            audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.tagged", nameof(Conversation), conversationId,
                new { tag = normalizedName });
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> RemoveTagAsync(Guid conversationId, Guid tagId, CancellationToken cancellationToken)
    {
        var link = await db.ConversationTags
            .SingleOrDefaultAsync(ct => ct.ConversationId == conversationId && ct.TagId == tagId, cancellationToken);
        if (link is null)
        {
            return false;
        }

        db.ConversationTags.Remove(link);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.untagged", nameof(Conversation), conversationId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ConversationDetail?> GetDetailAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await (
            from conv in db.Conversations
            where conv.Id == conversationId
            join contact in db.Contacts on conv.ContactId equals contact.Id
            join channel in db.ChannelAccounts on conv.ChannelAccountId equals channel.Id
            select new { Conversation = conv, contact.DisplayName, channel.Type }
        ).SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        var tags = await GetTagNamesAsync(conversationId, cancellationToken);
        var conversation = result.Conversation;
        return new ConversationDetail(
            conversation.Id, conversation.ContactId, result.DisplayName, conversation.ChannelAccountId, result.Type, conversation.Status, conversation.Priority,
            conversation.AssignedUserId, conversation.AiMode, conversation.LastMessageAt, conversation.CreatedAt, conversation.ClosedAt, tags);
    }

    public async Task<KeysetResult<ConversationSummary>> ListAsync(
        ConversationStatus? status, Guid? assignedUserId, string? search, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var decoded = KeysetCursor.Decode(cursor);

        var query =
            from conv in db.Conversations
            join contact in db.Contacts on conv.ContactId equals contact.Id
            join channel in db.ChannelAccounts on conv.ChannelAccountId equals channel.Id
            select new { Conversation = conv, contact.DisplayName, channel.Type };

        if (status.HasValue)
        {
            query = query.Where(x => x.Conversation.Status == status.Value);
        }

        if (assignedUserId.HasValue)
        {
            query = query.Where(x => x.Conversation.AssignedUserId == assignedUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();

            // Translated to SQL by EF Core, never executed as CLR code — see the identical
            // pattern (and the reason for the pragma) in ContactService.ListAsync.
#pragma warning disable CA1304, CA1311, CA1862
            query = query.Where(x => x.DisplayName.ToLower().Contains(normalizedSearch));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (decoded is { } cursorValue)
        {
            query = query.Where(x =>
                x.Conversation.LastMessageAt < cursorValue.Timestamp ||
                (x.Conversation.LastMessageAt == cursorValue.Timestamp && x.Conversation.Id.CompareTo(cursorValue.Id) < 0));
        }

        var page = await query
            .OrderByDescending(x => x.Conversation.LastMessageAt).ThenByDescending(x => x.Conversation.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToList();
        var tagsByConversation = await GetTagNamesForConversationsAsync(items.Select(x => x.Conversation.Id), cancellationToken);

        var summaries = items.Select(x => new ConversationSummary(
            x.Conversation.Id, x.Conversation.ContactId, x.DisplayName, x.Conversation.ChannelAccountId, x.Type, x.Conversation.Status, x.Conversation.Priority,
            x.Conversation.AssignedUserId, x.Conversation.LastMessageAt, x.Conversation.LastMessagePreview,
            tagsByConversation.GetValueOrDefault(x.Conversation.Id, []))).ToList();

        var nextCursor = hasMore && items.Count > 0
            ? KeysetCursor.Encode(items[^1].Conversation.LastMessageAt, items[^1].Conversation.Id)
            : null;

        return new KeysetResult<ConversationSummary>(summaries, nextCursor);
    }

    public async Task<KeysetResult<MessageSummary>> ListMessagesAsync(
        Guid conversationId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var decoded = KeysetCursor.Decode(cursor);

        var query = db.Messages.Where(m => m.ConversationId == conversationId);
        if (decoded is { } cursorValue)
        {
            query = query.Where(m =>
                m.CreatedAt < cursorValue.Timestamp ||
                (m.CreatedAt == cursorValue.Timestamp && m.Id.CompareTo(cursorValue.Id) < 0));
        }

        var page = await query
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(pageSize + 1)
            .Select(m => new MessageSummary(m.Id, m.Direction, m.SenderType, m.ContentType, m.Text, m.CreatedAt, m.DeliveryStatus))
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        var items = page.Take(pageSize).ToList();
        var nextCursor = hasMore && items.Count > 0 ? KeysetCursor.Encode(items[^1].CreatedAt, items[^1].Id) : null;

        return new KeysetResult<MessageSummary>(items, nextCursor);
    }

    private async Task<bool> MutateAsync(Guid conversationId, string action, Action<Conversation> mutate, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        mutate(conversation);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, action, nameof(Conversation), conversationId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<List<TagRef>> GetTagNamesAsync(Guid conversationId, CancellationToken cancellationToken)
        => await (
            from ct in db.ConversationTags
            where ct.ConversationId == conversationId
            join tag in db.Tags on ct.TagId equals tag.Id
            select new TagRef(tag.Id, tag.Name)
        ).ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, List<TagRef>>> GetTagNamesForConversationsAsync(
        IEnumerable<Guid> conversationIds, CancellationToken cancellationToken)
    {
        var ids = conversationIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
            from ct in db.ConversationTags
            where ids.Contains(ct.ConversationId)
            join tag in db.Tags on ct.TagId equals tag.Id
            select new { ct.ConversationId, Tag = new TagRef(tag.Id, tag.Name) }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ConversationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Tag).ToList());
    }
}
