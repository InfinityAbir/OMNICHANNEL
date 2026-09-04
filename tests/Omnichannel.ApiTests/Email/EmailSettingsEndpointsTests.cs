using System.Net;
using System.Net.Http.Json;
using Omnichannel.Contracts.Notifications;

namespace Omnichannel.ApiTests.Email;

/// <summary>Phase 16 (ADR-0027): per-tenant SMTP configuration CRUD + clear, exercised through the real endpoints. The "test" endpoint sends a real email through whatever SMTP is resolved (tenant's own or the platform default) — not exercised here to avoid triggering a real send from the test suite; its resolution logic (tenant-configured vs. platform-default fallback) was verified live during the phase.</summary>
public class EmailSettingsEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Get_NewTenant_ReturnsUnconfigured()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.GetAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<EmailSettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(settings!.IsConfigured);
        Assert.False(settings.HasPassword);
        Assert.Null(settings.Host);
    }

    [Fact]
    public async Task Update_ValidSettingsWithPassword_RoundTrips()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("smtp.gmail.com", 587, "me@example.test", "support@example.test", "My Business", "fake-app-password"));
        var settings = await response.Content.ReadFromJsonAsync<EmailSettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(settings!.IsConfigured);
        Assert.True(settings.HasPassword);
        Assert.Equal("smtp.gmail.com", settings.Host);
        Assert.Equal("My Business", settings.FromName);
    }

    [Fact]
    public async Task Update_MissingPassword_KeepsExistingHasPasswordTrue()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("smtp.gmail.com", 587, "me@example.test", "support@example.test", null, "fake-app-password"));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("smtp.gmail.com", 465, "me@example.test", "support@example.test", null, null));
        var settings = await response.Content.ReadFromJsonAsync<EmailSettingsResponse>();

        Assert.True(settings!.HasPassword);
        Assert.Equal(465, settings.Port);
    }

    [Theory]
    [InlineData("", "me@example.test", "support@example.test")]
    [InlineData("smtp.gmail.com", "", "support@example.test")]
    [InlineData("smtp.gmail.com", "me@example.test", "")]
    public async Task Update_MissingRequiredField_ReturnsBadRequest(string host, string username, string fromAddress)
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest(host, 587, username, fromAddress, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_PortOutOfRange_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("smtp.gmail.com", 70000, "me@example.test", "support@example.test", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Clear_ResetsToUnconfigured()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("smtp.gmail.com", 587, "me@example.test", "support@example.test", null, "fake-app-password"));

        var clearResponse = await agent.DeleteAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        var getResponse = await agent.GetAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative));
        var settings = await getResponse.Content.ReadFromJsonAsync<EmailSettingsResponse>();
        Assert.False(settings!.IsConfigured);
        Assert.False(settings.HasPassword);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(new Uri("/api/v1/tenant/email-settings/test", UriKind.Relative), null)).StatusCode);
    }
}
