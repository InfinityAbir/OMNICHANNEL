using System.Net.Http.Headers;
using System.Net.Http.Json;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Knowledge;

namespace Omnichannel.SecurityTests;

/// <summary>PRD §70's security focus: tenant isolation in retrieval, unauthorized knowledge access.</summary>
public class KnowledgeSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Search_NeverReturnsAnotherTenantsDocuments()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PostAsJsonAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Tenant A Secret Pricing", "Our special enterprise discount is 40 percent for tenant A only."));

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        // Same query text a legitimate search on Tenant A's own document would match strongly —
        // Tenant B must still see nothing, proving isolation isn't just "different content
        // happens not to match" but an actual tenant-scoped filter.
        var search = await tenantB.GetAsync(new Uri(
            "/api/v1/knowledge/search?q=" + Uri.EscapeDataString("enterprise discount pricing"), UriKind.Relative));
        var results = await search.Content.ReadFromJsonAsync<List<KnowledgeSearchResultResponse>>();

        Assert.Empty(results!);
    }

    [Fact]
    public async Task Search_NeverReturnsAnotherTenantsDocuments_EvenWhenBothTenantsHaveIndexedContent()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        await tenantA.PostAsJsonAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Policy", "Tenant A returns are accepted within 10 days."));

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));
        await tenantB.PostAsJsonAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Policy", "Tenant B returns are accepted within 60 days."));

        var search = await tenantB.GetAsync(new Uri("/api/v1/knowledge/search?q=" + Uri.EscapeDataString("return policy days"), UriKind.Relative));
        var results = await search.Content.ReadFromJsonAsync<List<KnowledgeSearchResultResponse>>();

        Assert.All(results!, r => Assert.Contains("Tenant B", r.ChunkText));
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
