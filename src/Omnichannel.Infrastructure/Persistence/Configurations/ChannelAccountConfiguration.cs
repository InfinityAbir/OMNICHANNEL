using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Channels;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ChannelAccountConfiguration : IEntityTypeConfiguration<ChannelAccount>
{
    public void Configure(EntityTypeBuilder<ChannelAccount> builder)
    {
        builder.ToTable("channel_accounts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.ExternalAccountId).HasMaxLength(200);
        builder.HasIndex(c => c.TenantId);

        // Providers assign this id globally (not per-tenant), and it's the only thing an
        // inbound webhook carries to resolve which account it belongs to — must be unique across
        // all tenants for a given channel type. Postgres treats each NULL as distinct, so the
        // many accounts with no external id yet (Manual, WebsiteChat, not-yet-connected) never
        // collide with each other.
        builder.HasIndex(c => new { c.Type, c.ExternalAccountId }).IsUnique();
    }
}
