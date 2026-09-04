using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Knowledge;

/// <summary>
/// One retrievable slice of a <see cref="KnowledgeDocument"/> plus its embedding vector. Kept as
/// a plain <c>float[]</c> here (not the Pgvector NuGet package's own type) — Domain has zero
/// external dependencies by design; Infrastructure's EF configuration converts to/from the
/// database's actual <c>vector</c> column type at the boundary.
/// </summary>
public sealed class KnowledgeChunk : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ChunkIndex { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public float[] Embedding { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    private KnowledgeChunk()
    {
    }

    public static KnowledgeChunk Create(Guid tenantId, Guid documentId, int chunkIndex, string text, float[] embedding, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Text = text,
            Embedding = embedding,
            CreatedAt = now,
        };
}
