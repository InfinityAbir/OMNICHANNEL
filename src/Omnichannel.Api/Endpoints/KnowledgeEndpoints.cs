using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Knowledge;
using Omnichannel.Contracts.Knowledge;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/knowledge/documents");

        group.MapGet("/", ListAsync).RequireAuthorization(PermissionKeys.KnowledgeRead);
        group.MapPost("/", CreateAsync).RequireAuthorization(PermissionKeys.KnowledgeManage);
        group.MapPut("/{id:guid}", ReviseAsync).RequireAuthorization(PermissionKeys.KnowledgeManage);
        group.MapDelete("/{id:guid}", ArchiveAsync).RequireAuthorization(PermissionKeys.KnowledgeManage);

        app.MapGet("/api/v1/knowledge/search", SearchAsync).RequireAuthorization(PermissionKeys.KnowledgeRead);

        return app;
    }

    private static async Task<IResult> ListAsync(KnowledgeService knowledge, CancellationToken cancellationToken)
    {
        var documents = await knowledge.ListDocumentsAsync(cancellationToken);
        return Results.Ok(documents.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] CreateKnowledgeDocumentRequest request, KnowledgeService knowledge, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.Problem(title: "Title and content are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var id = await knowledge.CreateDocumentAsync(request.Title, request.Content, cancellationToken);
        return Results.Created($"/api/v1/knowledge/documents/{id}", new { id });
    }

    private static async Task<IResult> ReviseAsync(
        Guid id, [Microsoft.AspNetCore.Mvc.FromBody] CreateKnowledgeDocumentRequest request, KnowledgeService knowledge, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.Problem(title: "Title and content are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await knowledge.ReviseDocumentAsync(id, request.Title, request.Content, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ArchiveAsync(Guid id, KnowledgeService knowledge, CancellationToken cancellationToken)
        => await knowledge.ArchiveDocumentAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> SearchAsync(
        string q, ITenantContext tenantContext, IKnowledgeRetrievalService retrieval, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.Ok(Array.Empty<KnowledgeSearchResultResponse>());
        }

        var results = await retrieval.RetrieveAsync(tenantContext.TenantId, q, topK: 5, cancellationToken);
        return Results.Ok(results.Select(r => new KnowledgeSearchResultResponse(r.DocumentId, r.DocumentTitle, r.ChunkText, r.Distance)).ToList());
    }

    private static KnowledgeDocumentResponse ToResponse(KnowledgeDocumentSummary s)
        => new(s.Id, s.Title, s.Version, s.Status, s.ChunkCount, s.UpdatedAt);
}
