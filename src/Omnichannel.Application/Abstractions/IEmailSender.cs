namespace Omnichannel.Application.Abstractions;

public interface IEmailSender
{
    Task SendEmailConfirmationAsync(string toEmail, string toDisplayName, string confirmationLink, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(string toEmail, string toDisplayName, string resetLink, CancellationToken cancellationToken);
}
