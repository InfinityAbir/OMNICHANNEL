using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Channels;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.ApiTests.Channels;

public class ChannelWebhookEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, FakeChannelAdapter Adapter) WithFakeAdapter()
    {
        var adapter = new FakeChannelAdapter();
        var customized = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IChannelAdapter>(adapter)));
        return (customized, adapter);
    }

    [Fact]
    public async Task Webhook_UnsupportedChannelType_ReturnsNotFound()
    {
        using var client = factory.CreateClient();
        var get = await client.GetAsync(new Uri("/webhooks/telegram?hub.challenge=x", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var post = await client.PostAsync(new Uri("/webhooks/telegram", UriKind.Relative), JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task Webhook_GetHandshake_ReturnsChallengeWhenValid()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;
        using var client = factoryInstance.CreateClient();
        adapter.ChallengeResponse = "echo-me";

        var response = await client.GetAsync(new Uri("/webhooks/whatsapp?hub.challenge=echo-me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("echo-me", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_GetHandshake_RejectedWhenAdapterInvalid()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;
        using var client = factoryInstance.CreateClient();
        adapter.VerifyShouldSucceed = false;

        var response = await client.GetAsync(new Uri("/webhooks/whatsapp", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_InboundMessage_CreatesConversationAndIsIdempotent()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;

        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var externalAccountId = $"whatsapp-{Guid.NewGuid():N}";
        var setAccount = await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/account", UriKind.Relative),
            new SetChannelExternalAccountRequest(externalAccountId));
        Assert.Equal(HttpStatusCode.OK, setAccount.StatusCode);

        adapter.EventsToReturn.Add(new Omnichannel.Application.Abstractions.NormalizedInboundEvent(
            Omnichannel.Application.Abstractions.NormalizedInboundEventKind.Message,
            externalAccountId,
            "wamid.abc123",
            VisitorExternalId: "+15551234567",
            VisitorDisplayName: "Jane Customer",
            Text: "Hi, is this open?",
            OccurredAt: DateTimeOffset.UtcNow));

        using var webhookClient = factoryInstance.CreateClient();
        var first = await webhookClient.PostAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var list = await (await agent.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        Assert.Single(list!.Items);
        var conversationId = list.Items[0].Id;

        var messages = await (await agent.GetAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<MessageResponse>>();
        Assert.Single(messages!.Items);
        Assert.Equal("Hi, is this open?", messages.Items[0].Text);

        // Provider retries the exact same delivery (same external message id) — must not duplicate.
        var second = await webhookClient.PostAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var messagesAfterRetry = await (await agent.GetAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<MessageResponse>>();
        Assert.Single(messagesAfterRetry!.Items);
    }

    [Fact]
    public async Task Credentials_NeverReturnedInApiResponse()
    {
        var (customFactory, _) = WithFakeAdapter();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        const string secret = "super-secret-permanent-token-value";
        var set = await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/credentials", UriKind.Relative),
            new SetChannelCredentialRequest(secret));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var setBody = await set.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, setBody);

        var get = await agent.GetAsync(new Uri("/api/v1/channels/whatsapp", UriKind.Relative));
        var getBody = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, getBody);
        var response = await get.Content.ReadFromJsonAsync<ChannelAccountAdminResponse>();
        Assert.True(response!.CredentialConfigured);
    }

    [Fact]
    public async Task OutboundSend_RetriesTransientFailureThenSucceeds()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var externalAccountId = $"whatsapp-{Guid.NewGuid():N}";
        await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/account", UriKind.Relative),
            new SetChannelExternalAccountRequest(externalAccountId));
        await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/credentials", UriKind.Relative),
            new SetChannelCredentialRequest("token"));

        adapter.EventsToReturn.Add(new Omnichannel.Application.Abstractions.NormalizedInboundEvent(
            Omnichannel.Application.Abstractions.NormalizedInboundEventKind.Message,
            externalAccountId, "wamid.seed1", VisitorExternalId: "+15550001111", Text: "hello", OccurredAt: DateTimeOffset.UtcNow));
        using var webhookClient = factoryInstance.CreateClient();
        await webhookClient.PostAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), JsonContent.Create(new { }));

        var list = await (await agent.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        var conversationId = list!.Items[0].Id;

        adapter.SendResultQueue.Enqueue(Omnichannel.Application.Abstractions.ChannelSendResult.Failed(Omnichannel.Application.Abstractions.ChannelSendErrorKind.Transient, "temporary"));
        adapter.SendResultQueue.Enqueue(Omnichannel.Application.Abstractions.ChannelSendResult.Failed(Omnichannel.Application.Abstractions.ChannelSendErrorKind.Transient, "temporary"));
        adapter.SendResultQueue.Enqueue(Omnichannel.Application.Abstractions.ChannelSendResult.Ok("wamid.reply1"));

        var reply = await agent.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative),
            new AddMessageRequest { Text = "We're open until 6pm." });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var sent = await reply.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Sent", sent!.DeliveryStatus);
        Assert.Equal(3, adapter.SendCallCount);
    }

    [Fact]
    public async Task OutboundSend_PermanentFailureDoesNotRetry()
    {
        var (customFactory, adapter) = WithFakeAdapter();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var externalAccountId = $"whatsapp-{Guid.NewGuid():N}";
        await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/account", UriKind.Relative),
            new SetChannelExternalAccountRequest(externalAccountId));
        await agent.PutAsJsonAsync(
            new Uri("/api/v1/channels/whatsapp/credentials", UriKind.Relative),
            new SetChannelCredentialRequest("token"));

        adapter.EventsToReturn.Add(new Omnichannel.Application.Abstractions.NormalizedInboundEvent(
            Omnichannel.Application.Abstractions.NormalizedInboundEventKind.Message,
            externalAccountId, "wamid.seed2", VisitorExternalId: "+15550002222", Text: "hello", OccurredAt: DateTimeOffset.UtcNow));
        using var webhookClient = factoryInstance.CreateClient();
        await webhookClient.PostAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), JsonContent.Create(new { }));

        var list = await (await agent.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative)))
            .Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        var conversationId = list!.Items[0].Id;

        adapter.SendResultQueue.Enqueue(Omnichannel.Application.Abstractions.ChannelSendResult.Failed(Omnichannel.Application.Abstractions.ChannelSendErrorKind.InvalidRecipient, "unknown number"));

        var reply = await agent.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative),
            new AddMessageRequest { Text = "hello back" });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var sent = await reply.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Failed", sent!.DeliveryStatus);
        Assert.Equal(1, adapter.SendCallCount);
    }
}
