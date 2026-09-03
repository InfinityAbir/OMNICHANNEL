namespace Omnichannel.Infrastructure.Widget;

public sealed class WidgetTokenOptions
{
    public const string SectionName = "WidgetToken";

    public required string Audience { get; init; }

    public int SessionLifetimeMinutes { get; init; } = 30;
}
