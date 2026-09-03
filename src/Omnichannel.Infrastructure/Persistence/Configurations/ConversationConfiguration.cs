using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.AiMode).HasConversion<string>().HasMaxLength(50);

        // PRD §47's exact index list — these back the inbox list query directly.
        builder.HasIndex(c => new { c.TenantId, c.Status, c.LastMessageAt });
        builder.HasIndex(c => new { c.TenantId, c.AssignedUserId, c.Status });
        builder.HasIndex(c => c.ContactId);
    }
}
