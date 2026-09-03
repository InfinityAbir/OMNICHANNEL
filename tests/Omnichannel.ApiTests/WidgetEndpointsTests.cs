using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Contracts.Widget;

namespace Omnichannel.ApiTests;

public class WidgetEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string AllowedOrigin = "https://shop.example";

    private static async Task<HttpResponseMessage> OpenSessionAsync(HttpClient client, string slug, string origin, string visitorKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/widget/{slug}/session", UriKind.Relative))
        {
            Content = JsonContent.Create(new WidgetSessionOpenRequest(visitorKey, "Bob")),
        };
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    private async Task<(HttpClient agent, WidgetSettingsResponse settings)> RegisterAgentAsync()
    {
        var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        var settings = await (await agent.GetAsync(new Uri("/api/v1/channels/widget", UriKind.Relative)))
            .Content.ReadFromJsonAsync<WidgetSettingsResponse>();
        return (agent, settings!);
    }

    [Fact]
    public async Task WidgetSession_RequiresAllowedOrigin_ThenSendsAndAgentSeesMessage()
    {
        var (agent, settings) = await RegisterAgentAsync();

        // Session from a disallowed origin is blocked.
        var visitor = factory.CreateClient();
        var blocked = await OpenSessionAsync(visitor, settings.Slug, "https://evil.example", "visitor-1");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // Business allows its own site, then the session from that site succeeds.
        var allow = await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/widget/origins", UriKind.Relative),
            new WidgetOriginsUpdateRequest([AllowedOrigin]));
        Assert.Equal(HttpStatusCode.OK, allow.StatusCode);
        var afterAllow = await (await agent.GetAsync(new Uri("/api/v1/channels/widget", UriKind.Relative)))
            .Content.ReadFromJsonAsync<WidgetSettingsResponse>();
        Assert.Contains(AllowedOrigin, afterAllow!.AllowedOrigins);

        var sessionResponse = await OpenSessionAsync(visitor, settings.Slug, AllowedOrigin, "visitor-1");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<WidgetSessionResponse>();
        Assert.False(string.IsNullOrWhiteSpace(session!.SessionToken));
        Assert.Equal(settings.ChannelAccountId, session.ChannelAccountId);

        // Visitor sends a message via the widget token.
        var widgetClient = factory.CreateClient();
        widgetClient.UseBearer(session.SessionToken);
        var send = await widgetClient.PostAsJsonAsync(
            new Uri($"/widget/conversations/{session.ConversationId}/messages", UriKind.Relative),
            new WidgetSendRequest(session.ConversationId, "I need help with my order."));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var sent = await send.Content.ReadFromJsonAsync<WidgetMessageResponse>();
        Assert.Equal("Inbound", sent!.Direction);

        // Visitor sees the thread with their message.
        var thread = await widgetClient.GetAsync(
            new Uri($"/widget/conversations/{session.ConversationId}/messages", UriKind.Relative));
        var threadData = await thread.Content.ReadFromJsonAsync<WidgetThreadResponse>();
        Assert.Single(threadData!.Messages);

        // Agent inbox sees the inbound message arrive.
        var agentMessages = await agent.GetAsync(
            new Uri($"/api/v1/conversations/{session.ConversationId}/messages", UriKind.Relative));
        var agentPage = await agentMessages.Content.ReadFromJsonAsync<KeysetPageResponse<MessageResponse>>();
        Assert.Contains(agentPage!.Items, m => m.Text == "I need help with my order." && m.Direction == "Inbound");
    }

    [Fact]
    public async Task WidgetSession_CannotReachAnotherVisitorsConversation()
    {
        var (agent, settings) = await RegisterAgentAsync();
        await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/widget/origins", UriKind.Relative),
            new WidgetOriginsUpdateRequest([AllowedOrigin]));

        // Two different visitors on the same tenant's widget get two different conversations.
        var visitorA = factory.CreateClient();
        var sessionAResponse = await OpenSessionAsync(visitorA, settings.Slug, AllowedOrigin, "visitor-a");
        var sessionA = await sessionAResponse.Content.ReadFromJsonAsync<WidgetSessionResponse>();

        var visitorB = factory.CreateClient();
        var sessionBResponse = await OpenSessionAsync(visitorB, settings.Slug, AllowedOrigin, "visitor-b");
        var sessionB = await sessionBResponse.Content.ReadFromJsonAsync<WidgetSessionResponse>();
        Assert.NotEqual(sessionA!.ConversationId, sessionB!.ConversationId);

        // Visitor B's valid session token must not read or write into Visitor A's conversation,
        // even though both belong to the same tenant (a BOLA path if the route id alone were
        // trusted — the token's own conversation_id claim must gate the object, same principle
        // as ConversationSecurityTests' cross-tenant test, just cross-visitor within one tenant).
        var widgetClientB = factory.CreateClient();
        widgetClientB.UseBearer(sessionB.SessionToken);

        var readOther = await widgetClientB.GetAsync(
            new Uri($"/widget/conversations/{sessionA.ConversationId}/messages", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, readOther.StatusCode);

        var sendOther = await widgetClientB.PostAsJsonAsync(
            new Uri($"/widget/conversations/{sessionA.ConversationId}/messages", UriKind.Relative),
            new WidgetSendRequest(sessionA.ConversationId, "injected"));
        Assert.Equal(HttpStatusCode.NotFound, sendOther.StatusCode);
    }

    [Fact]
    public async Task WidgetSession_UnknownSlug_ReturnsNotFound()
    {
        var visitor = factory.CreateClient();
        var response = await OpenSessionAsync(visitor, Guid.NewGuid().ToString("N"), AllowedOrigin, "visitor-x");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WidgetSettings_RequiresChannelsManagePermission()
    {
        var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));

        var response = await client.GetAsync(new Uri("/api/v1/channels/widget", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
