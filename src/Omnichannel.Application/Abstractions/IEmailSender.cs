namespace Omnichannel.Application.Abstractions;

public interface IEmailSender
{
    Task SendEmailConfirmationAsync(string toEmail, string toDisplayName, string confirmationLink, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(string toEmail, string toDisplayName, string resetLink, CancellationToken cancellationToken);

    Task SendConversationEscalatedAsync(
        string toEmail, string toDisplayName, string tenantName, Guid conversationId, string ruleName, CancellationToken cancellationToken);
}
