using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Knowledge;
using Pgvector;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    // Must match IEmbeddingProvider.Dimensions of whichever provider is registered — a fixed
    // pgvector column width, not inferred at runtime. Changing embedding dimensionality later
    // needs a migration (drop + recreate the column and re-index every chunk), documented here so
    // that requirement isn't a surprise.
    private const int EmbeddingDimensions = 256;

    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Text).IsRequired();

        // Domain keeps this a plain float[] (framework-free); Infrastructure converts to/from
        // Pgvector's Vector type only at the storage boundary. Explicit value comparer — without
        // one, EF compares float[] by reference identity for change tracking, which would mark
        // every tracked chunk as "modified" (or never notice a real change) rather than comparing
        // the actual vector contents.
        builder.Property(c => c.Embedding)
            .HasConversion(
                v => new Vector(v),
                v => v.ToArray(),
                new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
                    v => v.ToArray()))
            .HasColumnType($"vector({EmbeddingDimensions})");

        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => c.DocumentId);
    }
}
