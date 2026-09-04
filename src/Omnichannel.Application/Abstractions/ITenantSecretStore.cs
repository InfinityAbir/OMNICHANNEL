namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Encrypts/decrypts an arbitrary per-tenant secret at rest, keyed by a purpose string (e.g.
/// "smtp.password", "ai.apikey"). Generalizes <c>IChannelCredentialStore</c>'s pattern for
/// secrets that aren't tied to a specific ChannelAccount. Takes an explicit <c>tenantId</c>
/// rather than <c>ITenantContext</c> — email sending in particular happens from unauthenticated
/// contexts (registration, password reset), the same reason <c>AiAutoReplyService</c> and
/// <c>AutomationRuleService</c> take an explicit tenantId (ADR-0016/0022).
/// </summary>
public interface ITenantSecretStore
{
    Task SetAsync(Guid tenantId, string purpose, string plaintextSecret, CancellationToken cancellationToken);

    Task<string?> GetAsync(Guid tenantId, string purpose, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid tenantId, string purpose, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, string purpose, CancellationToken cancellationToken);
}
