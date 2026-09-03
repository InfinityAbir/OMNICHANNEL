using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Channels;

public enum ChannelAccountStatus
{
    Active = 0,
    Disabled = 1,
}

/// <summary>
/// A connected channel instance for a tenant (e.g. "our website chat widget", later "our
/// WhatsApp Business number"). No credentials/webhook config here — that's Phase 6+
/// (ChannelCredential, WebhookSubscription per PRD §14). Phase 2 only needs enough to give
/// Conversation a real ChannelAccountId to point at.
/// </summary>
public sealed class ChannelAccount : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public ChannelType Type { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public ChannelAccountStatus Status { get; private set; } = ChannelAccountStatus.Active;

    /// <summary>
    /// The provider's own identifier for this account (e.g. a WhatsApp Business
    /// phone_number_id, an Instagram/Messenger page id) — null for channels with no such concept
    /// (Manual, WebsiteChat). Set once the business connects the account (Phase 7+). Inbound
    /// webhooks carry only this id, never a tenant id, so it's the sole key the webhook pipeline
    /// uses to resolve which tenant/account a provider event belongs to (see
    /// WebhookIngestionService) — unique per channel type across all tenants, since providers
    /// assign it globally.
    /// </summary>
    public string? ExternalAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ChannelAccount()
    {
    }

    public static ChannelAccount Create(Guid tenantId, ChannelType type, string displayName, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = type,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? type.ToString() : displayName.Trim(),
            Status = ChannelAccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void SetExternalAccountId(string externalAccountId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            throw new ArgumentException("External account id is required.", nameof(externalAccountId));
        }

        ExternalAccountId = externalAccountId.Trim();
        UpdatedAt = now;
    }
}
