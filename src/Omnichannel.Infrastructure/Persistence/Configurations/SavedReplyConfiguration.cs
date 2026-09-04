using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Automation;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class SavedReplyConfiguration : IEntityTypeConfiguration<SavedReply>
{
    public void Configure(EntityTypeBuilder<SavedReply> builder)
    {
        builder.ToTable("saved_replies");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Text).HasMaxLength(4000).IsRequired();
        builder.HasIndex(r => r.TenantId);
    }
}
