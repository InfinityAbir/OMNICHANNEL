using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Automation;
using Omnichannel.Contracts.Auth;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.SecurityTests;

/// <summary>PRD §72's security focus: rules cannot access other tenants, cannot bypass authorization, cannot send unlimited messages, cannot disable safety controls.</summary>
public class AutomationSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task AutomationRules_TenantACannotSeeTenantBsRules()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PostAsJsonAsync(new Uri("/api/v1/automation-rules", UriKind.Relative),
            new CreateAutomationRuleRequest { Keyword = "secret-a", ApplyTagName = "TagA" });

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.GetAsync(new Uri("/api/v1/automation-rules", UriKind.Relative));
        var rules = await response.Content.ReadFromJsonAsync<List<AutomationRuleResponse>>();

        Assert.Empty(rules!);
    }

    [Fact]
    public async Task SavedReplies_TenantACannotSeeTenantBsReplies()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PostAsJsonAsync(new Uri("/api/v1/saved-replies", UriKind.Relative),
            new SavedReplyRequest { Title = "Tenant A Only", Text = "Secret reply text." });

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.GetAsync(new Uri("/api/v1/saved-replies", UriKind.Relative));
        var replies = await response.Content.ReadFromJsonAsync<List<SavedReplyResponse>>();

        Assert.Empty(replies!);
    }

    [Fact]
    public async Task Notifications_TenantACannotSeeTenantBsNotifications()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PostAsJsonAsync(new Uri("/api/v1/automation-rules", UriKind.Relative),
            new CreateAutomationRuleRequest { Keyword = "refund", Escalate = true });
        var conversation = await (await tenantA.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new Omnichannel.Contracts.Conversations.CreateConversationRequest { NewContactDisplayName = "Customer" }))
            .Content.ReadFromJsonAsync<Omnichannel.Contracts.Conversations.ConversationDetailResponse>();
        await tenantA.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/messages", UriKind.Relative),
            new Omnichannel.Contracts.Conversations.AddMessageRequest { Direction = "Inbound", SenderType = "Customer", Text = "I want a refund" });

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative));
        var notifications = await response.Content.ReadFromJsonAsync<List<Omnichannel.Contracts.Notifications.NotificationResponse>>();

        Assert.Empty(notifications!);
    }

    [Fact]
    public async Task AgentRole_CannotManageAutomationRulesOrBusinessHours_ButCanManageSavedReplies()
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

        var ruleResponse = await agentClient.PostAsJsonAsync(new Uri("/api/v1/automation-rules", UriKind.Relative),
            new CreateAutomationRuleRequest { Keyword = "refund", Escalate = true });
        Assert.Equal(HttpStatusCode.Forbidden, ruleResponse.StatusCode);

        var hoursResponse = await agentClient.PutAsJsonAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative),
            new UpdateTenantBusinessHoursRequest(null, null));
        Assert.Equal(HttpStatusCode.Forbidden, hoursResponse.StatusCode);

        // Saved replies are an agent tool, not an admin-only config — Agent role should succeed.
        var replyResponse = await agentClient.PostAsJsonAsync(new Uri("/api/v1/saved-replies", UriKind.Relative),
            new SavedReplyRequest { Title = "Welcome", Text = "Hi there!" });
        Assert.Equal(HttpStatusCode.Created, replyResponse.StatusCode);
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
