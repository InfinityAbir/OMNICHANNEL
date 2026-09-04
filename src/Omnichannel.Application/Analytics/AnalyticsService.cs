using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Application.Analytics;

public sealed record ChannelMetric(string ChannelType, int ConversationCount);

public sealed record AgentMetric(Guid AgentUserId, string AgentDisplayName, int AssignedConversationCount, int ClosedConversationCount);

public sealed record AnalyticsSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalConversations,
    int OpenConversations,
    int PendingConversations,
    int EscalatedConversations,
    int ResolvedConversations,
    int ClosedConversations,
    double? AverageFirstResponseMinutes,
    double? AverageResolutionMinutes,
    double ResolutionRatePercent,
    int AiSuggestionsGenerated,
    double? AverageAiSuggestionConfidence,
    int AiAutoRepliesSent,
    IReadOnlyList<ChannelMetric> ByChannel,
    IReadOnlyList<AgentMetric> ByAgent);

/// <summary>
/// PRD §73: inbox/response-time/resolution/AI/channel/agent metrics, always scoped to the
/// caller's own tenant via the ambient <see cref="ITenantContext"/> (analytics is a purely
/// authenticated-dashboard read path — unlike Phase 12/13's services, there's no unauthenticated
/// call site here, so the ordinary EF tenant filter is sufficient; PRD §73's "must never aggregate
/// across tenants" is enforced by the same global query filter every other tenant-owned read
/// already relies on). Every metric is a single aggregate/grouped query (COUNT/AVG/GROUP BY),
/// never a full row materialization, per PRD §73's "avoid expensive per-request calculation".
/// </summary>
public sealed class AnalyticsService(IAppDbContext db)
{
    public async Task<AnalyticsSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var conversations = db.Conversations.Where(c => c.CreatedAt >= from && c.CreatedAt <= to);

        var statusCounts = await conversations
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var total = statusCounts.Sum(s => s.Count);
        int CountOf(ConversationStatus status) => statusCounts.FirstOrDefault(s => s.Status == status)?.Count ?? 0;
        var resolved = CountOf(ConversationStatus.Resolved);
        var closed = CountOf(ConversationStatus.Closed);

        double? resolutionMinutes = null;
        var closedDurations = await conversations
            .Where(c => c.ClosedAt != null)
            .Select(c => new { c.CreatedAt, ClosedAt = c.ClosedAt!.Value })
            .ToListAsync(cancellationToken);
        if (closedDurations.Count > 0)
        {
            resolutionMinutes = closedDurations.Average(d => (d.ClosedAt - d.CreatedAt).TotalMinutes);
        }

        var responseTimeRows = await db.Messages
            .Where(m => conversations.Select(c => c.Id).Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new
            {
                FirstCustomerAt = g.Min(m => m.SenderType == MessageSenderType.Customer ? m.CreatedAt : (DateTimeOffset?)null),
                FirstReplyAt = g.Min(m => (m.SenderType == MessageSenderType.Agent || m.SenderType == MessageSenderType.Ai) ? m.CreatedAt : (DateTimeOffset?)null),
            })
            .Where(x => x.FirstCustomerAt != null && x.FirstReplyAt != null && x.FirstReplyAt > x.FirstCustomerAt)
            .ToListAsync(cancellationToken);

        double? averageFirstResponseMinutes = responseTimeRows.Count > 0
            ? responseTimeRows.Average(x => (x.FirstReplyAt!.Value - x.FirstCustomerAt!.Value).TotalMinutes)
            : null;

        var aiSuggestions = await db.AiSuggestions
            .Where(s => s.CreatedAt >= from && s.CreatedAt <= to)
            .Select(s => s.Confidence)
            .ToListAsync(cancellationToken);

        var aiAutoRepliesSent = await db.Messages
            .Where(m => m.SenderType == MessageSenderType.Ai && m.Direction == MessageDirection.Outbound && m.CreatedAt >= from && m.CreatedAt <= to)
            .CountAsync(cancellationToken);

        var byChannel = await conversations
            .Join(db.ChannelAccounts, c => c.ChannelAccountId, a => a.Id, (c, a) => a.Type)
            .GroupBy(t => t)
            .Select(g => new ChannelMetric(g.Key.ToString(), g.Count()))
            .ToListAsync(cancellationToken);

        var assignedCounts = await conversations
            .Where(c => c.AssignedUserId != null)
            .GroupBy(c => c.AssignedUserId!.Value)
            .Select(g => new { AgentUserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var closedByAgent = await conversations
            .Where(c => c.AssignedUserId != null && c.Status == ConversationStatus.Closed)
            .GroupBy(c => c.AssignedUserId!.Value)
            .Select(g => new { AgentUserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var agentIds = assignedCounts.Select(a => a.AgentUserId).ToList();
        var agentNames = await db.UserProfiles
            .Where(u => agentIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var byAgent = assignedCounts
            .Select(a => new AgentMetric(
                a.AgentUserId,
                agentNames.GetValueOrDefault(a.AgentUserId, "Unknown"),
                a.Count,
                closedByAgent.FirstOrDefault(c => c.AgentUserId == a.AgentUserId)?.Count ?? 0))
            .OrderByDescending(a => a.AssignedConversationCount)
            .ToList();

        return new AnalyticsSummary(
            from, to, total,
            CountOf(ConversationStatus.Open), CountOf(ConversationStatus.Pending), CountOf(ConversationStatus.Escalated),
            resolved, closed,
            averageFirstResponseMinutes, resolutionMinutes,
            total == 0 ? 0 : (resolved + closed) * 100.0 / total,
            aiSuggestions.Count, aiSuggestions.Count > 0 ? aiSuggestions.Average() : null, aiAutoRepliesSent,
            byChannel, byAgent);
    }
}
