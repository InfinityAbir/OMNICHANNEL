using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Security;

namespace Omnichannel.Infrastructure.Security;

/// <summary>
/// Same Data Protection encryption-at-rest approach as <c>DataProtectionChannelCredentialStore</c>
/// (ADR-0016), generalized to any per-tenant secret keyed by a purpose string rather than a
/// ChannelAccount id.
/// </summary>
public sealed class DataProtectionTenantSecretStore : ITenantSecretStore
{
    private const string PurposeString = "Omnichannel.TenantSecrets.v1";

    private readonly IAppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IDataProtector _protector;

    public DataProtectionTenantSecretStore(IAppDbContext db, TimeProvider timeProvider, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
        _protector = dataProtectionProvider.CreateProtector(PurposeString);
    }

    public async Task SetAsync(Guid tenantId, string purpose, string plaintextSecret, CancellationToken cancellationToken)
    {
        var encrypted = _protector.Protect(plaintextSecret);
        var now = _timeProvider.GetUtcNow();

        var existing = await _db.TenantSecrets.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId && s.Purpose == purpose, cancellationToken);
        if (existing is null)
        {
            _db.TenantSecrets.Add(TenantSecret.Create(tenantId, purpose, encrypted, now));
        }
        else
        {
            existing.Rotate(encrypted, now);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(Guid tenantId, string purpose, CancellationToken cancellationToken)
    {
        var existing = await _db.TenantSecrets.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId && s.Purpose == purpose, cancellationToken);
        return existing is null ? null : _protector.Unprotect(existing.EncryptedValue);
    }

    public async Task<bool> ExistsAsync(Guid tenantId, string purpose, CancellationToken cancellationToken)
        => await _db.TenantSecrets.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId && s.Purpose == purpose, cancellationToken);

    public async Task DeleteAsync(Guid tenantId, string purpose, CancellationToken cancellationToken)
    {
        var existing = await _db.TenantSecrets.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId && s.Purpose == purpose, cancellationToken);
        if (existing is not null)
        {
            _db.TenantSecrets.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
