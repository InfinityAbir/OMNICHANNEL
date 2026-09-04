namespace Omnichannel.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Legacy/optional (ADR-0029): only used once, to seed the very first row in the
    /// database-backed signing key ring (<c>IJwtSigningKeyStore</c>) if the ring is empty and this
    /// is set — preserves already-issued tokens across the upgrade to key-ring rotation. A brand
    /// new deployment needs no value here at all; the store generates a random key itself.</summary>
    public string? SigningKey { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    public int RefreshTokenLifetimeDays { get; init; } = 30;
}
