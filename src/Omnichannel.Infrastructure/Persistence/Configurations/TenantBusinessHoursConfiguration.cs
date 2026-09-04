using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Automation;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class TenantBusinessHoursConfiguration : IEntityTypeConfiguration<TenantBusinessHours>
{
    public void Configure(EntityTypeBuilder<TenantBusinessHours> builder)
    {
        builder.ToTable("tenant_business_hours");
        builder.HasKey(b => b.TenantId);
        builder.Property(b => b.BusinessHoursJson).HasMaxLength(4000);
        builder.Property(b => b.HolidaysJson).HasMaxLength(4000).IsRequired();
    }
}
