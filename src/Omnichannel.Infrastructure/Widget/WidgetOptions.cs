namespace Omnichannel.Infrastructure.Widget;

/// <summary>Configuration for the self-hosted website-chat widget (embed script path, etc.).</summary>
public sealed class WidgetOptions
{
    public const string SectionName = "Widget";

    /// <summary>URL path prefix under which the widget embed script + assets are served by the API.</summary>
    public string EmbedPath { get; set; } = "/widget";
}
