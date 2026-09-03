namespace Omnichannel.Contracts.Channels;

public sealed record ChannelAccountAdminResponse(
    Guid Id,
    string Type,
    string DisplayName,
    string Status,
    string? ExternalAccountId,
    bool CredentialConfigured);

public sealed record SetChannelExternalAccountRequest(string ExternalAccountId);

/// <summary>Never a response type — a secret is write-only. See ChannelAccountAdminResponse.CredentialConfigured for the read side.</summary>
public sealed record SetChannelCredentialRequest(string Secret);
