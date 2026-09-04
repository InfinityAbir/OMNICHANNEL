using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Auth;
using Omnichannel.Infrastructure.Security;

namespace Omnichannel.SecurityTests;

/// <summary>
/// JWT signing key rotation with an overlap window (ADR-0029/PRD's "single-key, no overlap"
/// gap). No HTTP endpoint exists for this — rotation is a platform-wide operational action (see
/// Program.cs's `--rotate-jwt-key` command mode), so these tests drive
/// <see cref="IJwtSigningKeyStore"/> directly, the same way an operator's CLI invocation would.
/// </summary>
public class JwtKeyRotationTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Rotate_WithOverlapWindow_PreRotationTokenStillAuthenticates()
    {
        using var agent = factory.CreateClient();
        var preRotationToken = await RegisterAsync(agent);
        agent.UseBearer(preRotationToken);

        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative))).StatusCode);

        await RotateAndRefreshCacheAsync(TimeSpan.FromMinutes(5));

        // The token issued before rotation was signed with the now-retired key, but that key is
        // still inside its overlap window — must keep authenticating, not force every active
        // session to re-login the instant an operator rotates the key.
        var postRotationResponse = await agent.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, postRotationResponse.StatusCode);
    }

    [Fact]
    public async Task Rotate_NewLoginAfterRotation_UsesNewKeyAndAuthenticates()
    {
        using var agent = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        const string password = "Str0ng!Passw0rd";
        var registerResponse = await agent.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = email,
            Password = password,
            DisplayName = "Rotation Test",
            BusinessName = $"Rotation Test Biz {Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        await RotateAndRefreshCacheAsync(TimeSpan.FromMinutes(5));

        // A token issued AFTER rotation is signed with the new primary key — must authenticate
        // immediately, proving the signing side (not just validation) picked up the rotation
        // without a redeploy.
        var loginResponse = await agent.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = email, Password = password });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        agent.UseBearer(tokens!.AccessToken);

        var meResponse = await agent.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Rotate_WithZeroOverlap_PreRotationTokenIsImmediatelyRejected()
    {
        using var agent = factory.CreateClient();
        var preRotationToken = await RegisterAsync(agent);
        agent.UseBearer(preRotationToken);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative))).StatusCode);

        // A zero overlap window is the "no overlap" behavior the old single-key design always
        // had — every existing token stops validating the instant the key ring updates. Exercised
        // here as the boundary case proving the overlap window is what actually keeps old tokens
        // alive above, not some other mechanism accidentally making rotation a no-op.
        await RotateAndRefreshCacheAsync(TimeSpan.Zero);

        var response = await agent.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Rotation Test",
            BusinessName = $"Rotation Test Biz {Guid.NewGuid():N}",
        });
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        return tokens!.AccessToken;
    }

    private async Task RotateAndRefreshCacheAsync(TimeSpan overlapWindow)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJwtSigningKeyStore>();
        await store.RotateAsync(overlapWindow, CancellationToken.None);

        // Production keeps JwtSigningKeyCache warm via a 60s background refresh
        // (JwtSigningKeyRefreshService); forcing one refresh here makes the test deterministic
        // instead of waiting on (or racing) that interval.
        var refreshService = factory.Services.GetRequiredService<JwtSigningKeyRefreshService>();
        await refreshService.RefreshAsync(CancellationToken.None);
    }
}

file static class HttpClientExtensions6
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
