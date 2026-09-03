using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Widget;

/// <summary>
/// Per-tenant configuration for the website-chat widget (PRD §64). Holds the allowlist of
/// origins and the active WebsiteChat channel account id. Origin validation (PRD §64 security)
/// reads this allowlist before issuing a widget session for an embed, so a foreign site can
/// never impersonate a tenant's widget.
/// </summary>
public sealed class WidgetChannelSettings : ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ChannelAccountId { get; private set; }
    public bool Enabled { get; private set; } = true;
    public string AllowedOriginsJson { get; private set; } = "[]";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WidgetChannelSettings()
    {
    }

    public static WidgetChannelSettings Create(Guid tenantId, Guid channelAccountId, IReadOnlyCollection<string> allowedOrigins, DateTimeOffset now)
        => new()
        {
            TenantId = tenantId,
            ChannelAccountId = channelAccountId,
            Enabled = true,
            AllowedOriginsJson = Serialize(allowedOrigins),
            CreatedAt = now,
            UpdatedAt = now,
        };

    public IReadOnlyCollection<string> GetAllowedOrigins()
        => Deserialize(AllowedOriginsJson);

    public void SetAllowedOrigins(IReadOnlyCollection<string> origins, DateTimeOffset now)
    {
        AllowedOriginsJson = Serialize(origins);
        UpdatedAt = now;
    }

    public bool IsOriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        return Deserialize(AllowedOriginsJson).Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static string Serialize(IReadOnlyCollection<string> origins)
        => System.Text.Json.JsonSerializer.Serialize(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    private static string[] Deserialize(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
