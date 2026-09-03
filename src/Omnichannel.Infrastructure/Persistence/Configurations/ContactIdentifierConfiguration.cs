using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Contacts;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ContactIdentifierConfiguration : IEntityTypeConfiguration<ContactIdentifier>
{
    public void Configure(EntityTypeBuilder<ContactIdentifier> builder)
    {
        builder.ToTable("contact_identifiers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Value).HasMaxLength(320).IsRequired();
        builder.Property(c => c.ChannelType).HasConversion<string>().HasMaxLength(50);

        // Lets an inbound webhook (Phase 6+) find-or-create the right contact deterministically.
        builder.HasIndex(c => new { c.TenantId, c.ChannelType, c.Value }).IsUnique();
        builder.HasIndex(c => c.ContactId);
    }
}
