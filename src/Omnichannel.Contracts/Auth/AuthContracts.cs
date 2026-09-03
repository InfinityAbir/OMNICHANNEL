using System.ComponentModel.DataAnnotations;

namespace Omnichannel.Contracts.Auth;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(10), MaxLength(200)]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string BusinessName { get; init; } = string.Empty;

    [MaxLength(100)]
    public string TimeZone { get; init; } = "UTC";
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class LogoutRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class RequestPasswordResetRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    public Guid UserId { get; init; }

    [Required]
    public string Token { get; init; } = string.Empty;

    [Required, MinLength(10), MaxLength(200)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record AuthTokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public sealed record CurrentUserResponse(Guid UserId, string Email, string DisplayName, Guid TenantId, string TenantName, string Role, IReadOnlyList<string> Permissions);
