using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Ai;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.ApiTests.Ai;

/// <summary>
/// Phase 12 (PRD §71): the auto-reply decision pipeline (business hours, eligibility, confidence,
/// escalation, limits) exercised end-to-end through the real endpoints, plus the two config
/// endpoints (per-conversation AI mode, tenant-wide auto-reply settings) that gate it.
/// </summary>
public class AiAutoReplyEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    // Deliberately not midnight-adjacent — a schedule covering the whole day for every day of the
    // week is "always open" regardless of which real wall-clock day/time the test happens to run
    // at, with only a negligible (~1s/day) flake window right at day boundary.
    private static readonly Dictionary<DayOfWeek, List<BusinessHoursWindowRequest>> AlwaysOpen =
        Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => new List<BusinessHoursWindowRequest> { new("00:00", "23:59:59") });

    private (WebApplicationFactory<Program> Factory, FakeAiProvider Provider) WithFakeProvider()
    {
        var provider = new FakeAiProvider();
        var customized = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        return (customized, provider);
    }

    private static async Task<(HttpClient Agent, Guid ConversationId)> RegisterAndCreateConversationAsync(WebApplicationFactory<Program> factoryInstance)
    {
        var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var createResponse = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Ada Customer" });
        var conversation = await createResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();
        return (agent, conversation!.Id);
    }

    private static Task<HttpResponseMessage> ConfigureAutoReplyAsync(
        HttpClient agent, bool enabled, bool alwaysOpen, double confidenceThreshold = 0.85, int dailyLimit = 50)
        => agent.PutAsJsonAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative),
            new UpdateAiAutoReplySettingsRequest(enabled, confidenceThreshold, dailyLimit,
                alwaysOpen ? AlwaysOpen.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<BusinessHoursWindowRequest>)kv.Value) : new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>>()));

    private static Task<HttpResponseMessage> SetAiModeAsync(HttpClient agent, Guid conversationId, string mode)
        => agent.PutAsJsonAsync(new Uri($"/api/v1/conversations/{conversationId}/ai-mode", UriKind.Relative), new SetConversationAiModeRequest(mode));

    private static Task<HttpResponseMessage> SendCustomerMessageAsync(HttpClient agent, Guid conversationId, string text)
        => agent.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative),
            new AddMessageRequest { Direction = "Inbound", SenderType = "Customer", Text = text });

    private static async Task<List<MessageResponse>> GetMessagesAsync(HttpClient agent, Guid conversationId)
    {
        var response = await agent.GetAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative));
        var page = await response.Content.ReadFromJsonAsync<KeysetPageResponse<MessageResponse>>();
        return page!.Items.ToList();
    }

    private static async Task<ConversationDetailResponse> GetConversationAsync(HttpClient agent, Guid conversationId)
    {
        var response = await agent.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative));
        return (await response.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
    }

    [Fact]
    public async Task AutoReply_Enabled_WithinBusinessHours_HighConfidence_SendsAiMessage()
    {
        var (customFactory, provider) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);
        provider.SuggestionToReturn = "Yes, blue jackets are in stock in size M.";
        provider.ConfidenceToReturn = 0.9;

        Assert.Equal(HttpStatusCode.OK, (await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetAiModeAsync(agent, conversationId, "AutoReply")).StatusCode);

        var sendResponse = await SendCustomerMessageAsync(agent, conversationId, "Do you have blue jackets in size M?");
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var messages = await GetMessagesAsync(agent, conversationId);
        var aiMessage = Assert.Single(messages, m => m.SenderType == "Ai");
        Assert.Equal(provider.SuggestionToReturn, aiMessage.Text);
        Assert.Equal("Outbound", aiMessage.Direction);
    }

    [Fact]
    public async Task AutoReply_TenantSettingsDisabled_DoesNotSend()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);

        // Tenant-wide switch left at its default (disabled) — never explicitly enabled.
        Assert.Equal(HttpStatusCode.NoContent, (await SetAiModeAsync(agent, conversationId, "AutoReply")).StatusCode);
        await SendCustomerMessageAsync(agent, conversationId, "Anyone there?");

        var messages = await GetMessagesAsync(agent, conversationId);
        Assert.DoesNotContain(messages, m => m.SenderType == "Ai");
    }

    [Fact]
    public async Task AutoReply_ConversationModeDisabled_DoesNotSend()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);

        await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: true);
        // Conversation's own AiMode never changed from its Disabled default.
        await SendCustomerMessageAsync(agent, conversationId, "Anyone there?");

        var messages = await GetMessagesAsync(agent, conversationId);
        Assert.DoesNotContain(messages, m => m.SenderType == "Ai");
    }

    [Fact]
    public async Task AutoReplyWithEscalation_OutsideBusinessHours_EscalatesConversation()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);

        await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: false); // no schedule configured => never "open"
        await SetAiModeAsync(agent, conversationId, "AutoReplyWithEscalation");
        await SendCustomerMessageAsync(agent, conversationId, "Anyone there?");

        var conversation = await GetConversationAsync(agent, conversationId);
        Assert.Equal("Escalated", conversation.Status);
        Assert.DoesNotContain(await GetMessagesAsync(agent, conversationId), m => m.SenderType == "Ai");
    }

    [Fact]
    public async Task AutoReplyWithEscalation_LowConfidence_EscalatesConversation()
    {
        var (customFactory, provider) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);
        provider.ConfidenceToReturn = 0.4;

        await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: true, confidenceThreshold: 0.85);
        await SetAiModeAsync(agent, conversationId, "AutoReplyWithEscalation");
        await SendCustomerMessageAsync(agent, conversationId, "Can I get a refund?");

        var conversation = await GetConversationAsync(agent, conversationId);
        Assert.Equal("Escalated", conversation.Status);
        Assert.DoesNotContain(await GetMessagesAsync(agent, conversationId), m => m.SenderType == "Ai");
    }

    [Fact]
    public async Task AutoReplyWithEscalation_RequiresHuman_EscalatesConversation()
    {
        var (customFactory, provider) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);
        provider.ConfidenceToReturn = 0.99;
        provider.RequiresHumanToReturn = true;
        provider.EscalationReasonToReturn = "refund request";

        await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: true);
        await SetAiModeAsync(agent, conversationId, "AutoReplyWithEscalation");
        await SendCustomerMessageAsync(agent, conversationId, "I want a refund for my order.");

        var conversation = await GetConversationAsync(agent, conversationId);
        Assert.Equal("Escalated", conversation.Status);
        Assert.DoesNotContain(await GetMessagesAsync(agent, conversationId), m => m.SenderType == "Ai");
    }

    [Fact]
    public async Task AutoReply_RequiresHuman_PlainModeNeverEscalates()
    {
        // Plain AutoReply (no escalation) mode takes no extra action when the AI defers to a
        // human — the message just sits for normal human pickup, same as before this feature.
        var (customFactory, provider) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);
        provider.RequiresHumanToReturn = true;

        await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: true);
        await SetAiModeAsync(agent, conversationId, "AutoReply");
        await SendCustomerMessageAsync(agent, conversationId, "I want a refund.");

        var conversation = await GetConversationAsync(agent, conversationId);
        Assert.Equal("Open", conversation.Status);
        Assert.DoesNotContain(await GetMessagesAsync(agent, conversationId), m => m.SenderType == "Ai");
    }

    [Fact]
    public async Task AutoReply_DailyLimitReached_SkipsFurtherReplies()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);

        await ConfigureAutoReplyAsync(agent, enabled: true, alwaysOpen: true, dailyLimit: 1);
        await SetAiModeAsync(agent, conversationId, "AutoReply");

        await SendCustomerMessageAsync(agent, conversationId, "First question?");
        await SendCustomerMessageAsync(agent, conversationId, "Second question?");

        var aiMessages = (await GetMessagesAsync(agent, conversationId)).Count(m => m.SenderType == "Ai");
        Assert.Equal(1, aiMessages);
    }

    [Fact]
    public async Task SetConversationAiMode_InvalidValue_ReturnsBadRequest()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        var (agent, conversationId) = await RegisterAndCreateConversationAsync(factoryInstance);

        var response = await SetAiModeAsync(agent, conversationId, "NotARealMode");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetConversationAiMode_UnknownConversation_ReturnsNotFound()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await SetAiModeAsync(agent, Guid.NewGuid(), "AutoReply");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AutoReplySettings_GetDefault_ReturnsConservativeDefaults()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.GetAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<AiAutoReplySettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(settings!.Enabled);
        Assert.Empty(settings.BusinessHours);
    }

    [Fact]
    public async Task AutoReplySettings_Update_RoundTripsBusinessHours()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        await agent.PutAsJsonAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative),
            new UpdateAiAutoReplySettingsRequest(true, 0.7, 25,
                new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>>
                {
                    [DayOfWeek.Monday] = [new BusinessHoursWindowRequest("09:00", "17:00")],
                }));

        var response = await agent.GetAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative));
        var settings = await response.Content.ReadFromJsonAsync<AiAutoReplySettingsResponse>();

        Assert.True(settings!.Enabled);
        Assert.Equal(0.7, settings.ConfidenceThreshold);
        Assert.Equal(25, settings.DailyLimit);
        var window = Assert.Single(settings.BusinessHours[DayOfWeek.Monday]);
        Assert.Equal("09:00", window.Start);
        Assert.Equal("17:00", window.End);
    }

    [Fact]
    public async Task AutoReplySettings_InvalidBusinessHoursWindow_ReturnsBadRequest()
    {
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative),
            new UpdateAiAutoReplySettingsRequest(true, 0.85, 50,
                new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>>
                {
                    [DayOfWeek.Monday] = [new BusinessHoursWindowRequest("18:00", "09:00")], // end before start
                }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AutoReplySettings_TooManyWindowsInOneDay_ReturnsBadRequestNotServerError()
    {
        // Phase 15 hardening: BusinessHoursJson is a bounded character varying(4000) column —
        // before this guard, an oversized payload surfaced as an unhandled Postgres data-length
        // error (500) instead of a clean validation failure.
        var (customFactory, _) = WithFakeProvider();
        using var factoryInstance = customFactory;
        using var agent = factoryInstance.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var tooManyWindows = Enumerable.Range(0, 30).Select(_ => new BusinessHoursWindowRequest("09:00", "10:00")).ToList<BusinessHoursWindowRequest>();

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/ai/auto-reply-settings", UriKind.Relative),
            new UpdateAiAutoReplySettingsRequest(true, 0.85, 50,
                new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>> { [DayOfWeek.Monday] = tooManyWindows }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
