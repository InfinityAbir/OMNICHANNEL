namespace Omnichannel.Application.Abstractions;

public sealed record RefreshTokenRecord(
    Guid Id,
    Guid UserId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    Guid? ReplacedByTokenId);

/// <summary>
/// Persists rotating refresh tokens by hash only — the raw token is never stored (PRD §13,
/// AGENTS.md: never log/store tokens in recoverable form).
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Issues a new refresh token, returns the raw token to hand to the client.</summary>
    Task<string> IssueAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Looks up an active (not expired, not revoked) token by its raw value.</summary>
    Task<RefreshTokenRecord?> FindActiveAsync(string rawToken, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes the given token and issues+returns a new one in its place (rotation).</summary>
    Task<string> RotateAsync(Guid tokenId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes a token without issuing a replacement (logout).</summary>
    Task RevokeAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes every active token for a user (e.g. on detected reuse of a rotated token).</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
