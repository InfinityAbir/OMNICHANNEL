namespace Omnichannel.Application.Abstractions;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenGenerator
{
    Task<AccessTokenResult> GenerateAsync(
        Guid userId, string email, Guid tenantId, IReadOnlyCollection<string> permissions, DateTimeOffset now, CancellationToken cancellationToken);
}
