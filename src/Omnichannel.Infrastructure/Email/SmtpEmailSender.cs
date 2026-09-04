using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Email;

/// <summary>
/// Resolves each tenant's own SMTP configuration (<c>TenantEmailSettings</c> + the encrypted
/// app-password in <c>ITenantSecretStore</c>), falling back to the platform's own default
/// (<see cref="SmtpOptions"/>) when the tenant hasn't configured one — same fallback shape as
/// <c>AiProviderResolver</c> (ADR-0027). Every method takes an explicit tenantId rather than
/// relying on ambient tenant context, since registration and password-reset call this before any
/// authenticated context exists (the same reason <c>AiAutoReplyService</c> does).
/// </summary>
public sealed partial class SmtpEmailSender(
    IAppDbContext db, ITenantSecretStore secrets, IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private const string PasswordPurpose = "smtp.password";
    private readonly SmtpOptions _defaultOptions = options.Value;

    public Task SendEmailConfirmationAsync(Guid tenantId, string toEmail, string toDisplayName, string confirmationLink, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailTemplates.EmailConfirmation(toDisplayName, confirmationLink);
        return SendAsync(tenantId, toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    public Task SendPasswordResetAsync(Guid tenantId, string toEmail, string toDisplayName, string resetLink, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailTemplates.PasswordReset(toDisplayName, resetLink);
        return SendAsync(tenantId, toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    public Task SendConversationEscalatedAsync(
        Guid tenantId, string toEmail, string toDisplayName, string tenantName, Guid conversationId, string ruleName, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailTemplates.ConversationEscalated(toDisplayName, tenantName, conversationId, ruleName);
        return SendAsync(tenantId, toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    public Task SendTenantDeletionScheduledAsync(
        Guid tenantId, string toEmail, string toDisplayName, string tenantName, DateTimeOffset scheduledDeletionAt, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailTemplates.TenantDeletionScheduled(toDisplayName, tenantName, scheduledDeletionAt);
        return SendAsync(tenantId, toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    public async Task<EmailTestResult> SendTestEmailAsync(Guid tenantId, string toEmail, string toDisplayName, CancellationToken cancellationToken)
    {
        var config = await ResolveConfigAsync(tenantId, cancellationToken);
        if (config is null)
        {
            return new EmailTestResult(false, "No SMTP is configured (neither this business's own settings nor a platform default).");
        }

        var (subject, html, text) = EmailTemplates.TestEmail(toDisplayName, config.IsTenantSpecific);
        return await TrySendAsync(config, toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    private async Task SendAsync(Guid tenantId, string toEmail, string toDisplayName, string subject, string html, string plainText, CancellationToken cancellationToken)
    {
        var config = await ResolveConfigAsync(tenantId, cancellationToken);
        if (config is null)
        {
            // No SMTP configured at all (tenant or platform) — an expected, valid state (e.g.
            // CI/E2E environments without the dev SMTP secret), not a failure worth logging as one.
            LogSendSkippedNotConfigured(logger);
            return;
        }

        await TrySendAsync(config, toEmail, toDisplayName, subject, html, plainText, cancellationToken);
    }

    private async Task<EmailTestResult> TrySendAsync(
        ResolvedSmtpConfig config, string toEmail, string toDisplayName, string subject, string html, string plainText, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        message.To.Add(new MailboxAddress(toDisplayName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = plainText }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(config.Host, config.Port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(config.Username, config.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new EmailTestResult(true, $"Sent successfully via {config.Host}:{config.Port}.");
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // Email delivery failure must never break the calling flow (registration, password
            // reset) — log and continue, whatever the failure mode (auth, network, DNS, timeout).
            // OperationCanceledException is excluded: that means the caller's own request was
            // cancelled, which should propagate, not be swallowed as an "email failed" case.
            // Never log the message body or recipient's full address.
            LogSendFailed(logger, ex);
            return new EmailTestResult(false, DescribeFailure(ex));
        }
    }

    // A generic-but-useful message: the real exception is already logged server-side (with full
    // detail) via LogSendFailed — what reaches the API/UI must never include internal exception
    // text verbatim (could echo back connection strings, stack detail, etc.), just an actionable
    // category.
    private static string DescribeFailure(Exception ex) => ex switch
    {
        AuthenticationException => "Authentication failed — check the username and app password.",
        SmtpCommandException or SmtpProtocolException => "The mail server rejected the connection or message.",
        _ => "Could not connect to the mail server — check the host and port.",
    };

    private async Task<ResolvedSmtpConfig?> ResolveConfigAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantSettings = await db.TenantEmailSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (tenantSettings is { IsConfigured: true })
        {
            var password = await secrets.GetAsync(tenantId, PasswordPurpose, cancellationToken);
            if (!string.IsNullOrWhiteSpace(password))
            {
                return new ResolvedSmtpConfig(
                    tenantSettings.Host!, tenantSettings.Port, tenantSettings.Username!, password,
                    tenantSettings.FromAddress!, tenantSettings.FromName ?? tenantSettings.FromAddress!, IsTenantSpecific: true);
            }
        }

        if (string.IsNullOrWhiteSpace(_defaultOptions.Host))
        {
            return null;
        }

        return new ResolvedSmtpConfig(
            _defaultOptions.Host, _defaultOptions.Port, _defaultOptions.Username, _defaultOptions.Password,
            _defaultOptions.FromAddress, _defaultOptions.FromName, IsTenantSpecific: false);
    }

    private sealed record ResolvedSmtpConfig(string Host, int Port, string Username, string Password, string FromAddress, string FromName, bool IsTenantSpecific);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send transactional email.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped sending transactional email: no SMTP host configured.")]
    private static partial void LogSendSkippedNotConfigured(ILogger logger);
}
