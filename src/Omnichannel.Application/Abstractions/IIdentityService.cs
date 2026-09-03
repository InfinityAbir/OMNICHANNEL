namespace Omnichannel.Application.Abstractions;

public enum SignInOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
    EmailNotConfirmed,
}

public sealed record CreateUserResult(bool Succeeded, Guid UserId, IReadOnlyList<string> Errors);

/// <summary>
/// Facade over the credential/auth store (ASP.NET Core Identity in Infrastructure) so
/// Application never references Identity types directly — keeps the dependency direction
/// correct (Application must not depend on Infrastructure). See ADR-0007.
/// </summary>
public interface IIdentityService
{
    Task<CreateUserResult> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    Task<SignInOutcome> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);
}
