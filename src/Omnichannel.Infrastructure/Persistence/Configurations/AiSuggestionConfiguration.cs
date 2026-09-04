using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Omnichannel.Domain.Ai;

namespace Omnichannel.Infrastructure.Persistence.Configurations;

public sealed class AiSuggestionConfiguration : IEntityTypeConfiguration<AiSuggestion>
{
    public void Configure(EntityTypeBuilder<AiSuggestion> builder)
    {
        builder.ToTable("ai_suggestions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SuggestedText).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.Model).HasMaxLength(200).IsRequired();

        // Usage-limit counting (AiUsageLimiter) queries by (TenantId, CreatedAt) — same access
        // pattern as every other list-by-date-range query in this codebase.
        builder.HasIndex(s => new { s.TenantId, s.CreatedAt });
        builder.HasIndex(s => new { s.ConversationId, s.CreatedAt });
    }
}
