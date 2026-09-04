namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Every method takes an explicit <c>tenantId</c> so the implementation can resolve that
/// tenant's own SMTP configuration (falling back to the platform default when unconfigured —
/// ADR-0027) — the same explicit-tenantId shape <c>AiAutoReplyService</c>/
/// <c>AutomationRuleService</c> already use, and for the same underlying reason: registration and
/// password-reset both call this before any authenticated/ambient tenant context exists.
/// </summary>
public interface IEmailSender
{
    Task SendEmailConfirmationAsync(Guid tenantId, string toEmail, string toDisplayName, string confirmationLink, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(Guid tenantId, string toEmail, string toDisplayName, string resetLink, CancellationToken cancellationToken);

    Task SendConversationEscalatedAsync(
        Guid tenantId, string toEmail, string toDisplayName, string tenantName, Guid conversationId, string ruleName, CancellationToken cancellationToken);

    /// <summary>Notifies the requester that the whole business account (tenant) is scheduled for
    /// permanent deletion, and by when — ADR-0030.</summary>
    Task SendTenantDeletionScheduledAsync(
        Guid tenantId, string toEmail, string toDisplayName, string tenantName, DateTimeOffset scheduledDeletionAt, CancellationToken cancellationToken);

    /// <summary>Sends a real test email to prove a tenant's (or the platform default's) SMTP configuration actually works — not just that Set/authenticate succeeded. Returns a human-readable outcome, never throws for an expected failure (bad host/credentials).</summary>
    Task<EmailTestResult> SendTestEmailAsync(Guid tenantId, string toEmail, string toDisplayName, CancellationToken cancellationToken);
}

public sealed record EmailTestResult(bool Success, string Message);
