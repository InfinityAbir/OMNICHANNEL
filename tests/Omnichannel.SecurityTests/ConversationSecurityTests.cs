using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.SecurityTests;

/// <summary>
/// The two PRD §60 mandatory attack tests explicitly deferred from Phase 1's report — this
/// phase is the first with object-with-id endpoints and a permission that not every role has.
/// </summary>
public class ConversationSecurityTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ModifiedObjectId_CannotReachAnotherTenantsConversation()
    {
        using var clientA = factory.CreateClient();
        var tokenA = await RegisterAsync(clientA);
        clientA.UseBearer(tokenA);

        var createResponse = await clientA.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Tenant A Customer" });
        var conversation = await createResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        using var clientB = factory.CreateClient();
        var tokenB = await RegisterAsync(clientB);
        clientB.UseBearer(tokenB);

        // Tenant B, using a real (but foreign) conversation id.
        var response = await clientB.GetAsync(new Uri($"/api/v1/conversations/{conversation!.Id}", UriKind.Relative));

        // 404, not 403 — never confirm the object exists to a tenant that can't see it.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AgentRole_CannotReachAuditLogEndpoint()
    {
        using var ownerClient = factory.CreateClient();
        var ownerToken = await RegisterAsync(ownerClient);
        ownerClient.UseBearer(ownerToken);

        var me = await (await ownerClient.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative)))
            .Content.ReadFromJsonAsync<CurrentUserResponse>();

        // No team-invite endpoint exists yet (out of Phase 1/2 scope) — seed a second,
        // Agent-role user into the same tenant directly, then authenticate as them through the
        // real login endpoint, so what's under test is real authorization enforcement, not the
        // test setup.
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

        var response = await agentClient.GetAsync(new Uri("/api/v1/audit", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
