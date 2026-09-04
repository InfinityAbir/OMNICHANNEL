using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Persistence;
using Pgvector;

namespace Omnichannel.Infrastructure.Knowledge;

/// <summary>
/// Nearest-neighbor lookup via pgvector's cosine-distance operator (<c>&lt;=&gt;</c>). Raw SQL,
/// not LINQ: KnowledgeChunk.Embedding is a plain <c>float[]</c> in Domain (kept framework-free —
/// no Pgvector type there), and EF's LINQ-to-SQL vector-distance translation needs the model
/// property itself typed as <c>Vector</c> to recognize the operator pattern. A raw query with an
/// explicit tenant filter is simpler and just as correct — and, like every other exception to the
/// automatic EF tenant filter in this codebase (ADR-0016's third-exception precedent), it's
/// documented here rather than silently relied upon. Reuses AppDbContext's own connection (same
/// one EF already opened for the request) rather than opening a second one.
/// </summary>
public sealed class PgVectorKnowledgeRetrievalService(AppDbContext db, IEmbeddingProvider embeddings) : IKnowledgeRetrievalService
{
    public async Task<IReadOnlyList<KnowledgeRetrievalResult>> RetrieveAsync(Guid tenantId, string query, int topK, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || topK <= 0)
        {
            return [];
        }

        var queryVector = new Vector(await embeddings.EmbedAsync(query, cancellationToken));
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c."DocumentId", d."Title", c."Text", c."Embedding" <=> $1 AS distance
                FROM knowledge_chunks c
                JOIN knowledge_documents d ON d."Id" = c."DocumentId"
                WHERE c."TenantId" = $2 AND d."TenantId" = $2 AND d."Status" = 'Active'
                ORDER BY distance ASC
                LIMIT $3
                """;
            command.Parameters.Add(new NpgsqlParameter { Value = queryVector });
            command.Parameters.Add(new NpgsqlParameter { Value = tenantId });
            command.Parameters.Add(new NpgsqlParameter { Value = topK });

            var results = new List<KnowledgeRetrievalResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new KnowledgeRetrievalResult(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3)));
            }

            return results;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
