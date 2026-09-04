using System.Net;
using System.Net.Http.Json;
using Omnichannel.Contracts.Ai;

namespace Omnichannel.ApiTests.Ai;

/// <summary>Phase 16 (ADR-0027): per-tenant AI provider configuration CRUD + clear, exercised through the real endpoints. Detect/Test against a real provider are covered live (see the phase's own verification notes) rather than here, since they require a real outbound key.</summary>
public class AiProviderSettingsEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Get_NewTenant_ReturnsPlatformDefaultWithNoApiKey()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.GetAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<AiProviderSettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OpenAiCompatible", settings!.ProviderKind);
        Assert.False(settings.HasApiKey);
        Assert.False(string.IsNullOrWhiteSpace(settings.Model));
    }

    [Fact]
    public async Task Update_ValidOpenAiCompatibleSettingsWithKey_RoundTrips()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://api.example-provider.test/v1", "some-model", "fake-test-key"));
        var settings = await response.Content.ReadFromJsonAsync<AiProviderSettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://api.example-provider.test/v1", settings!.BaseUrl);
        Assert.Equal("some-model", settings.Model);
        Assert.True(settings.HasApiKey);
    }

    [Fact]
    public async Task Update_AnthropicKind_ClearsBaseUrl()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("Anthropic", "https://ignored.example", "claude-3-5-sonnet-latest", "fake-key"));
        var settings = await response.Content.ReadFromJsonAsync<AiProviderSettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Anthropic", settings!.ProviderKind);
        Assert.Null(settings.BaseUrl);
    }

    [Fact]
    public async Task Update_MissingApiKey_KeepsExistingHasApiKeyTrue()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://api.example-provider.test/v1", "model-a", "fake-key"));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://api.example-provider.test/v1", "model-b", null));
        var settings = await response.Content.ReadFromJsonAsync<AiProviderSettingsResponse>();

        Assert.True(settings!.HasApiKey);
        Assert.Equal("model-b", settings.Model);
    }

    [Fact]
    public async Task Update_InvalidProviderKind_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("NotARealProvider", null, "model", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_OpenAiCompatibleMissingBaseUrl_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", null, "model", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_BlankModel_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://example.test", " ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClearApiKey_RemovesKeyButKeepsOtherSettings()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://api.example-provider.test/v1", "model-a", "fake-key"));

        var clearResponse = await agent.DeleteAsync(new Uri("/api/v1/ai/provider-settings/key", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        var getResponse = await agent.GetAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative));
        var settings = await getResponse.Content.ReadFromJsonAsync<AiProviderSettingsResponse>();
        Assert.False(settings!.HasApiKey);
        Assert.Equal("model-a", settings.Model);
    }

    [Fact]
    public async Task Detect_BlankApiKey_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PostAsJsonAsync(new Uri("/api/v1/ai/provider-settings/detect", UriKind.Relative),
            new DetectAiProviderRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://example.test", "model", null))).StatusCode);
    }

}
