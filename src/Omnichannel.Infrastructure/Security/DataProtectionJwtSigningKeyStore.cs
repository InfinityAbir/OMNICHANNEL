using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Omnichannel.Application.Abstractions;
using Omnichannel.Domain.Security;
using Omnichannel.Infrastructure.Persistence;

namespace Omnichannel.Infrastructure.Security;

/// <inheritdoc cref="IJwtSigningKeyStore" />
public sealed class DataProtectionJwtSigningKeyStore : IJwtSigningKeyStore
{
    private const string PurposeString = "Omnichannel.JwtSigningKeys.v1";
    private const int KeySizeBytes = 32; // 256 bits — meets HS256's recommended minimum key strength.

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;

    public DataProtectionJwtSigningKeyStore(
        AppDbContext db, TimeProvider timeProvider, IDataProtectionProvider dataProtectionProvider, IConfiguration configuration)
    {
        _db = db;
        _timeProvider = timeProvider;
        _protector = dataProtectionProvider.CreateProtector(PurposeString);
        _configuration = configuration;
    }

    public async Task<JwtSigningKeyMaterial> GetPrimaryAsync(CancellationToken cancellationToken)
    {
        var primary = await _db.JwtSigningKeys.SingleOrDefaultAsync(k => k.IsPrimary, cancellationToken);
        primary ??= await BootstrapPrimaryAsync(cancellationToken);
        return new JwtSigningKeyMaterial(primary.Id.ToString(), _protector.Unprotect(Convert.FromBase64String(primary.EncryptedKeyMaterial)));
    }

    public async Task<IReadOnlyList<JwtSigningKeyMaterial>> GetValidKeysAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var keys = await _db.JwtSigningKeys.ToListAsync(cancellationToken);
        if (keys.Count == 0)
        {
            var primary = await BootstrapPrimaryAsync(cancellationToken);
            keys = [primary];
        }

        return keys
            .Where(k => k.IsValidForValidation(now))
            .Select(k => new JwtSigningKeyMaterial(k.Id.ToString(), _protector.Unprotect(Convert.FromBase64String(k.EncryptedKeyMaterial))))
            .ToList();
    }

    public async Task<JwtKeyRotationResult> RotateAsync(TimeSpan overlapWindow, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var currentPrimary = await _db.JwtSigningKeys.SingleOrDefaultAsync(k => k.IsPrimary, cancellationToken);
        currentPrimary ??= await BootstrapPrimaryAsync(cancellationToken);

        var newKeyBytes = RandomNumberGenerator.GetBytes(KeySizeBytes);
        var newPrimary = JwtSigningKey.CreatePrimary(Convert.ToBase64String(_protector.Protect(newKeyBytes)), now);
        _db.JwtSigningKeys.Add(newPrimary);

        var retiredKeyValidUntil = now + overlapWindow;
        currentPrimary.Retire(retiredKeyValidUntil);

        await _db.SaveChangesAsync(cancellationToken);

        return new JwtKeyRotationResult(newPrimary.Id.ToString(), currentPrimary.Id.ToString(), retiredKeyValidUntil);
    }

    // Arbitrary fixed key for the session-level advisory lock below — only needs to be unique
    // within this database; RoleSeeder already uses 872364501 for the same reason, so this picks
    // a different constant.
    private const long BootstrapLockKey = 872364502;

    // Seeds the very first key: from the legacy Jwt:SigningKey config value if one is set (so an
    // upgrading deployment's already-issued tokens keep validating instead of every session being
    // logged out), otherwise a freshly generated random key (a brand-new deployment needs no
    // Jwt:SigningKey config at all anymore). A plain "check, then insert" race across concurrent
    // processes (e.g. multiple WebApplicationFactory test hosts starting at once against the same
    // shared database) produced both a duplicate-key violation and a Postgres deadlock for the
    // structurally identical RoleSeeder race — a session-level advisory lock serializes the whole
    // check-then-insert across connections here too; bootstrapping the key ring is a rare
    // startup-time operation, so losing concurrency here costs nothing.
    private async Task<JwtSigningKey> BootstrapPrimaryAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.CommandText = $"SELECT pg_advisory_lock({BootstrapLockKey})";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var existing = await _db.JwtSigningKeys.SingleOrDefaultAsync(k => k.IsPrimary, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var now = _timeProvider.GetUtcNow();
            var legacyKey = _configuration[$"{JwtOptionsSectionName}:SigningKey"];
            var keyBytes = string.IsNullOrWhiteSpace(legacyKey) ? RandomNumberGenerator.GetBytes(KeySizeBytes) : Encoding.UTF8.GetBytes(legacyKey);

            var seeded = JwtSigningKey.CreatePrimary(Convert.ToBase64String(_protector.Protect(keyBytes)), now);
            _db.JwtSigningKeys.Add(seeded);
            await _db.SaveChangesAsync(cancellationToken);
            return seeded;
        }
        finally
        {
            await using var unlockCommand = connection.CreateCommand();
            unlockCommand.CommandText = $"SELECT pg_advisory_unlock({BootstrapLockKey})";
            await unlockCommand.ExecuteNonQueryAsync(cancellationToken);

            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private const string JwtOptionsSectionName = "Jwt";
}
