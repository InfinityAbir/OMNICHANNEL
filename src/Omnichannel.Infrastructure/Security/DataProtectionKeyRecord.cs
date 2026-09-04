namespace Omnichannel.Infrastructure.Security;

/// <summary>
/// Storage for ASP.NET Core's Data Protection key ring — not a tenant-owned business entity (no
/// <c>ITenantOwned</c>, no EF tenant query filter), since the key ring is one shared, app-wide
/// resource that TenantSecret encryption (and every other Data Protection consumer in this app)
/// depends on regardless of tenant. Persisted here rather than the container's local filesystem
/// because a hosting platform's filesystem (Render, and most PaaS/container platforms) is
/// ephemeral: without this, every redeploy or restart would silently generate a fresh key ring
/// and permanently strand every previously-encrypted TenantSecret/ChannelCredential value. See
/// <see cref="EfXmlRepository"/>.
/// </summary>
public sealed class DataProtectionKeyRecord
{
    public int Id { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
}
