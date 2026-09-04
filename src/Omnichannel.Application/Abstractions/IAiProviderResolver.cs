namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Resolves the actual <see cref="IAiProvider"/> to use for one tenant — that tenant's own
/// configured provider/key if they've set one, otherwise the platform's own default provider
/// (the single global <c>IAiProvider</c> registration that existed before per-tenant provider
/// settings did). See ADR-0027.
/// </summary>
public interface IAiProviderResolver
{
    Task<IAiProvider> ResolveAsync(Guid tenantId, CancellationToken cancellationToken);
}
