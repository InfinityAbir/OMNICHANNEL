using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Omnichannel.Contracts.Auth;

namespace Omnichannel.SecurityTests;

/// <summary>
/// PRD §60's mandatory attack tests, scoped to what exists in Phase 1 (auth + tenancy, one
/// endpoint with no route-level object id). "Agent -> Admin endpoint" and "modified object ID
/// -> another tenant's object" need a real object-with-id endpoint to attack meaningfully —
/// those land in Phase 2 (Conversations/Contacts) and must be added there, not skipped.
/// </summary>
public class AuthSecurityTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Unauthenticated_CannotReachProtectedEndpoint()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_CannotReachProtectedEndpoint()
    {
        using var client = factory.CreateClient();
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var signingKey = config["Jwt:SigningKey"]!;
        var issuer = config["Jwt:Issuer"];
        var audience = config["Jwt:Audience"];

        var expiredToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)));

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", expiredToken);
        var response = await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokedRefreshToken_CannotBeUsedToRefresh()
    {
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Test Owner",
            BusinessName = "Test Business",
        });
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        var logoutResponse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/logout", UriKind.Relative),
            new LogoutRequest { RefreshToken = tokens!.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task ModifiedTenantIdClaim_IsRejected_BecauseSignatureNoLongerMatches()
    {
        // A client cannot forge a different tenant_id without the signing key — tampering with
        // a valid token's payload invalidates its signature, which JwtBearer rejects outright.
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Test Owner",
            BusinessName = "Test Business",
        });
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        var parts = tokens!.AccessToken.Split('.');
        var tamperedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"tenant_id\":\"{Guid.NewGuid()}\"}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tamperedToken = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tamperedToken);
        var response = await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
