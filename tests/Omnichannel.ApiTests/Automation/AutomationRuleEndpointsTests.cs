using System.Net;
using System.Net.Http.Json;
using Omnichannel.Contracts.Automation;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Contracts.Notifications;

namespace Omnichannel.ApiTests.Automation;

/// <summary>Phase 13 (PRD §72): keyword-triggered automation rules covering both "escalation rules" and "basic automation" from a single bounded rule concept.</summary>
public class AutomationRuleEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static Task<HttpResponseMessage> CreateRuleAsync(
        HttpClient agent, string keyword, string? tag = null, string? priority = null, bool escalate = false, string? name = null)
        => agent.PostAsJsonAsync(new Uri("/api/v1/automation-rules", UriKind.Relative),
            new CreateAutomationRuleRequest { Name = name, Keyword = keyword, ApplyTagName = tag, SetPriority = priority, Escalate = escalate });

    private static Task<HttpResponseMessage> SendCustomerMessageAsync(HttpClient agent, Guid conversationId, string text)
        => agent.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative),
            new AddMessageRequest { Direction = "Inbound", SenderType = "Customer", Text = text });

    private static async Task<ConversationDetailResponse> GetConversationAsync(HttpClient agent, Guid conversationId)
    {
        var response = await agent.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative));
        return (await response.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
    }

    [Fact]
    public async Task CreateRule_KeywordMatch_AppliesTagAndPriority()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var createRuleResponse = await CreateRuleAsync(agent, "billing", tag: "Billing", priority: "High");
        Assert.Equal(HttpStatusCode.Created, createRuleResponse.StatusCode);

        var createConversation = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer" });
        var conversation = await createConversation.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await SendCustomerMessageAsync(agent, conversation!.Id, "I have a billing question about my invoice.");

        var updated = await GetConversationAsync(agent, conversation.Id);
        Assert.Equal("High", updated.Priority);
        Assert.Contains(updated.Tags, t => t.Name == "Billing");
    }

    [Fact]
    public async Task CreateRule_NoMatch_TakesNoAction()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        await CreateRuleAsync(agent, "billing", tag: "Billing");

        var createConversation = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer" });
        var conversation = await createConversation.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await SendCustomerMessageAsync(agent, conversation!.Id, "What are your store hours?");

        var updated = await GetConversationAsync(agent, conversation.Id);
        Assert.DoesNotContain(updated.Tags, t => t.Name == "Billing");
        Assert.Equal("Normal", updated.Priority);
    }

    [Fact]
    public async Task CreateRule_Escalate_EscalatesConversationAndNotifiesOwner()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        await CreateRuleAsync(agent, "refund", escalate: true, name: "Refund escalation");

        var createConversation = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer" });
        var conversation = await createConversation.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await SendCustomerMessageAsync(agent, conversation!.Id, "I need a refund for my broken item.");

        var updated = await GetConversationAsync(agent, conversation.Id);
        Assert.Equal("Escalated", updated.Status);

        var notifications = await agent.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative));
        var list = await notifications.Content.ReadFromJsonAsync<List<NotificationResponse>>();
        Assert.Contains(list!, n => n.Type == "conversation.escalated" && n.ConversationId == conversation.Id);
    }

    [Fact]
    public async Task DisabledRule_NeverMatches()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var createResponse = await CreateRuleAsync(agent, "refund", escalate: true);
        var rule = await createResponse.Content.ReadFromJsonAsync<AutomationRuleResponse>();

        var disableResponse = await agent.PutAsJsonAsync(new Uri($"/api/v1/automation-rules/{rule!.Id}/enabled", UriKind.Relative),
            new SetAutomationRuleEnabledRequest { Enabled = false });
        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        var createConversation = await agent.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Customer" });
        var conversation = await createConversation.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        await SendCustomerMessageAsync(agent, conversation!.Id, "I need a refund.");

        var updated = await GetConversationAsync(agent, conversation.Id);
        Assert.Equal("Open", updated.Status);
    }

    [Fact]
    public async Task DeleteRule_RemovesIt()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var createResponse = await CreateRuleAsync(agent, "refund", escalate: true);
        var rule = await createResponse.Content.ReadFromJsonAsync<AutomationRuleResponse>();

        var deleteResponse = await agent.DeleteAsync(new Uri($"/api/v1/automation-rules/{rule!.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await agent.GetAsync(new Uri("/api/v1/automation-rules", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<List<AutomationRuleResponse>>();
        Assert.DoesNotContain(list!, r => r.Id == rule.Id);
    }

    [Fact]
    public async Task CreateRule_NoActions_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await CreateRuleAsync(agent, "refund");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SavedReplies_FullCrudLifecycle()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var createResponse = await agent.PostAsJsonAsync(new Uri("/api/v1/saved-replies", UriKind.Relative),
            new SavedReplyRequest { Title = "Welcome", Text = "Hi! How can I help?" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var reply = await createResponse.Content.ReadFromJsonAsync<SavedReplyResponse>();

        var updateResponse = await agent.PutAsJsonAsync(new Uri($"/api/v1/saved-replies/{reply!.Id}", UriKind.Relative),
            new SavedReplyRequest { Title = "Welcome!", Text = "Hi there! How can I help you today?" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var listResponse = await agent.GetAsync(new Uri("/api/v1/saved-replies", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<List<SavedReplyResponse>>();
        Assert.Contains(list!, r => r.Title == "Welcome!");

        var deleteResponse = await agent.DeleteAsync(new Uri($"/api/v1/saved-replies/{reply.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task BusinessHours_GetDefault_IsUnconfigured()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.GetAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative));
        var hours = await response.Content.ReadFromJsonAsync<TenantBusinessHoursResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(hours!.BusinessHours);
        Assert.Empty(hours.Holidays);
    }

    [Fact]
    public async Task BusinessHours_Update_RoundTripsScheduleAndHolidays()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative),
            new UpdateTenantBusinessHoursRequest(
                new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>> { [DayOfWeek.Monday] = [new BusinessHoursWindowRequest("09:00", "17:00")] },
                ["2026-12-25"]));

        var response = await agent.GetAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative));
        var hours = await response.Content.ReadFromJsonAsync<TenantBusinessHoursResponse>();

        Assert.Single(hours!.BusinessHours[DayOfWeek.Monday]);
        Assert.Contains("2026-12-25", hours.Holidays);
    }

    [Fact]
    public async Task BusinessHours_InvalidWindow_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative),
            new UpdateTenantBusinessHoursRequest(
                new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>> { [DayOfWeek.Monday] = [new BusinessHoursWindowRequest("18:00", "09:00")] },
                null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BusinessHours_TooManyHolidays_ReturnsBadRequestNotServerError()
    {
        // Phase 15 hardening: HolidaysJson is a bounded character varying(4000) column — before
        // this guard, an oversized list surfaced as an unhandled Postgres data-length error (500)
        // instead of a clean validation failure.
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var tooManyHolidays = Enumerable.Range(0, 400)
            .Select(i => new DateOnly(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative),
            new UpdateTenantBusinessHoursRequest(null, tooManyHolidays));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BusinessHours_TooManyWindowsInOneDay_ReturnsBadRequest()
    {
        using var agent = factory.CreateClient();
        agent.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(agent));

        var tooManyWindows = Enumerable.Range(0, 30).Select(_ => new BusinessHoursWindowRequest("09:00", "10:00")).ToList<BusinessHoursWindowRequest>();

        var response = await agent.PutAsJsonAsync(new Uri("/api/v1/tenant/business-hours", UriKind.Relative),
            new UpdateTenantBusinessHoursRequest(
                new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>> { [DayOfWeek.Monday] = tooManyWindows },
                null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
