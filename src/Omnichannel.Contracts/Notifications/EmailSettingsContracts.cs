namespace Omnichannel.Contracts.Notifications;

public sealed record EmailSettingsResponse(string? Host, int Port, string? Username, string? FromAddress, string? FromName, bool IsConfigured, bool HasPassword);

public sealed record UpdateEmailSettingsRequest(string Host, int Port, string Username, string FromAddress, string? FromName, string? Password);

public sealed record EmailTestResponse(bool Success, string Message);
