using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Security;

namespace Omnichannel.Infrastructure.Identity;

public sealed class JwtAccessTokenGenerator(IOptions<JwtOptions> options, JwtSigningKeyCache signingKeyCache, IJwtSigningKeyStore signingKeyStore)
    : IAccessTokenGenerator
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AccessTokenResult> GenerateAsync(
        Guid userId, string email, Guid tenantId, IReadOnlyCollection<string> permissions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        // Reads the same cache validation uses (see JwtSigningKeyCache's doc comment) rather than
        // the store directly — the cache is not yet warm only in the brief window before the
        // startup warm-up in Program.cs completes, so the store is a safety-net fallback, not the
        // normal path.
        var primaryKey = signingKeyCache.Primary ?? await signingKeyStore.GetPrimaryAsync(cancellationToken);
        var signingKey = new SymmetricSecurityKey(primaryKey.KeyBytes) { KeyId = primaryKey.Kid };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
