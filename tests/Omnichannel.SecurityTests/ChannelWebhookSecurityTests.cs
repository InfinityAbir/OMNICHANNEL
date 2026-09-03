using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Channels;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.SecurityTests;

/// <summary>
/// PRD §65's mandated review focus: webhook spoofing and cross-tenant/account mapping. Uses the
/// same test-only fake adapter pattern as Omnichannel.ApiTests.Channels — registered only in
/// test DI, standing in for the WhatsApp slot Phase 7 fills with a real provider.
/// </summary>
public class ChannelWebhookSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private sealed class FakeAdapter : IChannelAdapter
    {
        public Omnichannel.Domain.Channels.ChannelType Type => Omnichannel.Domain.Channels.ChannelType.WhatsApp;

        public ChannelCapabilities Capabilities { get; } = new(4096, true, true, true, true);

        public bool VerifyShouldSucceed { get; set; } = true;

        public List<NormalizedInboundEvent> EventsToReturn { get; } = [];

        public Task<WebhookVerificationResult> VerifyWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
            => Task.FromResult(VerifyShouldSucceed ? WebhookVerificationResult.Valid() : WebhookVerificationResult.Invalid("bad signature"));

        public Task<IReadOnlyList<NormalizedInboundEvent>> ParseWebhookAsync(WebhookRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NormalizedInboundEvent>>(EventsToReturn);

        public Task<ChannelSendResult> SendMessageAsync(ChannelSendRequest request, CancellationToken cancellationToken)
            => Task.FromResult(ChannelSendResult.Ok(Guid.NewGuid().ToString()));
    }

    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, FakeAdapter Adapter) WithFakeAdapter()
    {
        var adapter = new FakeAdapter();
        var customized = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IChannelAdapter>(adapter)));
        return (customized, adapter);
    }

    [Fact]
    public async Task Webhook_SpoofedSignature_IsRejectedAndNeverPersisted()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await RegisterAsync(agent));

        var externalAccountId = $"whatsapp-{Guid.NewGuid():N}";
        await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/account", UriKind.Relative),
            new SetChannelExternalAccountRequest(externalAccountId));

        adapter.VerifyShouldSucceed = false;
        adapter.EventsToReturn.Add(new NormalizedInboundEvent(
            NormalizedInboundEventKind.Message, externalAccountId, "wamid.spoofed",
            VisitorExternalId: "+15559998888", Text: "forged", OccurredAt: DateTimeOffset.UtcNow));

        using var attacker = factoryInstance.CreateClient();
        var response = await attacker.PostAsJsonAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var list = await (await agent.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Webhook_InboundEvent_NeverReachesAnotherTenantsChannelAccount()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;

        // Tenant A connects its own WhatsApp account.
        using var tenantA = factoryInstance.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        var externalAccountIdA = $"whatsapp-{Guid.NewGuid():N}";
        await tenantA.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/account", UriKind.Relative),
            new SetChannelExternalAccountRequest(externalAccountIdA));

        // Tenant B connects a *different* WhatsApp account.
        using var tenantB = factoryInstance.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));
        var externalAccountIdB = $"whatsapp-{Guid.NewGuid():N}";
        await tenantB.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/account", UriKind.Relative),
            new SetChannelExternalAccountRequest(externalAccountIdB));

        // A webhook event addressed to Tenant A's provider account must only ever route to
        // Tenant A — a malicious or misconfigured event can never land in Tenant B's inbox just
        // because both tenants use the same channel type (ADR-0005's tenant isolation guarantee,
        // extended here to the webhook resolution path added in Phase 6).
        adapter.EventsToReturn.Add(new NormalizedInboundEvent(
            NormalizedInboundEventKind.Message, externalAccountIdA, "wamid.for-a",
            VisitorExternalId: "+15551112222", Text: "message for tenant A", OccurredAt: DateTimeOffset.UtcNow));

        using var webhookClient = factoryInstance.CreateClient();
        var response = await webhookClient.PostAsJsonAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), new { });
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

file static class HttpClientExtensions
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
