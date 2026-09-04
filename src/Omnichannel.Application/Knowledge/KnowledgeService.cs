using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Application.Audit;
using Omnichannel.Domain.Knowledge;

namespace Omnichannel.Application.Knowledge;

public sealed record KnowledgeDocumentSummary(Guid Id, string Title, int Version, string Status, int ChunkCount, DateTimeOffset UpdatedAt);

/// <summary>
/// Owns the document -> chunks -> embeddings pipeline (PRD §70: text extraction — trivial here
/// since input is already plain text; chunking; embedding; versioning; re-indexing). Every write
/// re-derives the chunk set from scratch rather than diffing — simpler and correct, and knowledge
/// documents are edited rarely enough that re-embedding the whole document isn't a real cost
/// concern.
/// </summary>
public sealed class KnowledgeService(IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider, AuditService audit, IEmbeddingProvider embeddings)
{
    private const int ChunkSize = 800;
    private const int ChunkOverlap = 100;

    public async Task<Guid> CreateDocumentAsync(string title, string content, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var document = KnowledgeDocument.Create(tenantContext.TenantId, title, content, now);
        db.KnowledgeDocuments.Add(document);

        await IndexChunksAsync(document, cancellationToken);

        audit.Record(tenantContext.TenantId, tenantContext.UserId, "knowledge.document.created", nameof(KnowledgeDocument), document.Id,
            new { title = document.Title });
        await db.SaveChangesAsync(cancellationToken);

        return document.Id;
    }

    public async Task<bool> ReviseDocumentAsync(Guid documentId, string title, string content, CancellationToken cancellationToken)
    {
        var document = await db.KnowledgeDocuments.SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return false;
        }

        document.ReviseContent(title, content, timeProvider.GetUtcNow());

        var existingChunks = await db.KnowledgeChunks.Where(c => c.DocumentId == documentId).ToListAsync(cancellationToken);
        db.KnowledgeChunks.RemoveRange(existingChunks);

        await IndexChunksAsync(document, cancellationToken);

        audit.Record(tenantContext.TenantId, tenantContext.UserId, "knowledge.document.revised", nameof(KnowledgeDocument), document.Id,
            new { title = document.Title, version = document.Version });
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> ArchiveDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.KnowledgeDocuments.SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return false;
        }

        document.Archive(timeProvider.GetUtcNow());
        audit.Record(tenantContext.TenantId, tenantContext.UserId, "knowledge.document.archived", nameof(KnowledgeDocument), document.Id);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<KnowledgeDocumentSummary>> ListDocumentsAsync(CancellationToken cancellationToken)
    {
        var chunkCounts = await db.KnowledgeChunks
            .GroupBy(c => c.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, cancellationToken);

        var documents = await db.KnowledgeDocuments.OrderByDescending(d => d.UpdatedAt).ToListAsync(cancellationToken);

        return documents
            .Select(d => new KnowledgeDocumentSummary(d.Id, d.Title, d.Version, d.Status.ToString(), chunkCounts.GetValueOrDefault(d.Id, 0), d.UpdatedAt))
            .ToList();
    }

    private async Task IndexChunksAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var chunks = Chunk(document.Content);

        for (var i = 0; i < chunks.Count; i++)
        {
            var embedding = await embeddings.EmbedAsync(chunks[i], cancellationToken);
            db.KnowledgeChunks.Add(KnowledgeChunk.Create(document.TenantId, document.Id, i, chunks[i], embedding, now));
        }
    }

    /// <summary>Fixed-size character chunking with overlap — simple, deterministic, and good enough for the plain-text documents this phase supports (no semantic/sentence-boundary chunking yet).</summary>
    private static List<string> Chunk(string content)
    {
        var chunks = new List<string>();
        if (content.Length <= ChunkSize)
        {
            chunks.Add(content);
            return chunks;
        }

        var start = 0;
        while (start < content.Length)
        {
            var length = Math.Min(ChunkSize, content.Length - start);
            chunks.Add(content.Substring(start, length));
            if (start + length >= content.Length)
            {
                break;
            }

            start += ChunkSize - ChunkOverlap;
        }

        return chunks;
    }
}
