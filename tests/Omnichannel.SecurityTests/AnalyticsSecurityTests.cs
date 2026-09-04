using System.Net.Http.Json;
using Omnichannel.Contracts.Analytics;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.SecurityTests;

/// <summary>PRD §73's security focus: analytics queries must never aggregate across tenants.</summary>
public class AnalyticsSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Summary_NeverIncludesAnotherTenantsConversations()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        for (var i = 0; i < 5; i++)
        {
            await tenantA.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
                new CreateConversationRequest { NewContactDisplayName = $"Tenant A Customer {i}" });
        }

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));
        await tenantB.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Tenant B Customer" });

        var response = await tenantB.GetAsync(new Uri("/api/v1/analytics/summary", UriKind.Relative));
        var summary = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>();

        // Tenant B must see only its own single conversation, never Tenant A's five.
        Assert.Equal(1, summary!.TotalConversations);
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
