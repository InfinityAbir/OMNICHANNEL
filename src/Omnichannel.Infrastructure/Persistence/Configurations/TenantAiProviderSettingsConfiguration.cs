using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class TenantAiProviderSettingsConfiguration : IEntityTypeConfiguration<TenantAiProviderSettings>
{
    public void Configure(EntityTypeBuilder<TenantAiProviderSettings> builder)
    {
        builder.ToTable("tenant_ai_provider_settings");
        builder.HasKey(s => s.TenantId);
        builder.Property(s => s.ProviderKind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.BaseUrl).HasMaxLength(500);
        builder.Property(s => s.Model).HasMaxLength(200).IsRequired();
    }
}
