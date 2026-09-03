using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");
        builder.HasKey(m => m.Id);

        // Leads with TenantId per PRD §47's indexing guidance; also enforces "one active
        // membership per user per tenant" so re-invites don't silently duplicate.
        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
    }
}
