using Microsoft.IdentityModel.Tokens;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Security;

/// <summary>
/// In-memory, singleton cache of the JWT signing key ring, kept warm by
/// <see cref="JwtSigningKeyRefreshService"/> from the real source of truth
/// (<c>IJwtSigningKeyStore</c>) on a short interval — both the currently-valid key set (read
/// synchronously by <c>JwtBearerOptions.TokenValidationParameters.IssuerSigningKeyResolver</c> on
/// every authenticated request, which has no DI/async access) and the current primary (read by
/// <c>JwtAccessTokenGenerator</c>/<c>WidgetSessionTokenGenerator</c> when issuing new tokens).
///
/// Signing and validation deliberately share this ONE cache rather than signing reading the
/// database live: an always-fresh signing read could otherwise race ahead of the validation
/// cache — a token signed with a brand-new primary immediately after a rotation could fail its
/// own very next validation for as long as the cache stayed stale, even within a single process.
/// Reading both from the same snapshot makes that impossible by construction.
/// </summary>
public sealed class JwtSigningKeyCache
{
    private volatile State _state = new([], null);

    public IReadOnlyList<SecurityKey> CurrentKeys => _state.ValidKeys;

    public JwtSigningKeyMaterial? Primary => _state.Primary;

    public void Update(IReadOnlyList<SecurityKey> validKeys, JwtSigningKeyMaterial primary) => _state = new State(validKeys, primary);

    private sealed record State(IReadOnlyList<SecurityKey> ValidKeys, JwtSigningKeyMaterial? Primary);
}
