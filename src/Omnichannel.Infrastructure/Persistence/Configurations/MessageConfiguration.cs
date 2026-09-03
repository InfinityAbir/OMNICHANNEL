using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.SenderType).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ContentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.DeliveryStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Text).HasMaxLength(8000).IsRequired();

        // PRD §47: conversation history query.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        builder.HasIndex(m => new { m.TenantId, m.CreatedAt });

        // PRD §17 idempotency shape — Postgres treats each NULL as distinct, so manually
        // created messages (ExternalMessageId null) never collide with each other.
        builder.HasIndex(m => new { m.ChannelAccountId, m.ExternalMessageId }).IsUnique();
    }
}
