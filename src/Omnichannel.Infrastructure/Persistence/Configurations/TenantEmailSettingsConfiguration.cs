using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Email;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class TenantEmailSettingsConfiguration : IEntityTypeConfiguration<TenantEmailSettings>
{
    public void Configure(EntityTypeBuilder<TenantEmailSettings> builder)
    {
        builder.ToTable("tenant_email_settings");
        builder.HasKey(s => s.TenantId);
        builder.Property(s => s.Host).HasMaxLength(255);
        builder.Property(s => s.Username).HasMaxLength(255);
        builder.Property(s => s.FromAddress).HasMaxLength(320);
        builder.Property(s => s.FromName).HasMaxLength(200);
        builder.Ignore(s => s.IsConfigured);
    }
}
