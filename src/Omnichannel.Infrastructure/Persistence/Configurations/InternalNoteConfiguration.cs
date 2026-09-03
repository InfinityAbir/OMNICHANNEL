using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class InternalNoteConfiguration : IEntityTypeConfiguration<InternalNote>
{
    public void Configure(EntityTypeBuilder<InternalNote> builder)
    {
        builder.ToTable("internal_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Text).HasMaxLength(4000).IsRequired();
        builder.HasIndex(n => new { n.ConversationId, n.CreatedAt });
    }
}
