using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Security;

/// <summary>
/// Keeps <see cref="JwtSigningKeyCache"/> in sync with the real key ring (ADR-0029) so JWT
/// signature validation never blocks on a per-request database read. Opens its own DI scope per
/// tick (it's a singleton-lifetime hosted service; <c>IJwtSigningKeyStore</c> is scoped).
/// </summary>
public sealed partial class JwtSigningKeyRefreshService(
    IServiceScopeFactory scopeFactory, JwtSigningKeyCache cache, ILogger<JwtSigningKeyRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IJwtSigningKeyStore>();

            // Both reads must reflect the same rotation state — see JwtSigningKeyCache's own
            // doc comment for why signing and validation share one cache snapshot rather than
            // each reading the store independently.
            var validKeys = await store.GetValidKeysAsync(cancellationToken);
            var primary = await store.GetPrimaryAsync(cancellationToken);
            cache.Update(validKeys.Select(k => (SecurityKey)new SymmetricSecurityKey(k.KeyBytes) { KeyId = k.Kid }).ToList(), primary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a transient DB hiccup crash the host — the cache just keeps serving
            // whatever it last had (still-valid keys don't change every refresh tick anyway).
            LogRefreshFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to refresh the JWT signing key cache.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
