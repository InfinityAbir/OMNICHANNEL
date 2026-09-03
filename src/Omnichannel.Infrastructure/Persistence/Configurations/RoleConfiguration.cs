using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SystemRole).HasConversion<string>().HasMaxLength(50);
        builder.HasIndex(r => r.SystemRole).IsUnique();

        // Native Postgres text[] — no join table needed for a fixed, seeded permission set.
        builder.Property(r => r.Permissions)
            .HasColumnName("permissions")
            .HasColumnType("text[]")
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode(StringComparison.Ordinal))),
                v => v.ToList()));
    }
}
