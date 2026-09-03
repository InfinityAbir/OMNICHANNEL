namespace Omnichannel.Application.Abstractions;

/// <summary>
/// Issues the short-lived, audience-scoped session token used by the anonymous website-chat
/// widget (implemented in Infrastructure over the shared JWT signing key). Application depends on
/// this abstraction, not on the Infrastructure JWT implementation.
/// </summary>
public interface IWidgetSessionTokenGenerator
{
    string Generate(Guid tenantId, Guid visitorId, Guid sessionId, Guid conversationId, DateTimeOffset now);
}
