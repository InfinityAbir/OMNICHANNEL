using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Knowledge;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).HasMaxLength(300).IsRequired();
        builder.Property(d => d.Content).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(d => d.TenantId);
    }
}
