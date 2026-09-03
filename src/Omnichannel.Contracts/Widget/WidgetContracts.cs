namespace Omnichannel.Contracts.Widget;

/// <summary>
/// Claim names carried by the short-lived website-chat widget session token (audience "widget").
/// Kept distinct from the agent token claims so a widget token can never authorize agent
/// endpoints and vice versa.
/// </summary>
public static class WidgetClaimNames
{
    public const string TenantId = "tenant_id";
    public const string VisitorId = "visitor_id";
    public const string SessionId = "widget_session_id";
    public const string ConversationId = "conversation_id";
}

public sealed record WidgetSessionOpenRequest(string? VisitorKey, string? VisitorName);

public sealed record WidgetSessionResponse(
    string SessionToken,
    Guid SessionId,
    Guid ConversationId,
    Guid ChannelAccountId,
    string ConnectionUrl,
    DateTimeOffset ExpiresAt);

public sealed record WidgetSendRequest(Guid ConversationId, string Text);

public sealed record WidgetMessageResponse(
    Guid MessageId,
    string Direction,
    string SenderType,
    string ContentType,
    string Text,
    DateTimeOffset CreatedAt,
    string DeliveryStatus);

public sealed record WidgetThreadResponse(
    Guid ConversationId,
    IReadOnlyCollection<WidgetMessageResponse> Messages);

public sealed record WidgetSettingsResponse(
    Guid ChannelAccountId,
    bool Enabled,
    IReadOnlyCollection<string> AllowedOrigins,
    string Slug,
    string EmbedSnippet);

public sealed record WidgetOriginsUpdateRequest(IReadOnlyCollection<string> Origins);
