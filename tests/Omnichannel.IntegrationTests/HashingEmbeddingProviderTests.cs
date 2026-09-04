using Omnichannel.Infrastructure.Knowledge;

namespace Omnichannel.IntegrationTests;

public class HashingEmbeddingProviderTests
{
    private readonly HashingEmbeddingProvider _provider = new();

    [Fact]
    public async Task EmbedAsync_ReturnsVectorOfDeclaredDimensions()
    {
        var vector = await _provider.EmbedAsync("Do you have a blue jacket in size M?", CancellationToken.None);

        Assert.Equal(_provider.Dimensions, vector.Length);
    }

    [Fact]
    public async Task EmbedAsync_IsDeterministic()
    {
        var v1 = await _provider.EmbedAsync("Return policy is 30 days.", CancellationToken.None);
        var v2 = await _provider.EmbedAsync("Return policy is 30 days.", CancellationToken.None);

        Assert.Equal(v1, v2);
    }

    [Fact]
    public async Task EmbedAsync_IsNormalizedToUnitLength()
    {
        var vector = await _provider.EmbedAsync("Some reasonably long piece of text to embed for the test.", CancellationToken.None);

        var magnitude = Math.Sqrt(vector.Sum(v => (double)v * v));
        Assert.InRange(magnitude, 0.99, 1.01);
    }

    [Fact]
    public async Task EmbedAsync_SharedVocabularyIsCloserThanUnrelatedText()
    {
        var query = await _provider.EmbedAsync("blue jacket size M", CancellationToken.None);
        var related = await _provider.EmbedAsync("We have the blue jacket available in size M and L.", CancellationToken.None);
        var unrelated = await _provider.EmbedAsync("Our store hours are 9am to 6pm on weekdays.", CancellationToken.None);

        Assert.True(CosineDistance(query, related) < CosineDistance(query, unrelated));
    }

    [Fact]
    public async Task EmbedAsync_TokenizesBanglaScript()
    {
        // Confirms the Bangla Unicode block is actually matched by the tokenizer, not silently
        // dropped (which would make Bangla documents embed as an all-zero/empty vector).
        var vector = await _provider.EmbedAsync("আপনার কাছে কি নীল জ্যাকেট আছে?", CancellationToken.None);

        Assert.Contains(vector, v => v != 0);
    }

    private static double CosineDistance(float[] a, float[] b)
    {
        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return 1 - dot;
    }
}
