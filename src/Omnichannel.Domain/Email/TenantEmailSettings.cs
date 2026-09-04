using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Email;

/// <summary>
/// Per-tenant SMTP configuration — one row per tenant, unconfigured by default. The app password
/// itself is never stored here: it lives in <see cref="Security.TenantSecret"/> (purpose
/// "smtp.password"), encrypted at rest. When a tenant hasn't configured their own SMTP, the
/// platform falls back to its own default SMTP config (the existing global `SmtpOptions`) — same
/// fallback pattern as <see cref="Ai.TenantAiProviderSettings"/> (ADR-0027).
/// </summary>
public sealed class TenantEmailSettings : ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string? Host { get; private set; }
    public int Port { get; private set; } = 587;
    public string? Username { get; private set; }
    public string? FromAddress { get; private set; }
    public string? FromName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether this tenant has actually configured its own SMTP — distinct from "row exists", since the row is created empty by default.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username);

    private TenantEmailSettings()
    {
    }

    public static TenantEmailSettings CreateDefault(Guid tenantId, DateTimeOffset now)
        => new()
        {
            TenantId = tenantId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Configure(string host, int port, string username, string fromAddress, string? fromName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentException("Port must be between 1 and 65535.", nameof(port));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new ArgumentException("From address is required.", nameof(fromAddress));
        }

        Host = host.Trim();
        Port = port;
        Username = username.Trim();
        FromAddress = fromAddress.Trim();
        FromName = string.IsNullOrWhiteSpace(fromName) ? null : fromName.Trim();
        UpdatedAt = now;
    }

    public void Clear(DateTimeOffset now)
    {
        Host = null;
        Port = 587;
        Username = null;
        FromAddress = null;
        FromName = null;
        UpdatedAt = now;
    }
}
