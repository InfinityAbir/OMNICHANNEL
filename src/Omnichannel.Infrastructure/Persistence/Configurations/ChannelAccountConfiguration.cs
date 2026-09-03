using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Channels;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ChannelAccountConfiguration : IEntityTypeConfiguration<ChannelAccount>
{
    public void Configure(EntityTypeBuilder<ChannelAccount> builder)
    {
        builder.ToTable("channel_accounts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(50);
        builder.HasIndex(c => c.TenantId);
    }
}
