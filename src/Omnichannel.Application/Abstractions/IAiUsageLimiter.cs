namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Configurable daily usage cap per tenant (docs/ai.md: "cost tracking and configurable usage
/// limits... with safe fallback to human handling when limits are reached"). Deliberately just a
/// daily cap for now — per-conversation/monthly limits are real future work, not built ahead of
/// a concrete need for them.
/// </summary>
public interface IAiUsageLimiter
{
    /// <summary>Returns true and counts this call against the tenant's daily limit if capacity remains; false if the limit is already reached (nothing is consumed).</summary>
    Task<bool> TryConsumeAsync(Guid tenantId, CancellationToken cancellationToken);
}
