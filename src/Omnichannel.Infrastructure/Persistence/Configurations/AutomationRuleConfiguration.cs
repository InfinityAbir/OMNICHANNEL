using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Automation;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> builder)
    {
        builder.ToTable("automation_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Keyword).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ApplyTagName).HasMaxLength(100);
        builder.Property(r => r.SetPriority).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => new { r.TenantId, r.Enabled });
    }
}
