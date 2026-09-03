namespace Omnichannel.Application.Auth;

public sealed record AuthTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public enum RegisterOutcome
{
    Success,
    EmailAlreadyRegistered,
    WeakPassword,
}

public sealed record RegisterResult(RegisterOutcome Outcome, IReadOnlyList<string> Errors, AuthTokens? Tokens = null);

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
    EmailNotConfirmed,
}

public sealed record LoginResult(LoginOutcome Outcome, AuthTokens? Tokens = null);

public enum RefreshOutcome
{
    Success,
    InvalidOrExpired,
}

public sealed record RefreshResult(RefreshOutcome Outcome, AuthTokens? Tokens = null);
