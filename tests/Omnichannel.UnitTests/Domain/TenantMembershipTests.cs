using Omnichannel.Domain.Tenancy;

namespace Omnichannel.UnitTests.Domain;

public class TenantMembershipTests
{
    [Fact]
    public void Create_SetsActiveStatus()
    {
        var membership = TenantMembership.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(MembershipStatus.Active, membership.Status);
    }

    [Fact]
    public void Remove_SetsStatusToRemoved()
    {
        var membership = TenantMembership.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        membership.Remove(DateTimeOffset.UtcNow);

        Assert.Equal(MembershipStatus.Removed, membership.Status);
    }
}
