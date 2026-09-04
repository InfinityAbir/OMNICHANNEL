using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Security;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class JwtSigningKeyConfiguration : IEntityTypeConfiguration<JwtSigningKey>
{
    public void Configure(EntityTypeBuilder<JwtSigningKey> builder)
    {
        builder.ToTable("jwt_signing_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.EncryptedKeyMaterial).IsRequired();

        // At most one primary key at a time — a concurrent bootstrap/rotation race surfaces as a
        // constraint violation (handled by the store), not two simultaneous "current" keys.
        builder.HasIndex(k => k.IsPrimary).IsUnique().HasFilter("\"IsPrimary\" = true");
    }
}
