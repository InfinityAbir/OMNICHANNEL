namespace Omnichannel.Application.Abstractions;

/// <summary>Provider-agnostic embedding abstraction (docs/ai.md, PRD §70) — Application/API code never depends on how a vector was computed, only that every chunk's vector has <see cref="Dimensions"/> length.</summary>
public interface IEmbeddingProvider
{
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}
