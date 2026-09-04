using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Email;

namespace Omnichannel.Application.Notifications;

/// <summary>CRUD + connection test for a tenant's own SMTP configuration (Phase 16, ADR-0027).</summary>
public sealed class EmailSettingsService(
    IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider, ITenantSecretStore secrets, IEmailSender emailSender)
{
    private const string PasswordPurpose = "smtp.password";

    public async Task<(TenantEmailSettings Settings, bool HasPassword)> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.TenantEmailSettings.SingleOrDefaultAsync(s => s.TenantId == tenantContext.TenantId, cancellationToken);
        if (settings is null)
        {
            settings = TenantEmailSettings.CreateDefault(tenantContext.TenantId, timeProvider.GetUtcNow());
            db.TenantEmailSettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
        }

        var hasPassword = await secrets.ExistsAsync(tenantContext.TenantId, PasswordPurpose, cancellationToken);
        return (settings, hasPassword);
    }

    public async Task<(TenantEmailSettings Settings, bool HasPassword)> UpdateAsync(
        string host, int port, string username, string fromAddress, string? fromName, string? password, CancellationToken cancellationToken)
    {
        var (settings, hasPassword) = await GetAsync(cancellationToken);
        settings.Configure(host, port, username, fromAddress, fromName, timeProvider.GetUtcNow());

        if (!string.IsNullOrWhiteSpace(password))
        {
            await secrets.SetAsync(tenantContext.TenantId, PasswordPurpose, password, cancellationToken);
            hasPassword = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (settings, hasPassword);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var (settings, _) = await GetAsync(cancellationToken);
        settings.Clear(timeProvider.GetUtcNow());
        await secrets.DeleteAsync(tenantContext.TenantId, PasswordPurpose, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<EmailTestResult> TestAsync(string toEmail, string toDisplayName, CancellationToken cancellationToken)
        => emailSender.SendTestEmailAsync(tenantContext.TenantId, toEmail, toDisplayName, cancellationToken);
}
