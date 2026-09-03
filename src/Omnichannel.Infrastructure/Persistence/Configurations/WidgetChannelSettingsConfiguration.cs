using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Widget;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class WidgetChannelSettingsConfiguration : IEntityTypeConfiguration<WidgetChannelSettings>
{
    public void Configure(EntityTypeBuilder<WidgetChannelSettings> builder)
    {
        builder.ToTable("widget_channel_settings");
        builder.HasKey(w => w.TenantId);
        builder.Property(w => w.AllowedOriginsJson).HasColumnName("AllowedOriginsJson").HasMaxLength(4000).IsRequired();
        builder.Property(w => w.ChannelAccountId).IsRequired();
        builder.Property(w => w.Enabled).IsRequired();
    }
}
