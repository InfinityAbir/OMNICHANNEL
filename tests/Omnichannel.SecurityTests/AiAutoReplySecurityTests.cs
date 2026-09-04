using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Ai;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.SecurityTests;

/// <summary>PRD §71's security focus: unauthorized AI actions, human takeover race conditions — covered here at the config/authorization boundary; the decision-pipeline behavior itself (confidence/business-hours/escalation) is covered in Omnichannel.ApiTests.</summary>
public class AiAutoReplySecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task AutoReplySettings_TenantACannotSeeOrAffectTenantBsSettings()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PutAsJsonAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative),
            new UpdateAiAutoReplySettingsRequest(true, 0.6, 999, null));

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.GetAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<AiAutoReplySettingsResponse>();

        // Tenant B must see its own untouched defaults, not Tenant A's configuration.
        Assert.False(settings!.Enabled);
        Assert.Equal(0.85, settings.ConfidenceThreshold);
        Assert.Equal(50, settings.DailyLimit);
    }

    [Fact]
    public async Task AgentRole_CannotConfigureAutoReplySettings()
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

        using var agentClient = factory.CreateClient();
        var loginResponse = await agentClient.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = agentEmail, Password = agentPassword });
        var agentTokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        agentClient.UseBearer(agentTokens!.AccessToken);

        // Agent role has ai.read but not ai.configure — auto-reply settings are a business-owner
        // level decision (PRD §71: "unauthorized AI actions" is an explicit security focus),
        // distinct from the per-conversation Suggest-mode use Agents already have.
        var settingsResponse = await agentClient.PutAsJsonAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative),
            new UpdateAiAutoReplySettingsRequest(true, 0.5, 100, null));
        Assert.Equal(HttpStatusCode.Forbidden, settingsResponse.StatusCode);

        var createConversation = await ownerClient.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer" });
        var conversation = await createConversation.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        var modeResponse = await agentClient.PutAsJsonAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/ai-mode", UriKind.Relative),
            new SetConversationAiModeRequest("AutoReply"));
        Assert.Equal(HttpStatusCode.Forbidden, modeResponse.StatusCode);
    }

    [Fact]
    public async Task ModifiedConversationId_CannotSetAiModeOnAnotherTenantsConversation()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        var createResponse = await tenantA.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Tenant A Customer" });
        var conversation = await createResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.PutAsJsonAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/ai-mode", UriKind.Relative),
            new SetConversationAiModeRequest("AutoReply"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

file static class HttpClientExtensions
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
}
