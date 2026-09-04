using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Automation;
using Omnichannel.Domain.Conversations;
using Omnichannel.Domain.Notifications;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.Application.Automation;

/// <summary>
/// CRUD for <see cref="AutomationRule"/> (ambient tenant context — admin-facing management) plus
/// <see cref="EvaluateAsync"/>, the trigger-time evaluation invoked from the same three inbound-
/// message paths as <see cref="Ai.AiAutoReplyService"/> and for the same reason: it must be
/// correct whether called from an authenticated agent request, an authenticated-but-not-tenant
/// widget request, or a fully unauthenticated provider webhook, so it takes an explicit
/// <c>tenantId</c> and queries via <c>IgnoreQueryFilters()</c> (ADR-0016's documented exception,
/// extended here).
/// </summary>
public sealed class AutomationRuleService(
    IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider, AuditService audit,
    IEmailSender emailSender, IRealtimeNotifier realtime)
{
    public Task<List<AutomationRule>> ListAsync(CancellationToken cancellationToken)
        => db.AutomationRules.OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public async Task<AutomationRule> CreateAsync(
        string name, string keyword, string? applyTagName, ConversationPriority? setPriority, bool escalate, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rule = AutomationRule.Create(tenantContext.TenantId, name, keyword, applyTagName, setPriority, escalate, now);
        db.AutomationRules.Add(rule);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "automation.rule_created", nameof(AutomationRule), rule.Id, new { keyword });
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> SetEnabledAsync(Guid ruleId, bool enabled, CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        rule.SetEnabled(enabled, timeProvider.GetUtcNow());
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "automation.rule_toggled", nameof(AutomationRule), rule.Id, new { enabled });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        db.AutomationRules.Remove(rule);
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "automation.rule_deleted", nameof(AutomationRule), rule.Id);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task EvaluateAsync(Guid tenantId, Guid conversationId, string messageText, CancellationToken cancellationToken)
    {
        var rules = await db.AutomationRules.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Enabled)
            .ToListAsync(cancellationToken);

        var matched = rules.Where(r => r.Matches(messageText)).ToList();
        if (matched.Count == 0)
        {
            return;
        }

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        string? escalatingRuleName = null;

        foreach (var rule in matched)
        {
            if (rule.ApplyTagName is not null)
            {
                await ApplyTagAsync(tenantId, conversationId, rule.ApplyTagName, now, cancellationToken);
            }

            if (rule.SetPriority is not null)
            {
                conversation.SetPriority(rule.SetPriority.Value, now);
            }

            if (rule.Escalate)
            {
                escalatingRuleName ??= rule.Name;
            }

            audit.Record(tenantId, null, "automation.rule_matched", nameof(AutomationRule), rule.Id,
                new { conversationId, ruleName = rule.Name });
        }

        if (escalatingRuleName is not null && conversation.Status != ConversationStatus.Escalated)
        {
            conversation.ChangeStatus(ConversationStatus.Escalated, now);
            await NotifyOwnersAsync(tenantId, conversation, escalatingRuleName, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        await realtime.NotifyConversationUpdateAsync(
            tenantId, conversation.Id, conversation.Status, conversation.Priority, conversation.AiMode,
            conversation.LastMessageAt, conversation.LastMessagePreview, conversation.AssignedUserId, cancellationToken);
    }

    private async Task ApplyTagAsync(Guid tenantId, Guid conversationId, string tagName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalized = tagName.Trim();
        var tag = await db.Tags.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.TenantId == tenantId && t.Name == normalized, cancellationToken);
        if (tag is null)
        {
            tag = Tag.Create(tenantId, normalized, now);
            db.Tags.Add(tag);
        }

        var alreadyTagged = await db.ConversationTags.IgnoreQueryFilters()
            .AnyAsync(ct => ct.ConversationId == conversationId && ct.TagId == tag.Id, cancellationToken);
        if (!alreadyTagged)
        {
            db.ConversationTags.Add(ConversationTag.Create(tenantId, conversationId, tag.Id, now));
        }
    }

    /// <summary>Notifies every Owner/Admin member of the tenant — both in-app (<see cref="Notification"/>) and by email, reusing the existing SMTP infrastructure. A small business realistically has 1-3 owners/admins, so no pagination/batching is needed here.</summary>
    private async Task NotifyOwnersAsync(Guid tenantId, Conversation conversation, string ruleName, CancellationToken cancellationToken)
    {
        var recipients = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active)
            .Join(db.Roles, m => m.RoleId, r => r.Id, (m, r) => new { m.UserId, r.SystemRole })
            .Where(x => x.SystemRole == SystemRole.Owner || x.SystemRole == SystemRole.Admin)
            .Join(db.UserProfiles, x => x.UserId, u => u.Id, (x, u) => new { u.Id, u.Email, u.DisplayName })
            .ToListAsync(cancellationToken);

        var tenantName = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken) ?? "your business";
        var now = timeProvider.GetUtcNow();

        foreach (var recipient in recipients)
        {
            var notification = Notification.Create(
                tenantId, recipient.Id, "conversation.escalated", "Conversation escalated",
                $"Automation rule \"{ruleName}\" escalated a conversation.", conversation.Id, now);
            db.Notifications.Add(notification);

            await emailSender.SendConversationEscalatedAsync(
                tenantId, recipient.Email, recipient.DisplayName, tenantName, conversation.Id, ruleName, cancellationToken);
        }
    }
}
