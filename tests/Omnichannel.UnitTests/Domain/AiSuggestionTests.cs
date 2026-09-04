using Omnichannel.Domain.Ai;

namespace Omnichannel.UnitTests.Domain;

public class AiSuggestionTests
{
    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.7, 1.0)]
    public void Create_ClampsConfidenceToZeroOneRange(double input, double expected)
    {
        var suggestion = AiSuggestion.Create(Guid.NewGuid(), Guid.NewGuid(), "text", input, "model", 1, 1, DateTimeOffset.UtcNow);

        Assert.Equal(expected, suggestion.Confidence);
    }
}
