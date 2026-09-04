using System.Net;
using System.Net.Http.Json;
using Omnichannel.Contracts.Knowledge;

namespace Omnichannel.ApiTests.Knowledge;

public class KnowledgeEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private async Task<HttpClient> RegisterAgentAsync()
    {
        var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAndGetAccessTokenAsync(client));
        return client;
    }

    [Fact]
    public async Task CreateDocument_ThenSearch_FindsRelevantChunk()
    {
        using var agent = await RegisterAgentAsync();

        var create = await agent.PostAsJsonAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Return Policy", "Customers may return unused items within 30 days of purchase for a full refund."));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await (await agent.GetAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative)))
            .Content.ReadFromJsonAsync<List<KnowledgeDocumentResponse>>();
        var doc = Assert.Single(list!);
        Assert.Equal("Return Policy", doc.Title);
        Assert.Equal(1, doc.Version);
        Assert.True(doc.ChunkCount > 0);

        var search = await agent.GetAsync(new Uri("/api/v1/knowledge/search?q=" + Uri.EscapeDataString("how many days can I return something"), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var results = await search.Content.ReadFromJsonAsync<List<KnowledgeSearchResultResponse>>();
        Assert.NotEmpty(results!);
        Assert.Equal("Return Policy", results![0].DocumentTitle);
    }

    [Fact]
    public async Task ReviseDocument_IncrementsVersionAndReindexes()
    {
        using var agent = await RegisterAgentAsync();

        var create = await agent.PostAsJsonAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Shipping", "We ship within 3 business days."));
        var createdId = (await create.Content.ReadFromJsonAsync<CreatedDocumentId>())!.Id;

        var revise = await agent.PutAsJsonAsync(new Uri($"/api/v1/knowledge/documents/{createdId}", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Shipping", "We now ship within 1 business day nationwide."));
        Assert.Equal(HttpStatusCode.NoContent, revise.StatusCode);

        var list = await (await agent.GetAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative)))
            .Content.ReadFromJsonAsync<List<KnowledgeDocumentResponse>>();
        var doc = Assert.Single(list!);
        Assert.Equal(2, doc.Version);
    }

    [Fact]
    public async Task ArchiveDocument_RemovesItFromSearchResults()
    {
        using var agent = await RegisterAgentAsync();

        var create = await agent.PostAsJsonAsync(new Uri("/api/v1/knowledge/documents", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("Warranty", "All products carry a one year warranty against defects."));
        var createdId = (await create.Content.ReadFromJsonAsync<CreatedDocumentId>())!.Id;

        var archive = await agent.DeleteAsync(new Uri($"/api/v1/knowledge/documents/{createdId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        var search = await agent.GetAsync(new Uri("/api/v1/knowledge/search?q=" + Uri.EscapeDataString("warranty defects"), UriKind.Relative));
        var results = await search.Content.ReadFromJsonAsync<List<KnowledgeSearchResultResponse>>();
        Assert.Empty(results!);
    }

    [Fact]
    public async Task ReviseUnknownDocument_ReturnsNotFound()
    {
        using var agent = await RegisterAgentAsync();

        var response = await agent.PutAsJsonAsync(new Uri($"/api/v1/knowledge/documents/{Guid.NewGuid()}", UriKind.Relative),
            new CreateKnowledgeDocumentRequest("X", "Y"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreatedDocumentId(Guid Id);
}
