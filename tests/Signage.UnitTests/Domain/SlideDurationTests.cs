using Signage.Web.Domain;

namespace Signage.UnitTests.Domain;

public class SlideDurationTests
{
    [Theory]
    [InlineData(SlideType.Image, null, 10)]
    [InlineData(SlideType.Image, 25, 25)]
    [InlineData(SlideType.WebPage, 5, 5)]
    [InlineData(SlideType.Html, null, 10)]
    [InlineData(SlideType.Html, 0, 10)]
    [InlineData(SlideType.Image, -3, 10)]
    public void Non_video_slides_use_their_own_duration_or_the_default(SlideType type, int? own, int expected)
    {
        var slide = new Slide { Title = "s", Type = type, DurationSeconds = own };

        Assert.Equal(expected, SlideDuration.Resolve(slide, defaultDurationSeconds: 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30)]
    public void Video_slides_have_no_duration_because_they_play_to_the_end(int? own)
    {
        var slide = new Slide { Title = "v", Type = SlideType.Video, DurationSeconds = own };

        Assert.Null(SlideDuration.Resolve(slide, defaultDurationSeconds: 10));
    }
}
