using Omnichannel.Domain.Security;

namespace Omnichannel.UnitTests.Domain;

public class TenantSecretTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();

        var secret = TenantSecret.Create(tenantId, "smtp.password", "encrypted-blob", Now);

        Assert.Equal(tenantId, secret.TenantId);
        Assert.Equal("smtp.password", secret.Purpose);
        Assert.Equal("encrypted-blob", secret.EncryptedValue);
        Assert.Equal(Now, secret.CreatedAt);
        Assert.Equal(Now, secret.UpdatedAt);
        Assert.NotEqual(Guid.Empty, secret.Id);
    }

    [Fact]
    public void Rotate_ReplacesEncryptedValueAndBumpsUpdatedAt_LeavesCreatedAtUnchanged()
    {
        var secret = TenantSecret.Create(Guid.NewGuid(), "ai.apikey", "old-encrypted", Now);
        var later = Now.AddDays(1);

        secret.Rotate("new-encrypted", later);

        Assert.Equal("new-encrypted", secret.EncryptedValue);
        Assert.Equal(later, secret.UpdatedAt);
        Assert.Equal(Now, secret.CreatedAt);
    }
}
