using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Widget;
using Omnichannel.Infrastructure.Identity;
using Omnichannel.Infrastructure.Security;

namespace Omnichannel.Infrastructure.Widget;

/// <summary>
/// Issues the short-lived session token used by the anonymous website-chat widget. Uses the same
/// signing key ring + issuer as agent JWTs (one issuer, one key ring, both audiences valid only
/// against the matching handler), but a distinct audience ("widget") and a separate claim set, so:
///   - a widget token cannot call agent APIs (audience mismatch, and it lacks agent claims), and
///   - an agent token cannot drive the widget (it lacks visitor/session claims).
/// </summary>
public sealed class WidgetSessionTokenGenerator(
    IOptions<JwtOptions> jwt, IOptions<WidgetTokenOptions> widget, JwtSigningKeyCache signingKeyCache, IJwtSigningKeyStore signingKeyStore)
    : IWidgetSessionTokenGenerator
{
    private readonly JwtOptions _jwt = jwt.Value;
    private readonly WidgetTokenOptions _widget = widget.Value;

    public async Task<string> GenerateAsync(Guid tenantId, Guid visitorId, Guid sessionId, Guid conversationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expiresAt = now.AddMinutes(_widget.SessionLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, visitorId.ToString()),
            new(WidgetClaimNames.TenantId, tenantId.ToString()),
            new(WidgetClaimNames.VisitorId, visitorId.ToString()),
            new(WidgetClaimNames.SessionId, sessionId.ToString()),
            new(WidgetClaimNames.ConversationId, conversationId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var primaryKey = signingKeyCache.Primary ?? await signingKeyStore.GetPrimaryAsync(cancellationToken);
        var signingKey = new SymmetricSecurityKey(primaryKey.KeyBytes) { KeyId = primaryKey.Kid };
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _widget.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
