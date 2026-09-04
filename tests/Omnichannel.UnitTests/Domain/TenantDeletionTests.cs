using Omnichannel.Domain.Tenancy;

namespace Omnichannel.UnitTests.Domain;

public class TenantDeletionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScheduleDeletion_SetsPendingDeletionAndScheduledDate()
    {
        var tenant = Tenant.Create("Test Biz", "test-biz", "UTC", Now);
        var scheduledAt = Now.AddDays(14);

        tenant.ScheduleDeletion(scheduledAt, Now);

        Assert.Equal(TenantStatus.PendingDeletion, tenant.Status);
        Assert.Equal(scheduledAt, tenant.ScheduledDeletionAt);
    }

    [Fact]
    public void CancelScheduledDeletion_RevertsToActive()
    {
        var tenant = Tenant.Create("Test Biz", "test-biz", "UTC", Now);
        tenant.ScheduleDeletion(Now.AddDays(14), Now);

        tenant.CancelScheduledDeletion(Now.AddDays(1));

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Null(tenant.ScheduledDeletionAt);
    }

    [Fact]
    public void CancelScheduledDeletion_WhenNotPending_Throws()
    {
        var tenant = Tenant.Create("Test Biz", "test-biz", "UTC", Now);

        Assert.Throws<InvalidOperationException>(() => tenant.CancelScheduledDeletion(Now));
    }

    [Fact]
    public void MarkDeleted_IsTerminalAndClearsScheduledDate()
    {
        var tenant = Tenant.Create("Test Biz", "test-biz", "UTC", Now);
        tenant.ScheduleDeletion(Now.AddDays(14), Now);

        tenant.MarkDeleted(Now.AddDays(14));

        Assert.Equal(TenantStatus.Deleted, tenant.Status);
        Assert.Null(tenant.ScheduledDeletionAt);
    }
}
