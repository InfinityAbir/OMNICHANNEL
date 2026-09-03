using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Conversations;

public sealed class Tag : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private Tag()
    {
    }

    public static Tag Create(Guid tenantId, string name, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        return new Tag
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            CreatedAt = now,
        };
    }
}
