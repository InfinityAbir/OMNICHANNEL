namespace Omnichannel.Domain.Security;

/// <summary>
/// One JWT HMAC signing key in the app-wide key ring — not tenant-owned (no
/// <c>ITenantOwned</c>), since agent and widget JWTs share one issuer/key ring across every
/// tenant. Exactly one row has <see cref="IsPrimary"/> true at any time: the key currently used
/// to SIGN new tokens. Rotating creates a new primary and <see cref="Retire"/>s the old one with a
/// future <see cref="RetiredAt"/> (now + an overlap window) rather than retiring it immediately —
/// a token already signed with the old key must keep validating until it naturally expires
/// (access tokens live 15 minutes by default), so an overlap window avoids logging out every
/// active session the instant a rotation happens. Encrypted at rest via the same Data Protection
/// mechanism as <c>TenantSecret</c>/<c>ChannelCredential</c> — see
/// <c>DataProtectionJwtSigningKeyStore</c>.
/// </summary>
public sealed class JwtSigningKey
{
    public Guid Id { get; private set; }
    public string EncryptedKeyMaterial { get; private set; } = string.Empty;
    public bool IsPrimary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null while still valid for both signing (if primary) or validation-only. Once set,
    /// the key stays valid for validation only until this timestamp, then is no longer offered
    /// during signature validation.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    private JwtSigningKey()
    {
    }

    public static JwtSigningKey CreatePrimary(string encryptedKeyMaterial, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            EncryptedKeyMaterial = encryptedKeyMaterial,
            IsPrimary = true,
            CreatedAt = now,
            RetiredAt = null,
        };

    /// <summary>Demotes this key from primary (it stops signing new tokens) and schedules it to
    /// stop validating tokens at <paramref name="retiredAt"/> — a future timestamp, not now.</summary>
    public void Retire(DateTimeOffset retiredAt)
    {
        IsPrimary = false;
        RetiredAt = retiredAt;
    }

    public bool IsValidForValidation(DateTimeOffset now) => RetiredAt is null || RetiredAt > now;
}
