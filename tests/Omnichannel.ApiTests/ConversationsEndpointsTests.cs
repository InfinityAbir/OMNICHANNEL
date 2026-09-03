using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.ApiTests;

public class ConversationsEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task FullConversationLifecycle_WorksEndToEnd()
    {
        using var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));

        // Create a conversation with a brand-new contact + initial inbound message.
        var createResponse = await client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new CreateConversationRequest
        {
            NewContactDisplayName = "Jane Customer",
            InitialMessageText = "Hi, I have a question.",
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var conversation = await createResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();
        Assert.Equal("Open", conversation!.Status);
        Assert.Equal("Jane Customer", conversation.ContactDisplayName);

        // Reply.
        var messageResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversation.Id}/messages", UriKind.Relative),
            new AddMessageRequest { Direction = "Outbound", SenderType = "Agent", Text = "Sure, how can I help?" });
        Assert.Equal(HttpStatusCode.OK, messageResponse.StatusCode);

        // Message history has both messages, newest first.
        var messagesResponse = await client.GetAsync(new Uri($"/api/v1/conversations/{conversation.Id}/messages", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        var messages = await messagesResponse.Content.ReadFromJsonAsync<KeysetPageResponse<MessageResponse>>();
        Assert.Equal(2, messages!.Items.Count);
        Assert.Equal("Sure, how can I help?", messages.Items[0].Text);

        // Tag it.
        var tagResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversation.Id}/tags", UriKind.Relative), new AddTagRequest { Name = "billing" });
        Assert.Equal(HttpStatusCode.NoContent, tagResponse.StatusCode);

        // Add an internal note.
        var noteResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversation.Id}/notes", UriKind.Relative), new AddNoteRequest { Text = "Called customer back." });
        Assert.Equal(HttpStatusCode.Created, noteResponse.StatusCode);

        // Change status to Resolved.
        var statusResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversation.Id}/status", UriKind.Relative), new ChangeStatusRequest { Status = "Resolved" });
        Assert.Equal(HttpStatusCode.NoContent, statusResponse.StatusCode);

        // Detail reflects everything.
        var detailResponse = await client.GetAsync(new Uri($"/api/v1/conversations/{conversation.Id}", UriKind.Relative));
        var detail = await detailResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();
        Assert.Equal("Resolved", detail!.Status);
        Assert.Contains(detail.Tags, t => t.Name == "billing");

        // List includes it.
        var listResponse = await client.GetAsync(new Uri("/api/v1/conversations?status=Resolved", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<KeysetPageResponse<ConversationSummaryResponse>>();
        Assert.Contains(list!.Items, c => c.Id == conversation.Id);
    }

    [Fact]
    public async Task CreateConversation_WithoutContactInfo_ReturnsValidationProblem()
    {
        using var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new CreateConversationRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assign_ThenUnassign_Works()
    {
        using var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));

        var createResponse = await client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Bob" });
        var conversation = await createResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        var userId = Guid.NewGuid();
        var assignResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversation!.Id}/assign", UriKind.Relative), new AssignConversationRequest { UserId = userId });
        Assert.Equal(HttpStatusCode.NoContent, assignResponse.StatusCode);

        var detail = await (await client.GetAsync(new Uri($"/api/v1/conversations/{conversation.Id}", UriKind.Relative)))
            .Content.ReadFromJsonAsync<ConversationDetailResponse>();
        Assert.Equal(userId, detail!.AssignedUserId);

        var unassignResponse = await client.PostAsync(new Uri($"/api/v1/conversations/{conversation.Id}/unassign", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.NoContent, unassignResponse.StatusCode);
    }

    [Fact]
    public async Task GetConversation_UnknownId_ReturnsNotFound()
    {
        using var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));

        var response = await client.GetAsync(new Uri($"/api/v1/conversations/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Contacts_CreateAndList_Works()
    {
        using var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));

        var createResponse = await client.PostAsJsonAsync(new Uri("/api/v1/contacts", UriKind.Relative), new CreateContactRequest { DisplayName = "Ada Lovelace" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await client.GetAsync(new Uri("/api/v1/contacts?search=Ada", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResponse<ContactResponse>>();
        Assert.Contains(list!.Items, c => c.DisplayName == "Ada Lovelace");
    }
}
