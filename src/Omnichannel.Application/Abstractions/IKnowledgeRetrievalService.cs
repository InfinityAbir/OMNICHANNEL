namespace Omnichannel.Application.Abstractions;

public sealed record KnowledgeRetrievalResult(Guid DocumentId, string DocumentTitle, string ChunkText, double Distance);

/// <summary>
/// Tenant-scoped nearest-neighbor lookup over indexed knowledge chunks. Implemented in
/// Infrastructure (needs the database's native vector similarity operator) but declared here so
/// Application code (AiSuggestionService) never depends on pgvector specifically.
/// </summary>
public interface IKnowledgeRetrievalService
{
    Task<IReadOnlyList<KnowledgeRetrievalResult>> RetrieveAsync(Guid tenantId, string query, int topK, CancellationToken cancellationToken);
}
