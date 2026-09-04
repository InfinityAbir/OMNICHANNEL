using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Automation;

namespace Omnichannel.UnitTests.Domain;

public class TenantBusinessHoursTests
{
    // 2026-09-04 is a Friday.
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_IsClosed()
    {
        var hours = TenantBusinessHours.CreateDefault(Guid.NewGuid(), Now);

        Assert.False(hours.IsOpenNow(Now, "UTC"));
    }

    [Fact]
    public void IsOpenNow_WithinConfiguredWindow_True()
    {
        var hours = TenantBusinessHours.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(9, 0), new TimeOnly(18, 0))],
        };
        hours.Configure(schedule, [], Now);

        Assert.True(hours.IsOpenNow(Now, "UTC"));
    }

    [Fact]
    public void IsOpenNow_OnHoliday_False_EvenWithinScheduledWindow()
    {
        var hours = TenantBusinessHours.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(9, 0), new TimeOnly(18, 0))],
        };
        hours.Configure(schedule, [DateOnly.FromDateTime(Now.Date)], Now);

        Assert.False(hours.IsOpenNow(Now, "UTC"));
    }

    [Fact]
    public void IsOpenNow_OutsideConfiguredWindow_False()
    {
        var hours = TenantBusinessHours.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(20, 0), new TimeOnly(23, 0))],
        };
        hours.Configure(schedule, [], Now);

        Assert.False(hours.IsOpenNow(Now, "UTC"));
    }

    [Fact]
    public void GetHolidays_RoundTrips()
    {
        var hours = TenantBusinessHours.CreateDefault(Guid.NewGuid(), Now);
        var holidays = new List<DateOnly> { new(2026, 12, 25), new(2026, 1, 1) };
        hours.Configure(null, holidays, Now);

        var roundTripped = hours.GetHolidays();

        Assert.Equal(2, roundTripped.Count);
        Assert.Contains(new DateOnly(2026, 12, 25), roundTripped);
    }

    [Fact]
    public void IsOpenNow_UnresolvableTimeZone_FalseInsteadOfThrowing()
    {
        var hours = TenantBusinessHours.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(0, 0), new TimeOnly(23, 59))],
        };
        hours.Configure(schedule, [], Now);

        Assert.False(hours.IsOpenNow(Now, "Not/A/Real/Zone"));
    }
}
