using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Channels;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.SecurityTests;

/// <summary>
/// PRD §67's mandated review focus: incorrect account mapping, cross-tenant channel access,
/// unauthorized outbound messages. Mirrors WhatsAppWebhookSecurityTests against Instagram's own
/// real production adapter.
/// </summary>
public class InstagramWebhookSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string AppSecret = "test-only-instagram-app-secret-never-used-outside-automated-tests";

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public async Task Webhook_ForgedSignature_IsRejectedAndNeverPersisted()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await RegisterAsync(agent));

        var externalAccountId = $"ig-{Guid.NewGuid():N}";
        await agent.PutAsJsonAsync(new Uri("/api/v1/channels/instagram/account", UriKind.Relative), new SetChannelExternalAccountRequest(externalAccountId));

        const string bodyTemplate = """
        {"object":"instagram","entry":[{"id":"__ACCOUNT__","messaging":[{"sender":{"id":"1"},"recipient":{"id":"__ACCOUNT__"},"timestamp":1778223722476,"message":{"mid":"forged-mid","text":"forged"}}]}]}
        """;
        var body = bodyTemplate.Replace("__ACCOUNT__", externalAccountId, StringComparison.Ordinal);

        using var attacker = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/instagram", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));

        var response = await attacker.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var list = await (await agent.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Webhook_GenuineSignature_RoutesOnlyToConnectedTenant()
    {
        using var tenantA = factory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        var externalAccountIdA = $"ig-{Guid.NewGuid():N}";
        await tenantA.PutAsJsonAsync(new Uri("/api/v1/channels/instagram/account", UriKind.Relative), new SetChannelExternalAccountRequest(externalAccountIdA));

        using var tenantB = factory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));
        var externalAccountIdB = $"ig-{Guid.NewGuid():N}";
        await tenantB.PutAsJsonAsync(new Uri("/api/v1/channels/instagram/account", UriKind.Relative), new SetChannelExternalAccountRequest(externalAccountIdB));

        const string bodyTemplate = """
        {"object":"instagram","entry":[{"id":"__ACCOUNT__","messaging":[{"sender":{"id":"1"},"recipient":{"id":"__ACCOUNT__"},"timestamp":1778223722476,"message":{"mid":"real-mid-1","text":"for tenant A only"}}]}]}
        """;
        var body = bodyTemplate.Replace("__ACCOUNT__", externalAccountIdA, StringComparison.Ordinal);

        using var provider = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/instagram", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", Sign(body));

        var response = await provider.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listA = await (await tenantA.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        Assert.Single(listA!.Items);

        var listB = await (await tenantB.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        Assert.Empty(listB!.Items);
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

file static class HttpClientExtensions2
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
