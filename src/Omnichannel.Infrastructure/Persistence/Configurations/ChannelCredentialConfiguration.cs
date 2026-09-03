using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Channels;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ChannelCredentialConfiguration : IEntityTypeConfiguration<ChannelCredential>
{
    public void Configure(EntityTypeBuilder<ChannelCredential> builder)
    {
        builder.ToTable("channel_credentials");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.EncryptedSecret).IsRequired();
        builder.HasIndex(c => c.ChannelAccountId).IsUnique();
        builder.HasIndex(c => c.TenantId);
    }
}
