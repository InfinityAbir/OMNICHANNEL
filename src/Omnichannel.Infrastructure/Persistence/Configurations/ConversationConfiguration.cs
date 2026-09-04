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
        builder.Property(c => c.LastMessagePreview).HasMaxLength(160);

        // PRD §47's exact index list — these back the inbox list query directly.
        builder.HasIndex(c => new { c.TenantId, c.Status, c.LastMessageAt });
        builder.HasIndex(c => new { c.TenantId, c.AssignedUserId, c.Status });
        builder.HasIndex(c => c.ContactId);

        // Phase 14 analytics: every summary query filters by (TenantId, CreatedAt) date range
        // first — PRD §73's "use appropriate indexes... if needed" for avoiding a full scan on
        // every dashboard request.
        builder.HasIndex(c => new { c.TenantId, c.CreatedAt });
    }
}
