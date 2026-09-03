namespace Omnichannel.Infrastructure.Channels;

/// <summary>Platform-level Facebook Messenger configuration — same shape as WhatsApp/Instagram (ADR-0016/17/18/19): one Meta App's webhook subscription, own App Secret/Verify Token (Messenger apps are commonly configured separately from WhatsApp/Instagram Login apps).</summary>
public sealed class MessengerOptions
{
    public const string SectionName = "Messenger";

    public string AppSecret { get; set; } = string.Empty;

    public string VerifyToken { get; set; } = string.Empty;

    public string GraphApiVersion { get; set; } = "v23.0";

    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";
}
