namespace Omnichannel.Application.Abstractions;

public sealed record JwtSigningKeyMaterial(string Kid, byte[] KeyBytes);

public sealed record JwtKeyRotationResult(string NewPrimaryKid, string RetiredKid, DateTimeOffset RetiredKeyValidUntil);

/// <summary>
/// The app-wide JWT signing key ring (ADR-0029) — agent and widget tokens share one issuer/key
/// ring across every tenant, so this is a platform concern, never scoped to a tenant.
/// </summary>
public interface IJwtSigningKeyStore
{
    /// <summary>The key currently used to sign new tokens. Bootstraps one on first-ever call
    /// (seeded from the legacy <c>Jwt:SigningKey</c> config value if present, otherwise a fresh
    /// random key) so a first deploy never fails for lack of a pre-existing key.</summary>
    Task<JwtSigningKeyMaterial> GetPrimaryAsync(CancellationToken cancellationToken);

    /// <summary>Every key still valid for validating an incoming token's signature — the current
    /// primary plus any not-yet-fully-retired keys from a recent rotation's overlap window.</summary>
    Task<IReadOnlyList<JwtSigningKeyMaterial>> GetValidKeysAsync(CancellationToken cancellationToken);

    /// <summary>Creates a new primary signing key and retires the previous one, still valid for
    /// validation until <paramref name="overlapWindow"/> from now — long enough to cover every
    /// access token already issued under it (they expire well within a typical access-token
    /// lifetime), so an in-progress session isn't logged out the instant this runs.</summary>
    Task<JwtKeyRotationResult> RotateAsync(TimeSpan overlapWindow, CancellationToken cancellationToken);
}
