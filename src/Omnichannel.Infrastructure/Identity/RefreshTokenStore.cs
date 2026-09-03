using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Persistence;

namespace Omnichannel.Infrastructure.Identity;

public sealed class RefreshTokenStore(AppDbContext db, IOptions<JwtOptions> jwtOptions) : IRefreshTokenStore
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public async Task<string> IssueAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var raw = GenerateRawToken();
        var entity = RefreshToken.Create(userId, Hash(raw), now, TimeSpan.FromDays(_options.RefreshTokenLifetimeDays));
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return raw;
    }

    public async Task<RefreshTokenRecord?> FindActiveAsync(string rawToken, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hash = Hash(rawToken);
        var entity = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (entity is null || !entity.IsActive(now))
        {
            return null;
        }

        return new RefreshTokenRecord(entity.Id, entity.UserId, entity.ExpiresAt, entity.RevokedAt, entity.ReplacedByTokenId);
    }

    public async Task<string> RotateAsync(Guid tokenId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var raw = GenerateRawToken();
        var newEntity = RefreshToken.Create(userId, Hash(raw), now, TimeSpan.FromDays(_options.RefreshTokenLifetimeDays));

        var existing = await db.RefreshTokens.SingleAsync(t => t.Id == tokenId, cancellationToken);
        existing.Revoke(now, newEntity.Id);

        db.RefreshTokens.Add(newEntity);
        await db.SaveChangesAsync(cancellationToken);
        return raw;
    }

    public async Task RevokeAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.Id == tokenId, cancellationToken);
        if (existing is not null && existing.IsActive(now))
        {
            existing.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active.Where(t => t.IsActive(now)))
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string rawToken)
        => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
