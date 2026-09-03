using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Omnichannel.Application.Abstractions;

namespace Omnichannel.Infrastructure.Email;

public sealed partial class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public Task SendEmailConfirmationAsync(string toEmail, string toDisplayName, string confirmationLink, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailTemplates.EmailConfirmation(toDisplayName, confirmationLink);
        return SendAsync(toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    public Task SendPasswordResetAsync(string toEmail, string toDisplayName, string resetLink, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailTemplates.PasswordReset(toDisplayName, resetLink);
        return SendAsync(toEmail, toDisplayName, subject, html, text, cancellationToken);
    }

    private async Task SendAsync(string toEmail, string toDisplayName, string subject, string html, string plainText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            // No SMTP configured (e.g. CI/E2E environments without the dev SMTP secret) — this
            // is an expected, valid state, not a failure worth logging as an error.
            LogSendSkippedNotConfigured(logger);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(new MailboxAddress(toDisplayName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = plainText }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            // Email delivery failure must never break the calling flow (registration, password
            // reset) — log and continue, whatever the failure mode (auth, network, DNS, timeout).
            // OperationCanceledException is excluded: that means the caller's own request was
            // cancelled, which should propagate, not be swallowed as an "email failed" case.
            // Never log the message body or recipient's full address.
            LogSendFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send transactional email.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped sending transactional email: no SMTP host configured.")]
    private static partial void LogSendSkippedNotConfigured(ILogger logger);
}
