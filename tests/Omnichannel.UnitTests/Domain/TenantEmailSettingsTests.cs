using Omnichannel.Domain.Email;

namespace Omnichannel.UnitTests.Domain;

public class TenantEmailSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_IsNotConfigured()
    {
        var settings = TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now);

        Assert.False(settings.IsConfigured);
        Assert.Equal(587, settings.Port);
        Assert.Null(settings.Host);
    }

    [Fact]
    public void Configure_ValidValues_BecomesConfigured()
    {
        var settings = TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now);

        settings.Configure("smtp.gmail.com", 587, "me@example.com", "support@example.com", "My Business", Now);

        Assert.True(settings.IsConfigured);
        Assert.Equal("smtp.gmail.com", settings.Host);
        Assert.Equal("me@example.com", settings.Username);
        Assert.Equal("support@example.com", settings.FromAddress);
        Assert.Equal("My Business", settings.FromName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Configure_PortOutOfRange_Throws(int port)
    {
        var settings = TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now);

        Assert.Throws<ArgumentException>(() => settings.Configure("smtp.gmail.com", port, "me@example.com", "support@example.com", null, Now));
    }

    [Fact]
    public void Configure_BlankHost_Throws()
        => Assert.Throws<ArgumentException>(() =>
            TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now).Configure(" ", 587, "me@example.com", "support@example.com", null, Now));

    [Fact]
    public void Configure_BlankUsername_Throws()
        => Assert.Throws<ArgumentException>(() =>
            TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now).Configure("smtp.gmail.com", 587, " ", "support@example.com", null, Now));

    [Fact]
    public void Configure_BlankFromAddress_Throws()
        => Assert.Throws<ArgumentException>(() =>
            TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now).Configure("smtp.gmail.com", 587, "me@example.com", " ", null, Now));

    [Fact]
    public void Configure_BlankFromName_StoredAsNull()
    {
        var settings = TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now);

        settings.Configure("smtp.gmail.com", 587, "me@example.com", "support@example.com", "   ", Now);

        Assert.Null(settings.FromName);
    }

    [Fact]
    public void Clear_ResetsToUnconfiguredDefaults()
    {
        var settings = TenantEmailSettings.CreateDefault(Guid.NewGuid(), Now);
        settings.Configure("smtp.gmail.com", 587, "me@example.com", "support@example.com", "My Business", Now);

        settings.Clear(Now);

        Assert.False(settings.IsConfigured);
        Assert.Null(settings.Host);
        Assert.Null(settings.Username);
        Assert.Null(settings.FromAddress);
        Assert.Null(settings.FromName);
        Assert.Equal(587, settings.Port);
    }
}
