namespace Omnichannel.Infrastructure.Channels;

/// <summary>
/// Platform-level Instagram Messaging API configuration — same "one Meta App, many connected
/// accounts" shape as <see cref="WhatsAppOptions"/> (ADR-0016/0017/0018), kept as its own
/// distinct App Secret/Verify Token rather than assumed shared with WhatsApp's: Meta's Instagram
/// Login apps are commonly configured separately from Business/WhatsApp apps in the developer
/// dashboard, and reusing WhatsApp's values without confirming they're actually the same app
/// would be an unverified assumption, not a researched one.
/// </summary>
public sealed class InstagramOptions
{
    public const string SectionName = "Instagram";

    public string AppSecret { get; set; } = string.Empty;

    public string VerifyToken { get; set; } = string.Empty;

    public string GraphApiVersion { get; set; } = "v25.0";

    /// <summary>The "Instagram API with Instagram Login" model uses graph.instagram.com directly (IG_ID, Instagram User access token) — not graph.facebook.com's Page-token-based legacy model. See ADR-0018.</summary>
    public string GraphApiBaseUrl { get; set; } = "https://graph.instagram.com";
}
