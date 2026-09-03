using Microsoft.AspNetCore.Mvc;
using Omnichannel.Api.Validation;
using Omnichannel.Application.Auth;
using Omnichannel.Contracts.Auth;

namespace Omnichannel.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/confirm-email", ConfirmEmailAsync);
        group.MapPost("/password-reset/request", RequestPasswordResetAsync);
        group.MapPost("/password-reset/confirm", ResetPasswordAsync);
        group.MapGet("/password-reset/form", ResetPasswordFormAsync);
        group.MapPost("/password-reset/form", ResetPasswordFromFormAsync);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, AuthService auth, HttpContext http, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var confirmationBase = $"{http.Request.Scheme}://{http.Request.Host}/api/v1/auth/confirm-email";
        var result = await auth.RegisterAsync(
            request.Email, request.Password, request.DisplayName, request.BusinessName, request.TimeZone,
            confirmationBase, cancellationToken);

        return result.Outcome switch
        {
            RegisterOutcome.Success => Results.Ok(new AuthTokenResponse(
                result.Tokens!.AccessToken, result.Tokens.AccessTokenExpiresAt, result.Tokens.RefreshToken)),
            RegisterOutcome.EmailAlreadyRegistered => Results.Problem(
                title: "Registration failed.", detail: "That email can't be used.", statusCode: StatusCodes.Status400BadRequest),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = [.. result.Errors] }),
        };
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var result = await auth.LoginAsync(request.Email, request.Password, cancellationToken);

        return result.Outcome switch
        {
            LoginOutcome.Success => Results.Ok(new AuthTokenResponse(
                result.Tokens!.AccessToken, result.Tokens.AccessTokenExpiresAt, result.Tokens.RefreshToken)),
            LoginOutcome.LockedOut => Results.Problem(
                title: "Account locked.", detail: "Too many failed attempts. Try again later.", statusCode: StatusCodes.Status423Locked),
            LoginOutcome.EmailNotConfirmed => Results.Problem(
                title: "Email not confirmed.", statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Problem(title: "Invalid credentials.", statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, cancellationToken);
        return result.Outcome == RefreshOutcome.Success
            ? Results.Ok(new AuthTokenResponse(result.Tokens!.AccessToken, result.Tokens.AccessTokenExpiresAt, result.Tokens.RefreshToken))
            : Results.Problem(title: "Invalid or expired refresh token.", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LogoutAsync(LogoutRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmEmailAsync(
        [FromQuery] Guid userId, [FromQuery] string token, AuthService auth, CancellationToken cancellationToken)
    {
        var succeeded = await auth.ConfirmEmailAsync(userId, token, cancellationToken);
        return Results.Content(ResultPage.Render(
            succeeded ? "Email confirmed" : "Link expired or invalid",
            succeeded ? "Your email is confirmed. You can now sign in." : "This confirmation link is no longer valid. Request a new one from the sign-in page.",
            succeeded), "text/html");
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        RequestPasswordResetRequest request, AuthService auth, HttpContext http, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var resetBase = $"{http.Request.Scheme}://{http.Request.Host}/api/v1/auth/password-reset/form";
        await auth.RequestPasswordResetAsync(request.Email, resetBase, cancellationToken);

        // Always the same response, whether or not the email exists — prevents account enumeration.
        return Results.Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    private static async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var succeeded = await auth.ResetPasswordAsync(request.UserId, request.Token, request.NewPassword, cancellationToken);
        return succeeded
            ? Results.NoContent()
            : Results.Problem(title: "Reset failed.", detail: "The link is invalid, expired, or the new password doesn't meet policy.", statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult ResetPasswordFormAsync([FromQuery] Guid userId, [FromQuery] string token)
        => Results.Content(ResetPasswordFormPage.Render(userId, token), "text/html");

    private static async Task<IResult> ResetPasswordFromFormAsync(HttpRequest request, AuthService auth, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var userId = Guid.TryParse(form["userId"], out var id) ? id : Guid.Empty;
        var token = form["token"].ToString();
        var newPassword = form["newPassword"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();

        if (userId == Guid.Empty || string.IsNullOrEmpty(token) || newPassword != confirmPassword || newPassword.Length < 10)
        {
            return Results.Content(ResultPage.Render("Reset failed", "Passwords didn't match or didn't meet the minimum requirements. Go back and try again.", false), "text/html");
        }

        var succeeded = await auth.ResetPasswordAsync(userId, token, newPassword, cancellationToken);
        return Results.Content(ResultPage.Render(
            succeeded ? "Password updated" : "Link expired or invalid",
            succeeded ? "Your password has been changed. You can now sign in." : "This reset link is no longer valid. Request a new one.",
            succeeded), "text/html");
    }
}
