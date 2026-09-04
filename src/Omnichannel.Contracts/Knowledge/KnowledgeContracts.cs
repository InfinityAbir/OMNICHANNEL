namespace Omnichannel.Contracts.Knowledge;

public sealed record CreateKnowledgeDocumentRequest(string Title, string Content);

public sealed record KnowledgeDocumentResponse(Guid Id, string Title, int Version, string Status, int ChunkCount, DateTimeOffset UpdatedAt);

public sealed record KnowledgeSearchResultResponse(Guid DocumentId, string DocumentTitle, string ChunkText, double Distance);
