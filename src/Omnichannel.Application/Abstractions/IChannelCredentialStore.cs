namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Encrypts/decrypts a channel account's provider credential (API token, app secret, ...) at
/// rest. Plaintext exists only for the duration of a Set/Get call — never persisted, logged, or
/// returned to any API response (AGENTS.md: credentials in approved secret storage only, never
/// source code/logs/tests/client bundles).
/// </summary>
public interface IChannelCredentialStore
{
    Task SetAsync(Guid channelAccountId, string plaintextSecret, CancellationToken cancellationToken);

    Task<string?> GetAsync(Guid channelAccountId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid channelAccountId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid channelAccountId, CancellationToken cancellationToken);
}
