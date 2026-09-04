using Microsoft.AspNetCore.Identity;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Identity;

public sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<CreateUserResult> CreateUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, password);

        return result.Succeeded
            ? new CreateUserResult(true, user.Id, [])
            : new CreateUserResult(false, Guid.Empty, [.. result.Errors.Select(e => e.Description)]);
    }

    public async Task<SignInOutcome> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Constant-shape outcome vs. a real user's wrong password — avoids leaking
            // whether the email is registered via response-path timing/shape differences.
            return SignInOutcome.InvalidCredentials;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return SignInOutcome.LockedOut;
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            return SignInOutcome.InvalidCredentials;
        }

        if (userManager.Options.SignIn.RequireConfirmedEmail && !await userManager.IsEmailConfirmedAsync(user))
        {
            return SignInOutcome.EmailNotConfirmed;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return SignInOutcome.Success;
    }

    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user?.Id;
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? string.Empty : await userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded;
    }

    public async Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? string.Empty : await userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded;
    }

    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded;
    }
}
