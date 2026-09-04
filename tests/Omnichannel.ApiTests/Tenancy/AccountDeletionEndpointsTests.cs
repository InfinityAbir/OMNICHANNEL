using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Tenancy;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Infrastructure.Tenancy;

namespace Omnichannel.ApiTests.Tenancy;

/// <summary>Data retention / account deletion (ADR-0030), exercised end-to-end through the real
/// endpoints and the real (force-triggered, not timer-waited) purge job.</summary>
public class AccountDeletionEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task RequestTenantDeletion_AsOwner_SchedulesDeletion()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PostAsync(new Uri("/api/v1/tenant/deletion", UriKind.Relative), null);
        var status = await response.Content.ReadFromJsonAsync<TenantDeletionStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(nameof(TenantStatus.PendingDeletion), status!.Status);
        Assert.NotNull(status.ScheduledDeletionAt);
        Assert.True(status.ScheduledDeletionAt > DateTimeOffset.UtcNow.AddDays(13));
    }

    [Fact]
    public async Task RequestTenantDeletion_AsAgent_Forbidden()
    {
        var agentClient = await CreateAgentClientAsync();

        var response = await agentClient.PostAsync(new Uri("/api/v1/tenant/deletion", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelTenantDeletion_AfterRequesting_RevertsToActive()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        await agent.PostAsync(new Uri("/api/v1/tenant/deletion", UriKind.Relative), null);

        var cancelResponse = await agent.DeleteAsync(new Uri("/api/v1/tenant/deletion", UriKind.Relative));
        var status = await cancelResponse.Content.ReadFromJsonAsync<TenantDeletionStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(nameof(TenantStatus.Active), status!.Status);
        Assert.Null(status.ScheduledDeletionAt);
    }

    [Fact]
    public async Task CancelTenantDeletion_WhenNothingScheduled_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.DeleteAsync(new Uri("/api/v1/tenant/deletion", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PendingDeletionTenant_CannotLogIn()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        const string password = "Str0ng!Passw0rd";
        using var agent = factory.CreateClient();
        agent.UseBearer(await RegisterAsync(agent, email, password));
        await agent.PostAsync(new Uri("/api/v1/tenant/deletion", UriKind.Relative), null);

        var loginResponse = await agent.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task PurgeJob_AfterGracePeriodElapses_RemovesOperationalDataButKeepsAuditAndTenantRow()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var me = await (await agent.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .Content.ReadFromJsonAsync<CurrentUserResponse>();
        var tenantId = me!.TenantId;

        await agent.PostAsJsonAsync(new Uri("/api/v1/automation-rules", UriKind.Relative),
            new { Keyword = "purge-test", ApplyTagName = "PurgeTest" });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
            // Force the grace period into the past so the purge job picks it up now instead of
            // waiting 14 real days.
            tenant.ScheduleDeletion(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var purgeService = factory.Services.GetRequiredService<TenantDataPurgeService>();
        await purgeService.PurgeDueTenantsAsync(CancellationToken.None);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var purgedTenant = await verifyDb.Tenants.SingleAsync(t => t.Id == tenantId);
        Assert.Equal(TenantStatus.Deleted, purgedTenant.Status);

        var remainingRules = await verifyDb.AutomationRules.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).CountAsync();
        Assert.Equal(0, remainingRules);

        var auditEntries = await verifyDb.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).CountAsync();
        Assert.True(auditEntries > 0, "Audit trail should survive the purge, not be deleted along with operational data.");
    }

    [Fact]
    public async Task DeleteMyAccount_AsNonOwnerMember_RemovesMembershipAndBlocksFutureLogin()
    {
        var (agentClient, agentEmail, agentPassword) = await CreateAgentClientWithCredentialsAsync();

        var response = await agentClient.DeleteAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var freshClient = factory.CreateClient();
        var loginResponse = await freshClient.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = agentEmail, Password = agentPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteMyAccount_AsSoleOwnerOfMultiMemberTenant_IsBlocked()
    {
        using var owner = factory.CreateClient();
        var ownerToken = await TestAuth.RegisterAndGetAccessTokenAsync(owner);
        owner.UseBearer(ownerToken);
        await CreateAgentInSameTenantAsync(owner);

        var response = await owner.DeleteAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("sole owner", problem!.Detail, StringComparison.OrdinalIgnoreCase);

        // The account must NOT have been touched by the blocked attempt.
        var meResponse = await owner.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteMyAccount_AsSoleOwnerOfSoloTenant_SucceedsAndSchedulesTenantDeletionToo()
    {
        using var owner = factory.CreateClient();
        var ownerToken = await TestAuth.RegisterAndGetAccessTokenAsync(owner);
        owner.UseBearer(ownerToken);
        var me = await (await owner.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .Content.ReadFromJsonAsync<CurrentUserResponse>();
        var tenantId = me!.TenantId;

        var response = await owner.DeleteAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        Assert.Equal(TenantStatus.PendingDeletion, tenant.Status);
    }

    private async Task<HttpClient> CreateAgentClientAsync()
    {
        var (client, _, _) = await CreateAgentClientWithCredentialsAsync();
        return client;
    }

    private async Task<(HttpClient Client, string Email, string Password)> CreateAgentClientWithCredentialsAsync()
    {
        using var ownerClient = factory.CreateClient();
        var ownerToken = await TestAuth.RegisterAndGetAccessTokenAsync(ownerClient);
        ownerClient.UseBearer(ownerToken);

        var (agentEmail, agentPassword) = await CreateAgentInSameTenantAsync(ownerClient);

        var agentClient = factory.CreateClient();
        var loginResponse = await agentClient.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { Email = agentEmail, Password = agentPassword });
        var agentTokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        agentClient.UseBearer(agentTokens!.AccessToken);
        return (agentClient, agentEmail, agentPassword);
    }

    private async Task<(string Email, string Password)> CreateAgentInSameTenantAsync(HttpClient ownerClient)
    {
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

        return (agentEmail, agentPassword);
    }

    private static async Task<string> RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = email,
            Password = password,
            DisplayName = "Deletion Test",
            BusinessName = $"Deletion Test Biz {Guid.NewGuid():N}",
        });
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        return tokens!.AccessToken;
    }
}

file static class HttpClientExtensions7
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
