using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Security;

/// <summary>
/// A generic encrypted-at-rest secret scoped to one tenant, keyed by an arbitrary
/// <see cref="Purpose"/> string (e.g. "smtp.password", "ai.apikey") — the same shape
/// <see cref="Channels.ChannelCredential"/> already established for channel credentials,
/// generalized here because per-tenant SMTP passwords and AI provider API keys need the exact
/// same encrypted-at-rest treatment but aren't tied to a ChannelAccount. Never holds plaintext —
/// that boundary is enforced by <c>ITenantSecretStore</c>/<c>DataProtectionTenantSecretStore</c>,
/// never here (AGENTS.md: credentials never in source, logs, tests, or client bundles).
/// </summary>
public sealed class TenantSecret : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string EncryptedValue { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantSecret()
    {
    }

    public static TenantSecret Create(Guid tenantId, string purpose, string encryptedValue, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Purpose = purpose,
            EncryptedValue = encryptedValue,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Rotate(string encryptedValue, DateTimeOffset now)
    {
        EncryptedValue = encryptedValue;
        UpdatedAt = now;
    }
}
