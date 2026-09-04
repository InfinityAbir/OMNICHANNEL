using System.Text;
using System.Text.RegularExpressions;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Knowledge;

/// <summary>
/// A deterministic lexical (feature-hashing / "hashing trick") embedding — the same well-known
/// bag-of-words technique behind e.g. scikit-learn's HashingVectorizer, not a neural semantic
/// embedding. Chosen because no embeddings-capable API key was available this phase (confirmed:
/// Groq's own catalog has no embedding model — see ADR-0021); needs no network call, no API key,
/// no extra package dependency (a manual FNV-1a hash, not `string.GetHashCode()`, which is
/// randomized per process and would make embeddings non-reproducible across restarts). It
/// supports real, working keyword/near-duplicate retrieval today; swapping in a neural provider
/// later (OpenAI, Cohere, ...) is a new <see cref="IEmbeddingProvider"/> implementation and a DI
/// registration change — nothing above this class depends on which kind of embedding it is.
/// </summary>
public sealed partial class HashingEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 256;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = new float[Dimensions];
        var tokens = TokenPattern().Matches(text.ToLowerInvariant());

        foreach (Match token in tokens)
        {
            var hash = Fnv1aHash(token.Value);
            var index = (int)(hash % (uint)Dimensions);
            var sign = (hash & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    // FNV-1a — simple, stable across processes/machines/.NET versions (unlike
    // string.GetHashCode(), which is randomized per process by design).
    private static uint Fnv1aHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private static void Normalize(float[] vector)
    {
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude <= 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }
    }

    // Latin letters/digits plus the Bangla Unicode block (U+0980-U+09FF) so Bangla-script content
    // tokenizes correctly, not just Banglish (which is already plain Latin).
    [GeneratedRegex("[a-zA-Z0-9ঀ-৿]+")]
    private static partial Regex TokenPattern();
}
