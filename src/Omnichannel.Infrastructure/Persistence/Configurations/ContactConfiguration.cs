using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Contacts;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();

        // PRD §47: index tenant-scoped list/search access; DisplayName included for the
        // Phase 2 "search foundations" ILIKE-based lookup.
        builder.HasIndex(c => new { c.TenantId, c.DisplayName });
    }
}
