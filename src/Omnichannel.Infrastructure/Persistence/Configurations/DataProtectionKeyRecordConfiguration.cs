using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Infrastructure.Security;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class DataProtectionKeyRecordConfiguration : IEntityTypeConfiguration<DataProtectionKeyRecord>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRecord> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedOnAdd();
        builder.Property(k => k.FriendlyName).HasMaxLength(200).IsRequired();
        builder.Property(k => k.Xml).IsRequired();
    }
}
