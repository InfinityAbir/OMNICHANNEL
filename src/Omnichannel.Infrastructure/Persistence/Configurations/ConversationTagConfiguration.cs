using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ConversationTagConfiguration : IEntityTypeConfiguration<ConversationTag>
{
    public void Configure(EntityTypeBuilder<ConversationTag> builder)
    {
        builder.ToTable("conversation_tags");
        builder.HasKey(ct => ct.Id);
        builder.HasIndex(ct => new { ct.ConversationId, ct.TagId }).IsUnique();
        builder.HasIndex(ct => ct.TagId);
    }
}
