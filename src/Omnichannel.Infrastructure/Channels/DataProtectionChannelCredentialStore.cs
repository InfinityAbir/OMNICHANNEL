using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Channels;

namespace Omnichannel.Infrastructure.Channels;

/// <summary>
/// Encrypts provider credentials at rest using ASP.NET Core's Data Protection API (the same
/// machinery already backing Identity's own tokens in this app — no new key-management surface).
/// Ciphertext only ever touches the database; plaintext exists solely inside Set/Get calls.
/// </summary>
public sealed class DataProtectionChannelCredentialStore : IChannelCredentialStore
{
    private const string PurposeString = "Omnichannel.ChannelCredentials.v1";

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly IDataProtector _protector;

    public DataProtectionChannelCredentialStore(
        IAppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
        _protector = dataProtectionProvider.CreateProtector(PurposeString);
    }

    public async Task SetAsync(Guid channelAccountId, string plaintextSecret, CancellationToken cancellationToken)
    {
        var encrypted = _protector.Protect(plaintextSecret);
        var now = _timeProvider.GetUtcNow();

        var existing = await _db.ChannelCredentials.SingleOrDefaultAsync(c => c.ChannelAccountId == channelAccountId, cancellationToken);
        if (existing is null)
        {
            _db.ChannelCredentials.Add(ChannelCredential.Create(_tenantContext.TenantId, channelAccountId, encrypted, now));
        }
        else
        {
            existing.Rotate(encrypted, now);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(Guid channelAccountId, CancellationToken cancellationToken)
    {
        var existing = await _db.ChannelCredentials.SingleOrDefaultAsync(c => c.ChannelAccountId == channelAccountId, cancellationToken);
        return existing is null ? null : _protector.Unprotect(existing.EncryptedSecret);
    }

    public async Task<bool> ExistsAsync(Guid channelAccountId, CancellationToken cancellationToken)
        => await _db.ChannelCredentials.AnyAsync(c => c.ChannelAccountId == channelAccountId, cancellationToken);

    public async Task DeleteAsync(Guid channelAccountId, CancellationToken cancellationToken)
    {
        var existing = await _db.ChannelCredentials.SingleOrDefaultAsync(c => c.ChannelAccountId == channelAccountId, cancellationToken);
        if (existing is not null)
        {
            _db.ChannelCredentials.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
