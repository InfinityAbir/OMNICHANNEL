namespace Omnichannel.Infrastructure.Channels;

/// <summary>
/// Platform-level WhatsApp Cloud API configuration — one Meta App shared across every tenant
/// (Tech Provider model), not per-tenant. <see cref="AppSecret"/> is what signs every inbound
/// webhook regardless of which tenant's phone number it's for (Meta signs at the App level, not
/// per WABA), and <see cref="VerifyToken"/> is the string configured in the Meta App Dashboard's
/// webhook subscription for the one-time GET handshake. Per-tenant state (the connected
/// phone_number_id and its access token) lives in ChannelAccount/ChannelCredential instead — see
/// ADR-0017.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string AppSecret { get; set; } = string.Empty;

    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>Graph API version segment (e.g. "v23.0") — reviewed periodically as Meta deprecates old versions; not hardcoded in the adapter.</summary>
    public string GraphApiVersion { get; set; } = "v23.0";

    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";
}
