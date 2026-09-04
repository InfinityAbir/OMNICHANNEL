using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Security;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class TenantSecretConfiguration : IEntityTypeConfiguration<TenantSecret>
{
    public void Configure(EntityTypeBuilder<TenantSecret> builder)
    {
        builder.ToTable("tenant_secrets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Purpose).HasMaxLength(100).IsRequired();
        builder.Property(s => s.EncryptedValue).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.Purpose }).IsUnique();
    }
}
