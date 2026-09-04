using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Ai;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Notifications;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.SecurityTests;

/// <summary>
/// Phase 16 (ADR-0027): a tenant's AI provider key and SMTP password must never be readable or
/// overwritable by another tenant, and only a role with ai.configure/tenant.update — not the
/// read-only ai.read/tenant.read an Agent gets — may write or clear them.
/// </summary>
public class TenantSecretsSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task AiProviderSettings_TenantACannotSeeTenantBsConfiguration()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://tenant-a-only.example/v1", "tenant-a-model", "tenant-a-secret-key"));

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.GetAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<AiProviderSettingsResponse>();

        Assert.False(settings!.HasApiKey);
        Assert.NotEqual("https://tenant-a-only.example/v1", settings.BaseUrl);
    }

    [Fact]
    public async Task EmailSettings_TenantACannotSeeTenantBsConfiguration()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("tenant-a-only.example", 587, "a@example.test", "a@example.test", null, "tenant-a-secret-password"));

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.GetAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<EmailSettingsResponse>();

        Assert.False(settings!.IsConfigured);
        Assert.False(settings.HasPassword);
        Assert.Null(settings.Host);
    }

    [Fact]
    public async Task TenantSecrets_StoredEncrypted_NeverPlaintextInDatabase()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await RegisterAsync(agent));
        const string plaintextKey = "super-secret-plaintext-marker-value";

        await agent.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://example.test/v1", "model", plaintextKey));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var stored = await db.TenantSecrets.IgnoreQueryFilters().Where(s => s.Purpose == "ai.apikey").ToListAsync();

        Assert.NotEmpty(stored);
        Assert.All(stored, s => Assert.DoesNotContain(plaintextKey, s.EncryptedValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentRole_CanReadButCannotWriteOrClearAiProviderSettings()
    {
        var agentClient = await CreateAgentClientAsync();

        var getResponse = await agentClient.GetAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var putResponse = await agentClient.PutAsJsonAsync(new Uri("/api/v1/ai/provider-settings", UriKind.Relative),
            new UpdateAiProviderSettingsRequest("OpenAiCompatible", "https://example.test/v1", "model", "key"));
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);

        var deleteResponse = await agentClient.DeleteAsync(new Uri("/api/v1/ai/provider-settings/key", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);

        var testResponse = await agentClient.PostAsync(new Uri("/api/v1/ai/provider-settings/test", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Forbidden, testResponse.StatusCode);
    }

    [Fact]
    public async Task AgentRole_CanReadButCannotWriteOrClearEmailSettings()
    {
        var agentClient = await CreateAgentClientAsync();

        var getResponse = await agentClient.GetAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var putResponse = await agentClient.PutAsJsonAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative),
            new UpdateEmailSettingsRequest("smtp.gmail.com", 587, "me@example.test", "support@example.test", null, "password"));
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);

        var deleteResponse = await agentClient.DeleteAsync(new Uri("/api/v1/tenant/email-settings", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    private async Task<HttpClient> CreateAgentClientAsync()
    {
        using var ownerClient = factory.CreateClient();
        var ownerToken = await RegisterAsync(ownerClient);
        ownerClient.UseBearer(ownerToken);

        var me = await (await ownerClient.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .Content.ReadFromJsonAsync<CurrentUserResponse>();

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var agentEmail = $"{Guid.NewGuid():N}@example.test";
        const string agentPassword = "Str0ng!Passw0rd";
        var createResult = await identity.CreateUserAsync(agentEmail, agentPassword, CancellationToken.None);
        Assert.True(createResult.Succeeded);

        var agentRole = await db.Roles.SingleAsync(r => r.SystemRole == SystemRole.Agent, CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        db.UserProfiles.Add(User.Create(createResult.UserId, agentEmail, "Agent User", now));
        db.Memberships.Add(TenantMembership.Create(me!.TenantId, createResult.UserId, agentRole.Id, now));
        await db.SaveChangesAsync(CancellationToken.None);

        var agentClient = factory.CreateClient();
        var loginResponse = await agentClient.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = agentEmail, Password = agentPassword });
        var agentTokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        agentClient.UseBearer(agentTokens!.AccessToken);
        return agentClient;
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Test Owner",
            BusinessName = $"Test Business {Guid.NewGuid():N}",
        });
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        return tokens!.AccessToken;
    }
}

file static class HttpClientExtensions5
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
