using Omnichannel.Domain.Tenancy;

namespace Omnichannel.UnitTests.Domain;

public class TenantTests
{
    [Fact]
    public void Create_WithValidInput_SetsActiveStatus()
    {
        var tenant = Tenant.Create("Acme Ltd", "acme-ltd", "Asia/Dhaka", DateTimeOffset.UtcNow);

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal("acme-ltd", tenant.Slug);
    }

    [Fact]
    public void Create_WithEmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create("", "slug", "UTC", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_WithEmptyTimeZone_DefaultsToUtc()
    {
        var tenant = Tenant.Create("Acme", "acme", "", DateTimeOffset.UtcNow);

        Assert.Equal("UTC", tenant.TimeZone);
    }

    [Fact]
    public void Suspend_SetsStatusToSuspended()
    {
        var tenant = Tenant.Create("Acme", "acme", "UTC", DateTimeOffset.UtcNow);

        tenant.Suspend(DateTimeOffset.UtcNow);

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
    }
}
