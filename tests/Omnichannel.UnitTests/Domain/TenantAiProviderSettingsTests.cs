using Omnichannel.Domain.Ai;

namespace Omnichannel.UnitTests.Domain;

public class TenantAiProviderSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_IsOpenAiCompatibleWithGivenBaseUrlAndModel()
    {
        var settings = TenantAiProviderSettings.CreateDefault(Guid.NewGuid(), "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", Now);

        Assert.Equal(AiProviderKind.OpenAiCompatible, settings.ProviderKind);
        Assert.Equal("https://api.groq.com/openai/v1", settings.BaseUrl);
        Assert.Equal("openai/gpt-oss-120b", settings.Model);
    }

    [Fact]
    public void Configure_OpenAiCompatible_RequiresBaseUrl()
    {
        var settings = TenantAiProviderSettings.CreateDefault(Guid.NewGuid(), "https://api.groq.com/openai/v1", "model-a", Now);

        var ex = Assert.Throws<ArgumentException>(() => settings.Configure(AiProviderKind.OpenAiCompatible, null, "model-b", Now));
        Assert.Contains("Base URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_Anthropic_ClearsBaseUrlEvenIfProvided()
    {
        var settings = TenantAiProviderSettings.CreateDefault(Guid.NewGuid(), "https://api.groq.com/openai/v1", "model-a", Now);

        settings.Configure(AiProviderKind.Anthropic, "https://should-be-ignored.example", "claude-3-5-sonnet", Now);

        Assert.Equal(AiProviderKind.Anthropic, settings.ProviderKind);
        Assert.Null(settings.BaseUrl);
        Assert.Equal("claude-3-5-sonnet", settings.Model);
    }

    [Fact]
    public void Configure_BlankModel_Throws()
    {
        var settings = TenantAiProviderSettings.CreateDefault(Guid.NewGuid(), "https://api.groq.com/openai/v1", "model-a", Now);

        Assert.Throws<ArgumentException>(() => settings.Configure(AiProviderKind.OpenAiCompatible, "https://example.com", "  ", Now));
    }

    [Fact]
    public void Configure_TrimsBaseUrlAndModel()
    {
        var settings = TenantAiProviderSettings.CreateDefault(Guid.NewGuid(), "https://api.groq.com/openai/v1", "model-a", Now);

        settings.Configure(AiProviderKind.OpenAiCompatible, "  https://api.openai.com/v1  ", "  gpt-4o  ", Now);

        Assert.Equal("https://api.openai.com/v1", settings.BaseUrl);
        Assert.Equal("gpt-4o", settings.Model);
    }
}
