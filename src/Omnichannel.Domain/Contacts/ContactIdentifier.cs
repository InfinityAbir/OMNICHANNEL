using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Contacts;

/// <summary>
/// A channel-specific handle for a contact (phone number, WhatsApp id, Instagram handle, ...).
/// A contact can have several — one per channel they've messaged from. Unique per
/// (TenantId, ChannelType, Value) so inbound webhooks can find-or-create deterministically.
/// </summary>
public sealed class ContactIdentifier : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ContactId { get; private set; }
    public ChannelType ChannelType { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private ContactIdentifier()
    {
    }

    public static ContactIdentifier Create(Guid tenantId, Guid contactId, ChannelType channelType, string value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier value is required.", nameof(value));
        }

        return new ContactIdentifier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            ChannelType = channelType,
            Value = value.Trim(),
            CreatedAt = now,
        };
    }
}
