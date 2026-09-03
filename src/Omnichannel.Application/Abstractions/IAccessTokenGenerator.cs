namespace Omnichannel.Application.Abstractions;

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenGenerator
{
    AccessTokenResult Generate(Guid userId, string email, Guid tenantId, IReadOnlyCollection<string> permissions, DateTimeOffset now);
}
