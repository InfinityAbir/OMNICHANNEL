using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Application.Common;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Conversations;

public sealed class ConversationService(IAppDbContext db, AuditService audit, ITenantContext tenantContext, TimeProvider timeProvider)
{
    private const int MaxPageSize = 100;

    public async Task<Conversation> CreateManualAsync(Guid contactId, string? initialMessageText, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var manualChannel = await db.ChannelAccounts.SingleAsync(c => c.Type == ChannelType.Manual, cancellationToken);

        var conversation = Conversation.Create(tenantContext.TenantId, contactId, manualChannel.Id, now);
        db.Conversations.Add(conversation);

        if (!string.IsNullOrWhiteSpace(initialMessageText))
        {
            var message = Message.CreateInbound(
                tenantContext.TenantId, conversation.Id, manualChannel.Id, MessageSenderType.Customer, initialMessageText, now);
            db.Messages.Add(message);
        }

        audit.Record(tenantContext.TenantId, tenantContext.UserId, "conversation.created", nameof(Conversation), conversation.Id);
        await db.SaveChangesAsync(cancellationToken);
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
            // No real channel adapter behind "Manual" — treat as delivered immediately rather
            // than leaving it permanently "Queued".
            message.MarkSent(now);
        }

        db.Messages.Add(message);
        conversation.TouchLastMessage(now);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "message.sent", nameof(Message), message.Id,
            new { direction = direction.ToString() });

        await db.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<bool> AssignAsync(Guid conversationId, Guid assigneeUserId, CancellationToken cancellationToken)
        => await MutateAsync(conversationId, "conversation.assigned", c => c.AssignTo(assigneeUserId, timeProvider.GetUtcNow()), cancellationToken);

    public async Task<bool> UnassignAsync(Guid conversationId, CancellationToken cancellationToken)
        => await MutateAsync(conversationId, "conversation.unassigned", c => c.Unassign(timeProvider.GetUtcNow()), cancellationToken);

    public async Task<bool> ChangeStatusAsync(Guid conversationId, ConversationStatus status, CancellationToken cancellationToken)
        => await MutateAsync(conversationId, "conversation.status_changed", c => c.ChangeStatus(status, timeProvider.GetUtcNow()), cancellationToken);

    public async Task<bool> SetPriorityAsync(Guid conversationId, ConversationPriority priority, CancellationToken cancellationToken)
        => await MutateAsync(conversationId, "conversation.priority_changed", c => c.SetPriority(priority, timeProvider.GetUtcNow()), cancellationToken);

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
            select new { Conversation = conv, contact.DisplayName }
        ).SingleOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        var tags = await GetTagNamesAsync(conversationId, cancellationToken);
        var conversation = result.Conversation;
        return new ConversationDetail(
            conversation.Id, conversation.ContactId, result.DisplayName, conversation.ChannelAccountId, conversation.Status, conversation.Priority,
            conversation.AssignedUserId, conversation.AiMode, conversation.LastMessageAt, conversation.CreatedAt, conversation.ClosedAt, tags);
    }

    public async Task<KeysetResult<ConversationSummary>> ListAsync(
        ConversationStatus? status, Guid? assignedUserId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var decoded = KeysetCursor.Decode(cursor);

        var query =
            from conv in db.Conversations
            join contact in db.Contacts on conv.ContactId equals contact.Id
            select new { Conversation = conv, contact.DisplayName };

        if (status.HasValue)
        {
            query = query.Where(x => x.Conversation.Status == status.Value);
        }

        if (assignedUserId.HasValue)
        {
            query = query.Where(x => x.Conversation.AssignedUserId == assignedUserId.Value);
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
            x.Conversation.Id, x.Conversation.ContactId, x.DisplayName, x.Conversation.ChannelAccountId, x.Conversation.Status, x.Conversation.Priority,
            x.Conversation.AssignedUserId, x.Conversation.LastMessageAt,
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

    private async Task<List<string>> GetTagNamesAsync(Guid conversationId, CancellationToken cancellationToken)
        => await (
            from ct in db.ConversationTags
            where ct.ConversationId == conversationId
            join tag in db.Tags on ct.TagId equals tag.Id
            select tag.Name
        ).ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, List<string>>> GetTagNamesForConversationsAsync(
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
            select new { ct.ConversationId, tag.Name }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ConversationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Name).ToList());
    }
}
