using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;

namespace Omnichannel.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    IIdentityService identity,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokens,
    IEmailSender emailSender,
    TimeProvider timeProvider)
{
    public async Task<RegisterResult> RegisterAsync(
        string email,
        string password,
        string displayName,
        string businessName,
        string timeZone,
        string emailConfirmationLinkBase,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var alreadyExists = await db.UserProfiles.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (alreadyExists)
        {
            // Same generic outcome as an unknown weak-password case would look like from the
            // outside — callers must not reveal whether an email is registered (PRD: enumeration).
            return new RegisterResult(RegisterOutcome.EmailAlreadyRegistered, ["That email can't be used."]);
        }

        var createResult = await identity.CreateUserAsync(normalizedEmail, password, cancellationToken);
        if (!createResult.Succeeded)
        {
            return new RegisterResult(RegisterOutcome.WeakPassword, createResult.Errors);
        }

        var domainUser = User.Create(createResult.UserId, normalizedEmail, displayName, now);
        var tenant = Tenant.Create(businessName, GenerateSlug(businessName), timeZone, now);

        var ownerRole = await db.Roles.SingleAsync(r => r.SystemRole == SystemRole.Owner, cancellationToken);
        var membership = TenantMembership.Create(tenant.Id, domainUser.Id, ownerRole.Id, now);

        db.UserProfiles.Add(domainUser);
        db.Tenants.Add(tenant);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);

        var confirmationToken = await identity.GenerateEmailConfirmationTokenAsync(domainUser.Id, cancellationToken);
        var confirmationLink = $"{emailConfirmationLinkBase}?userId={domainUser.Id}&token={Uri.EscapeDataString(confirmationToken)}";
        await emailSender.SendEmailConfirmationAsync(domainUser.Email, domainUser.DisplayName, confirmationLink, cancellationToken);

        var tokens = await IssueTokensAsync(domainUser.Id, domainUser.Email, tenant.Id, ownerRole.Permissions, now, cancellationToken);
        return new RegisterResult(RegisterOutcome.Success, [], tokens);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var outcome = await identity.CheckPasswordAsync(normalizedEmail, password, cancellationToken);
        if (outcome != SignInOutcome.Success)
        {
            return new LoginResult(outcome switch
            {
                SignInOutcome.LockedOut => LoginOutcome.LockedOut,
                SignInOutcome.EmailNotConfirmed => LoginOutcome.EmailNotConfirmed,
                _ => LoginOutcome.InvalidCredentials,
            });
        }

        var context = await GetActiveTenantContextAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (context is null)
        {
            // No active tenant membership — nothing to issue a token against.
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var tokens = await IssueTokensAsync(context.UserId, context.Email, context.TenantId, context.Permissions, now, cancellationToken);
        return new LoginResult(LoginOutcome.Success, tokens);
    }

    public async Task<RefreshResult> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var record = await refreshTokens.FindActiveAsync(rawRefreshToken, now, cancellationToken);
        if (record is null)
        {
            return new RefreshResult(RefreshOutcome.InvalidOrExpired);
        }

        var context = await GetActiveTenantContextAsync(u => u.Id == record.UserId, cancellationToken);
        if (context is null)
        {
            return new RefreshResult(RefreshOutcome.InvalidOrExpired);
        }

        var newRawRefreshToken = await refreshTokens.RotateAsync(record.Id, context.UserId, now, cancellationToken);
        var accessToken = accessTokenGenerator.Generate(context.UserId, context.Email, context.TenantId, context.Permissions, now);

        return new RefreshResult(RefreshOutcome.Success, new AuthTokens(accessToken.Token, accessToken.ExpiresAt, newRawRefreshToken));
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var record = await refreshTokens.FindActiveAsync(rawRefreshToken, now, cancellationToken);
        if (record is not null)
        {
            await refreshTokens.RevokeAsync(record.Id, now, cancellationToken);
        }
    }

    public async Task RequestPasswordResetAsync(string email, string resetLinkBase, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var userId = await identity.FindUserIdByEmailAsync(normalizedEmail, cancellationToken);

        // Deliberately silent no-op when the email isn't registered — do not reveal
        // whether an account exists (PRD security review: enumeration).
        if (userId is null)
        {
            return;
        }

        var domainUser = await db.UserProfiles.SingleAsync(u => u.Id == userId, cancellationToken);
        var token = await identity.GeneratePasswordResetTokenAsync(domainUser.Id, cancellationToken);
        var resetLink = $"{resetLinkBase}?userId={domainUser.Id}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendPasswordResetAsync(domainUser.Email, domainUser.DisplayName, resetLink, cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var succeeded = await identity.ResetPasswordAsync(userId, token, newPassword, cancellationToken);
        if (succeeded)
        {
            // A reset invalidates every outstanding session — force re-authentication everywhere.
            await refreshTokens.RevokeAllForUserAsync(userId, timeProvider.GetUtcNow(), cancellationToken);
        }

        return succeeded;
    }

    public Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
        => identity.ConfirmEmailAsync(userId, token, cancellationToken);

    private async Task<AuthTokens> IssueTokensAsync(
        Guid userId,
        string email,
        Guid tenantId,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accessToken = accessTokenGenerator.Generate(userId, email, tenantId, permissions, now);
        var refreshToken = await refreshTokens.IssueAsync(userId, now, cancellationToken);
        return new AuthTokens(accessToken.Token, accessToken.ExpiresAt, refreshToken);
    }

    private sealed record ActiveTenantContext(Guid UserId, string Email, Guid TenantId, List<string> Permissions);

    /// <summary>
    /// One joined query for user + active membership + role instead of three sequential
    /// round-trips — used by both Login and Refresh.
    ///
    /// Deliberately bypasses the tenant global query filter (ADR-0005) via IgnoreQueryFilters:
    /// this call happens BEFORE a tenant context exists — discovering which tenant the user
    /// belongs to is the whole point of login/refresh, so the normal "scope by current tenant"
    /// filter would just return nothing. Safe because the query is scoped by <paramref
    /// name="userPredicate"/>, which the caller derives from a *verified* identity (a password
    /// that already passed Identity's check, or a refresh token already found by its hash) —
    /// never from client-supplied input — so this cannot be used to enumerate another user's
    /// memberships.
    /// </summary>
    private Task<ActiveTenantContext?> GetActiveTenantContextAsync(
        System.Linq.Expressions.Expression<Func<User, bool>> userPredicate, CancellationToken cancellationToken)
        => (
            from user in db.UserProfiles.Where(userPredicate)
            join membership in db.Memberships.IgnoreQueryFilters().Where(m => m.Status == MembershipStatus.Active)
                on user.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            orderby membership.CreatedAt
            select new ActiveTenantContext(user.Id, user.Email, membership.TenantId, role.Permissions)
        ).FirstOrDefaultAsync(cancellationToken);

    private static string GenerateSlug(string businessName)
    {
        var lowered = businessName.Trim().ToLowerInvariant();
        var slugChars = lowered.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(slugChars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        return string.IsNullOrEmpty(slug) ? suffix : $"{slug}-{suffix}";
    }
}
