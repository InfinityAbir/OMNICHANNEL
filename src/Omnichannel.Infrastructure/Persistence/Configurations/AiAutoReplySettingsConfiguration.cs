using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class AiAutoReplySettingsConfiguration : IEntityTypeConfiguration<AiAutoReplySettings>
{
    public void Configure(EntityTypeBuilder<AiAutoReplySettings> builder)
    {
        builder.ToTable("ai_auto_reply_settings");
        builder.HasKey(s => s.TenantId);
        builder.Property(s => s.Enabled).IsRequired();
        builder.Property(s => s.ConfidenceThreshold).IsRequired();
        builder.Property(s => s.DailyLimit).IsRequired();
        builder.Property(s => s.BusinessHoursJson).HasMaxLength(4000);
    }
}
