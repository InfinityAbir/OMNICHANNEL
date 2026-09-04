using Omnichannel.Domain.Security;

namespace Omnichannel.UnitTests.Domain;

public class JwtSigningKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatePrimary_IsPrimaryAndValidForValidation()
    {
        var key = JwtSigningKey.CreatePrimary("encrypted", Now);

        Assert.True(key.IsPrimary);
        Assert.Null(key.RetiredAt);
        Assert.True(key.IsValidForValidation(Now));
        Assert.True(key.IsValidForValidation(Now.AddYears(100))); // no RetiredAt = never expires on its own.
    }

    [Fact]
    public void Retire_DemotesFromPrimaryButStaysValidUntilRetiredAt()
    {
        var key = JwtSigningKey.CreatePrimary("encrypted", Now);
        var retiredAt = Now.AddHours(1);

        key.Retire(retiredAt);

        Assert.False(key.IsPrimary);
        Assert.Equal(retiredAt, key.RetiredAt);
        Assert.True(key.IsValidForValidation(Now)); // still within the overlap window.
        Assert.True(key.IsValidForValidation(retiredAt.AddSeconds(-1)));
    }

    [Fact]
    public void Retire_NoLongerValidAtOrAfterRetiredAt()
    {
        var key = JwtSigningKey.CreatePrimary("encrypted", Now);
        var retiredAt = Now.AddHours(1);

        key.Retire(retiredAt);

        Assert.False(key.IsValidForValidation(retiredAt));
        Assert.False(key.IsValidForValidation(retiredAt.AddSeconds(1)));
    }

    [Fact]
    public void Retire_WithZeroOverlap_IsImmediatelyInvalid()
    {
        var key = JwtSigningKey.CreatePrimary("encrypted", Now);

        key.Retire(Now);

        Assert.False(key.IsValidForValidation(Now));
    }
}
