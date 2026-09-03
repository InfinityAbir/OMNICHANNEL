using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Channels;

/// <summary>
/// Provider credentials (API token, app secret, etc.) for one <see cref="ChannelAccount"/>.
/// Stores only an already-encrypted payload — this entity never sees or holds plaintext; that
/// boundary is enforced by <c>IChannelCredentialStore</c> (Application) /
/// <c>DataProtectionChannelCredentialStore</c> (Infrastructure), never here (AGENTS.md: never
/// keep provider credentials in source code, logs, tests, or client bundles — encrypted at rest,
/// decrypted only server-side for the duration of an outbound send call).
/// </summary>
public sealed class ChannelCredential : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ChannelAccountId { get; private set; }
    public string EncryptedSecret { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ChannelCredential()
    {
    }

    public static ChannelCredential Create(Guid tenantId, Guid channelAccountId, string encryptedSecret, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelAccountId = channelAccountId,
            EncryptedSecret = encryptedSecret,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Rotate(string encryptedSecret, DateTimeOffset now)
    {
        EncryptedSecret = encryptedSecret;
        UpdatedAt = now;
    }
}
