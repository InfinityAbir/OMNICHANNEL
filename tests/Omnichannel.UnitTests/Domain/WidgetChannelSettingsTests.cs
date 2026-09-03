using Omnichannel.Domain.Widget;

namespace Omnichannel.UnitTests.Domain;

public class WidgetChannelSettingsTests
{
    private static WidgetChannelSettings Create(string[]? origins = null, string[]? extra = null)
        => WidgetChannelSettings.Create(Guid.NewGuid(), Guid.NewGuid(), origins ?? extra ?? [], DateTimeOffset.UtcNow);

    [Fact]
    public void Create_IsEnabled_WithNoOrigins()
    {
        var settings = Create([]);
        Assert.True(settings.Enabled);
        Assert.Empty(settings.GetAllowedOrigins());
    }

    [Fact]
    public void Create_StoresOrigins_Deduplicated()
    {
        var settings = Create(["https://shop.example", "https://shop.example/", "https://shop.example"]);
        Assert.Equal(2, settings.GetAllowedOrigins().Count);
        Assert.Contains("https://shop.example/", settings.GetAllowedOrigins());
    }

    [Fact]
    public void IsOriginAllowed_MatchesListenedOrigin()
    {
        var settings = Create(["https://shop.example"]);
        Assert.True(settings.IsOriginAllowed("https://shop.example"));
    }

    [Fact]
    public void IsOriginAllowed_IgnoresCase()
    {
        var settings = Create(["HTTPS://Shop.Example"]);
        Assert.True(settings.IsOriginAllowed("https://shop.example"));
    }

    [Fact]
    public void IsOriginAllowed_RejectsUnlistedOrigin()
    {
        var settings = Create(["https://shop.example"]);
        Assert.False(settings.IsOriginAllowed("https://evil.example"));
    }

    [Fact]
    public void IsOriginAllowed_RejectsBlankOrNull()
    {
        var settings = Create(["https://shop.example"]);
        Assert.False(settings.IsOriginAllowed(null));
        Assert.False(settings.IsOriginAllowed(""));
        Assert.False(settings.IsOriginAllowed("   "));
    }

    [Fact]
    public void SetAllowedOrigins_ReplacesList()
    {
        var settings = Create(["https://old.example"]);
        settings.SetAllowedOrigins(["https://new.example"], DateTimeOffset.UtcNow);
        Assert.DoesNotContain("https://old.example", settings.GetAllowedOrigins());
        Assert.Contains("https://new.example", settings.GetAllowedOrigins());
    }
}
