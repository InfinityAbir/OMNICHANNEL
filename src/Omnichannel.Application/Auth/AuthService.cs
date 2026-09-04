using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Automation;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Email;
using Omnichannel.Domain.Identity;
using Omnichannel.Domain.Tenancy;
using Omnichannel.Domain.Widget;

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

        // Every tenant gets a "Manual" channel account so conversations can be created before
        // any real channel adapter exists (Phase 5+) — see ADR-0012.
        var manualChannel = ChannelAccount.Create(tenant.Id, ChannelType.Manual, "Manual", now);

        // And a WebsiteChat channel account + widget settings (Phase 5). Origins start empty
        // (secure default = every embed origin denied) until the business allows its site(s).
        var websiteChatChannel = ChannelAccount.Create(tenant.Id, ChannelType.WebsiteChat, "Website Chat", now);
        var widgetSettings = WidgetChannelSettings.Create(tenant.Id, websiteChatChannel.Id, [], now);

        // AI auto-reply (Phase 12) starts disabled and unconfigured — the tenant must explicitly
        // opt in and set up business hours before it can ever fire (PRD §71 conservative default).
        var autoReplySettings = AiAutoReplySettings.CreateDefault(tenant.Id, now);

        // General business hours (Phase 13) — also unconfigured by default, independent of the
        // AI-specific config above (ADR-0023).
        var businessHours = TenantBusinessHours.CreateDefault(tenant.Id, now);

        // Per-tenant AI provider (Phase 16) — defaults to the same Groq configuration the
        // platform itself uses, so AI features work immediately; a tenant can override with their
        // own provider/key any time (ADR-0027).
        var aiProviderSettings = TenantAiProviderSettings.CreateDefault(tenant.Id, "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", now);

        // Per-tenant SMTP (Phase 16) — unconfigured by default; falls back to the platform's own
        // SMTP until the tenant sets their own (ADR-0027).
        var emailSettings = TenantEmailSettings.CreateDefault(tenant.Id, now);

        db.UserProfiles.Add(domainUser);
        db.Tenants.Add(tenant);
        db.Memberships.Add(membership);
        db.ChannelAccounts.Add(manualChannel);
        db.ChannelAccounts.Add(websiteChatChannel);
        db.WidgetSettings.Add(widgetSettings);
        db.TenantAiProviderSettings.Add(aiProviderSettings);
        db.TenantEmailSettings.Add(emailSettings);
        db.AiAutoReplySettings.Add(autoReplySettings);
        db.TenantBusinessHours.Add(businessHours);
        await db.SaveChangesAsync(cancellationToken);

        var confirmationToken = await identity.GenerateEmailConfirmationTokenAsync(domainUser.Id, cancellationToken);
        var confirmationLink = $"{emailConfirmationLinkBase}?userId={domainUser.Id}&token={Uri.EscapeDataString(confirmationToken)}";
        await emailSender.SendEmailConfirmationAsync(tenant.Id, domainUser.Email, domainUser.DisplayName, confirmationLink, cancellationToken);

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
        var accessToken = await accessTokenGenerator.GenerateAsync(context.UserId, context.Email, context.TenantId, context.Permissions, now, cancellationToken);

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
        var tenantId = await db.Memberships
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        var token = await identity.GeneratePasswordResetTokenAsync(domainUser.Id, cancellationToken);
        var resetLink = $"{resetLinkBase}?userId={domainUser.Id}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendPasswordResetAsync(tenantId, domainUser.Email, domainUser.DisplayName, resetLink, cancellationToken);
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
        var accessToken = await accessTokenGenerator.GenerateAsync(userId, email, tenantId, permissions, now, cancellationToken);
        var refreshToken = await refreshTokens.IssueAsync(userId, now, cancellationToken);
        return new AuthTokens(accessToken.Token, accessToken.ExpiresAt, refreshToken);
    }

    private sealed record ActiveTenantContext(Guid UserId, string Email, Guid TenantId, List<string> Permissions);

    /// <summary>
    /// One joined query for user + active membership + role + tenant instead of four sequential
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
    ///
    /// Filters to tenants still <see cref="TenantStatus.Active"/> (ADR-0030) — a Suspended,
    /// PendingDeletion, or Deleted tenant issues no new access/refresh tokens, though a token
    /// already issued keeps working until it naturally expires (same "no implicit mass logout"
    /// principle as JWT key rotation's overlap window, ADR-0029). A user who belongs to several
    /// tenants simply falls through to their next-oldest *active* membership instead of being
    /// blocked outright — only a user with no active tenant left at all gets the existing
    /// InvalidCredentials outcome.
    /// </summary>
    private Task<ActiveTenantContext?> GetActiveTenantContextAsync(
        System.Linq.Expressions.Expression<Func<User, bool>> userPredicate, CancellationToken cancellationToken)
        => (
            from user in db.UserProfiles.Where(userPredicate)
            join membership in db.Memberships.IgnoreQueryFilters().Where(m => m.Status == MembershipStatus.Active)
                on user.Id equals membership.UserId
            join role in db.Roles on membership.RoleId equals role.Id
            join tenant in db.Tenants.Where(t => t.Status == TenantStatus.Active) on membership.TenantId equals tenant.Id
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
