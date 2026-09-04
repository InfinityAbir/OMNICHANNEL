using Omnichannel.Domain.Ai;

namespace Omnichannel.UnitTests.Domain;

public class AiAutoReplySettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_IsDisabledAndHasNoBusinessHours()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);

        Assert.False(settings.Enabled);
        Assert.Null(settings.BusinessHoursJson);
        Assert.False(settings.IsWithinBusinessHours(Now, "UTC"));
    }

    [Fact]
    public void IsWithinBusinessHours_UnconfiguredSchedule_AlwaysFalse()
    {
        // The conservative default PRD §71 calls for — no schedule configured means never
        // eligible, not "assume 24/7".
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);
        settings.Configure(true, 0.85, 50, null, Now);

        Assert.False(settings.IsWithinBusinessHours(Now, "UTC"));
    }

    [Fact]
    public void IsWithinBusinessHours_WithinConfiguredWindow_True()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);
        // 2026-09-04 is a Friday.
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(9, 0), new TimeOnly(18, 0))],
        };
        settings.Configure(true, 0.85, 50, schedule, Now);

        Assert.True(settings.IsWithinBusinessHours(Now, "UTC")); // Now is 12:00 UTC on that Friday.
    }

    [Fact]
    public void IsWithinBusinessHours_OutsideConfiguredWindow_False()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(20, 0), new TimeOnly(23, 0))],
        };
        settings.Configure(true, 0.85, 50, schedule, Now);

        Assert.False(settings.IsWithinBusinessHours(Now, "UTC")); // Now (12:00) is before the 20:00 window.
    }

    [Fact]
    public void IsWithinBusinessHours_DayNotInSchedule_False()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Saturday] = [new BusinessHoursWindow(new TimeOnly(0, 0), new TimeOnly(23, 59))],
        };
        settings.Configure(true, 0.85, 50, schedule, Now);

        Assert.False(settings.IsWithinBusinessHours(Now, "UTC")); // Now is Friday, only Saturday is scheduled.
    }

    [Fact]
    public void IsWithinBusinessHours_UnresolvableTimeZone_FalseInsteadOfThrowing()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Friday] = [new BusinessHoursWindow(new TimeOnly(0, 0), new TimeOnly(23, 59))],
        };
        settings.Configure(true, 0.85, 50, schedule, Now);

        Assert.False(settings.IsWithinBusinessHours(Now, "Not/A/Real/Zone"));
    }

    [Fact]
    public void Configure_ClampsConfidenceThresholdAndDailyLimit()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.Configure(true, 1.5, -10, null, Now);

        Assert.Equal(1.0, settings.ConfidenceThreshold);
        Assert.Equal(0, settings.DailyLimit);
    }

    [Fact]
    public void GetBusinessHours_RoundTripsThroughSerialization()
    {
        var settings = AiAutoReplySettings.CreateDefault(Guid.NewGuid(), Now);
        var schedule = new Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>
        {
            [DayOfWeek.Monday] = [new BusinessHoursWindow(new TimeOnly(9, 0), new TimeOnly(17, 0))],
            [DayOfWeek.Tuesday] = [new BusinessHoursWindow(new TimeOnly(9, 0), new TimeOnly(12, 0)), new BusinessHoursWindow(new TimeOnly(13, 0), new TimeOnly(17, 0))],
        };
        settings.Configure(true, 0.85, 50, schedule, Now);

        var roundTripped = settings.GetBusinessHours();

        Assert.Single(roundTripped[DayOfWeek.Monday]);
        Assert.Equal(2, roundTripped[DayOfWeek.Tuesday].Count);
        Assert.Equal(new TimeOnly(9, 0), roundTripped[DayOfWeek.Monday][0].Start);
    }
}
