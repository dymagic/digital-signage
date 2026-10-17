using Signage.Web.Features.Slides;

namespace Signage.UnitTests.Slides;

public class ScheduleClockTests
{
    private static readonly TimeZoneInfo Copenhagen = ScheduleClock.Resolve("Europe/Copenhagen");

    [Fact]
    public void Summer_wall_clock_times_convert_with_the_summer_offset()
    {
        Assert.True(ScheduleClock.TryToUtc(new DateTime(2026, 7, 1, 9, 0, 0), Copenhagen, out var utc));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void Winter_wall_clock_times_convert_with_the_winter_offset()
    {
        Assert.True(ScheduleClock.TryToUtc(new DateTime(2026, 1, 15, 9, 0, 0), Copenhagen, out var utc));
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void A_time_in_the_spring_forward_gap_does_not_exist()
    {
        // 29 March 2026: clocks jump from 02:00 to 03:00 in Copenhagen.
        Assert.False(ScheduleClock.TryToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Copenhagen, out _));
    }

    [Fact]
    public void An_ambiguous_fall_back_time_means_the_standard_time_occurrence()
    {
        // 25 October 2026: 02:30 happens twice; standard time (UTC+1) is the later instant.
        Assert.True(ScheduleClock.TryToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Copenhagen, out var utc));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void Converting_back_gives_the_wall_clock_time_that_was_entered()
    {
        var entered = new DateTime(2026, 11, 3, 17, 45, 0);
        Assert.True(ScheduleClock.TryToUtc(entered, Copenhagen, out var utc));

        Assert.Equal(entered, ScheduleClock.ToWallClock(utc, Copenhagen));
    }

    [Fact]
    public void The_kind_of_the_entered_value_is_ignored()
    {
        var local = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Local);
        var utcKind = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.True(ScheduleClock.TryToUtc(local, Copenhagen, out var a));
        Assert.True(ScheduleClock.TryToUtc(utcKind, Copenhagen, out var b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Unknown_time_zone_ids_fall_back_to_utc()
    {
        Assert.Equal(TimeZoneInfo.Utc, ScheduleClock.Resolve("Nowhere/Nothing"));
    }
}
