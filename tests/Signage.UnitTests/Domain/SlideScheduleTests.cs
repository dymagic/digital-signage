using Signage.Web.Domain;

namespace Signage.UnitTests.Domain;

public class SlideScheduleTests
{
    private static readonly DateTimeOffset T = new(2026, 3, 29, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null, 0, true)]
    [InlineData(0, null, -1, false)]
    [InlineData(0, null, 0, true)]
    [InlineData(0, null, 1, true)]
    [InlineData(null, 0, -1, true)]
    [InlineData(null, 0, 0, false)]
    [InlineData(null, 0, 1, false)]
    [InlineData(-10, 10, 0, true)]
    [InlineData(10, -10, 0, false)] // inverted window never plays
    [InlineData(0, 0, 0, false)]    // empty window never plays
    public void IsActive_uses_a_half_open_window(int? fromOffsetSeconds, int? untilOffsetSeconds, int nowOffsetSeconds, bool expected)
    {
        DateTimeOffset? from = fromOffsetSeconds is { } f ? T.AddSeconds(f) : null;
        DateTimeOffset? until = untilOffsetSeconds is { } u ? T.AddSeconds(u) : null;

        Assert.Equal(expected, SlideSchedule.IsActive(from, until, T.AddSeconds(nowOffsetSeconds)));
    }

    [Fact]
    public void IsActive_compares_instants_not_wall_clock_offsets()
    {
        var from = new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)); // 01:00Z
        Assert.True(SlideSchedule.IsActive(from, null, T));
        Assert.False(SlideSchedule.IsActive(from, null, T.AddTicks(-1)));
    }
}
