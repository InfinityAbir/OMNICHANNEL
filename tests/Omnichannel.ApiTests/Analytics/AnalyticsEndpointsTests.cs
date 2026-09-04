using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Analytics;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.ApiTests.Analytics;

/// <summary>Phase 14 (PRD §73): inbox/response-time/resolution/AI/channel/agent metrics.</summary>
public class AnalyticsEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private (WebApplicationFactory<Program> Factory, Ai.FakeAiProvider Provider) WithFakeProvider()
    {
        var provider = new Ai.FakeAiProvider();
        var customized = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        return (customized, provider);
    }

    private static Task<AnalyticsSummaryResponse> GetSummaryAsync(HttpClient agent)
        => agent.GetFromJsonAsync<AnalyticsSummaryResponse>(new Uri("/api/v1/analytics/summary", UriKind.Relative))!;

    [Fact]
    public async Task Summary_NoConversations_ReturnsZeroedMetrics()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var summary = await GetSummaryAsync(agent);

        Assert.Equal(0, summary.TotalConversations);
        Assert.Equal(0, summary.ResolutionRatePercent);
        Assert.Null(summary.AverageFirstResponseMinutes);
        Assert.Null(summary.AverageResolutionMinutes);
        Assert.Empty(summary.ByChannel);
        Assert.Empty(summary.ByAgent);
    }

    [Fact]
    public async Task Summary_CountsConversationsByStatusAndChannel()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var create1 = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer One" });
        var conversation1 = await create1.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        var create2 = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer Two" });
        var conversation2 = await create2.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await agent.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation2!.Id}/status", UriKind.Relative),
            new ChangeStatusRequest { Status = "Closed" });

        var summary = await GetSummaryAsync(agent);

        Assert.Equal(2, summary.TotalConversations);
        Assert.Equal(1, summary.OpenConversations);
        Assert.Equal(1, summary.ClosedConversations);
        Assert.Equal(50.0, summary.ResolutionRatePercent);
        Assert.NotNull(summary.AverageResolutionMinutes);
        var manual = Assert.Single(summary.ByChannel);
        Assert.Equal("Manual", manual.ChannelType);
        Assert.Equal(2, manual.ConversationCount);
        Assert.NotNull(conversation1);
    }

    [Fact]
    public async Task Summary_ComputesFirstResponseTime()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var create = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer", InitialMessageText = "Hello?" });
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await agent.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/messages", UriKind.Relative),
            new AddMessageRequest { Direction = "Outbound", SenderType = "Agent", Text = "Hi, how can I help?" });

        var summary = await GetSummaryAsync(agent);

        Assert.NotNull(summary.AverageFirstResponseMinutes);
        Assert.True(summary.AverageFirstResponseMinutes >= 0);
    }

    [Fact]
    public async Task Summary_CountsAiSuggestionsAndConfidence()
    {
        var (customFactory, provider) = WithFakeProvider();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));
        provider.ConfidenceToReturn = 0.75;

        var create = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer", InitialMessageText = "Any deals today?" });
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await agent.PostAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/ai-suggestions", UriKind.Relative), null);

        var summary = await GetSummaryAsync(agent);

        Assert.Equal(1, summary.AiSuggestionsGenerated);
        Assert.Equal(0.75, summary.AverageAiSuggestionConfidence);
    }

    [Fact]
    public async Task Summary_GroupsByAssignedAgent()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var me = await agent.GetFromJsonAsync<CurrentUserResponse>(new Uri("/api/v1/users/me", UriKind.Relative));

        var create = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer" });
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await agent.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/assign", UriKind.Relative),
            new AssignConversationRequest { UserId = me!.UserId });

        var summary = await GetSummaryAsync(agent);

        var agentMetric = Assert.Single(summary.ByAgent);
        Assert.Equal(me.UserId, agentMetric.AgentUserId);
        Assert.Equal(1, agentMetric.AssignedConversationCount);
        Assert.Equal(0, agentMetric.ClosedConversationCount);
    }

    [Fact]
    public async Task Summary_InvalidDateRange_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var from = DateTimeOffset.UtcNow;
        var to = from.AddDays(-1);

        var response = await agent.GetAsync(new Uri(
            $"/api/v1/analytics/summary?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
