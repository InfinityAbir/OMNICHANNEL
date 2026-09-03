using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Omnichannel.Contracts.Auth;

namespace Omnichannel.ApiTests;

public class AuthEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static RegisterRequest NewRegisterRequest() => new()
    {
        Email = $"{Guid.NewGuid():N}@example.test",
        Password = "Str0ng!Passw0rd",
        DisplayName = "Test Owner",
        BusinessName = "Test Business",
        TimeZone = "UTC",
    };

    [Fact]
    public async Task Register_WithValidInput_ReturnsTokens()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), NewRegisterRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.False(string.IsNullOrEmpty(body.RefreshToken));
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsGenericError()
    {
        using var client = factory.CreateClient();
        var request = NewRegisterRequest();

        await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), request);
        var second = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), request);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsValidationProblem()
    {
        using var client = factory.CreateClient();
        var weak = new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "short",
            DisplayName = "Test Owner",
            BusinessName = "Test Business",
        };

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), weak);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        var registerRequest = NewRegisterRequest();
        await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), registerRequest);

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = registerRequest.Email, Password = "WrongPassword123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsTokens()
    {
        using var client = factory.CreateClient();
        var registerRequest = NewRegisterRequest();
        await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), registerRequest);

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = registerRequest.Email, Password = registerRequest.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.False(string.IsNullOrEmpty(tokens!.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameOutcomeShapeAsWrongPassword()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = $"{Guid.NewGuid():N}@example.test", Password = "Whatever123!" });

        // Same status as a real user's wrong password — must not reveal whether the email exists.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new RefreshRequest { RefreshToken = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokenPair()
    {
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), NewRegisterRequest());
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative),
            new RefreshRequest { RefreshToken = tokens!.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newTokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotEqual(tokens.RefreshToken, newTokens!.RefreshToken);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUserAndTenant()
    {
        using var client = factory.CreateClient();
        var registerRequest = NewRegisterRequest();
        var registerResponse = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), registerRequest);
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        var response = await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(registerRequest.Email, me!.Email);
        Assert.Equal("Owner", me.Role);
        Assert.Contains("tenant.read", me.Permissions);
    }
}
