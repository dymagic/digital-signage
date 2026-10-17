using Signage.Web.Domain;
using Signage.Web.Features.Slides;

namespace Signage.UnitTests.Slides;

public class SlideValidatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static SlideValidation Validate(SlideType type, Action<SlideInput> configure)
    {
        var input = new SlideInput { Title = "Lunch menu", Url = "https://example.org/menu.png", Html = "<h1>Hi</h1>" };
        configure(input);
        return SlideValidator.Validate(type, input, Utc);
    }

    [Theory]
    [InlineData(SlideType.Image)]
    [InlineData(SlideType.Video)]
    [InlineData(SlideType.WebPage)]
    [InlineData(SlideType.Html)]
    public void A_complete_slide_of_each_type_is_valid(SlideType type)
    {
        var result = Validate(type, i => i.Url = "https://example.org/a.mp4");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void A_title_is_required(string? title)
    {
        var result = Validate(SlideType.Image, i => i.Title = title);

        Assert.Equal(SlideErrors.TitleRequired, result.Errors[nameof(SlideInput.Title)]);
    }

    [Fact]
    public void Titles_are_trimmed_and_limited_in_length()
    {
        Assert.Equal("Menu", Validate(SlideType.Image, i => i.Title = "  Menu  ").Slide!.Title);
        Assert.Equal(SlideErrors.TitleTooLong, Validate(SlideType.Image, i => i.Title = new string('x', 201)).Errors[nameof(SlideInput.Title)]);
    }

    [Theory]
    [InlineData(SlideType.Image)]
    [InlineData(SlideType.Video)]
    [InlineData(SlideType.WebPage)]
    public void Url_types_require_a_url(SlideType type)
    {
        var result = Validate(type, i => i.Url = " ");

        Assert.Equal(SlideErrors.UrlRequired, result.Errors[nameof(SlideInput.Url)]);
    }

    [Theory]
    [InlineData("example.org/a.png")]
    [InlineData("/images/a.png")]
    [InlineData("ftp://example.org/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://")]
    public void Urls_must_be_absolute_http_or_https(string url)
    {
        var result = Validate(SlideType.Image, i => i.Url = url);

        Assert.Equal(SlideErrors.UrlInvalid, result.Errors[nameof(SlideInput.Url)]);
    }

    [Theory]
    [InlineData("http://intranet.local/menu.png")]
    [InlineData("HTTPS://Example.org/a.png?x=1#y")]
    [InlineData("http://10.0.0.5:8081/a.png")]
    public void Http_and_https_urls_including_lan_hosts_are_accepted(string url)
    {
        Assert.True(Validate(SlideType.Image, i => i.Url = url).IsValid);
    }

    [Fact]
    public void Html_slides_require_html_and_drop_the_url()
    {
        Assert.Equal(SlideErrors.HtmlRequired, Validate(SlideType.Html, i => i.Html = " ").Errors[nameof(SlideInput.Html)]);

        var valid = Validate(SlideType.Html, i => i.Url = "https://example.org/ignored").Slide!;
        Assert.Null(valid.Url);
        Assert.Equal("<h1>Hi</h1>", valid.Html);
    }

    [Fact]
    public void Url_slides_drop_any_html()
    {
        Assert.Null(Validate(SlideType.WebPage, i => i.Html = "<p>x</p>").Slide!.Html);
    }

    [Fact]
    public void Very_large_html_is_rejected()
    {
        var result = Validate(SlideType.Html, i => i.Html = new string('x', SlideValidator.MaxHtmlLength + 1));

        Assert.Equal(SlideErrors.HtmlTooLong, result.Errors[nameof(SlideInput.Html)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3601)]
    public void Durations_must_be_between_1_and_3600_seconds(int duration)
    {
        var result = Validate(SlideType.Image, i => i.DurationSeconds = duration);

        Assert.Equal(SlideErrors.DurationRange, result.Errors[nameof(SlideInput.DurationSeconds)]);
    }

    [Fact]
    public void No_duration_means_the_global_default()
    {
        Assert.Null(Validate(SlideType.Image, i => i.DurationSeconds = null).Slide!.DurationSeconds);
        Assert.Equal(30, Validate(SlideType.Image, i => i.DurationSeconds = 30).Slide!.DurationSeconds);
    }

    [Fact]
    public void Videos_ignore_the_duration_even_when_out_of_range()
    {
        var result = Validate(SlideType.Video, i => { i.Url = "https://example.org/a.mp4"; i.DurationSeconds = 0; });

        Assert.True(result.IsValid);
        Assert.Null(result.Slide!.DurationSeconds);
    }

    [Fact]
    public void Play_audio_only_sticks_on_videos()
    {
        Assert.True(Validate(SlideType.Video, i => { i.Url = "https://example.org/a.mp4"; i.PlayAudio = true; }).Slide!.PlayAudio);
        Assert.False(Validate(SlideType.Image, i => i.PlayAudio = true).Slide!.PlayAudio);
    }

    [Theory]
    [InlineData("https://cdn.example.org/clip.MP4", false)]
    [InlineData("https://cdn.example.org/clip.mp4?token=abc", false)]
    [InlineData("https://cdn.example.org/stream?id=7", true)]
    [InlineData("https://cdn.example.org/clip.webm", true)]
    public void A_video_url_that_does_not_end_in_mp4_only_warns(string url, bool warns)
    {
        var result = Validate(SlideType.Video, i => i.Url = url);

        Assert.True(result.IsValid);
        Assert.Equal(warns, result.Warnings.Contains(SlideErrors.VideoNotMp4Warning));
    }

    [Fact]
    public void Schedule_times_are_converted_to_utc_in_the_given_zone()
    {
        var zone = ScheduleClock.Resolve("Europe/Copenhagen");
        var input = new SlideInput { Title = "t", Url = "https://example.org/a.png", ActiveFrom = new DateTime(2026, 12, 24, 8, 0, 0), ActiveUntil = new DateTime(2026, 12, 24, 16, 0, 0) };

        var slide = SlideValidator.Validate(SlideType.Image, input, zone).Slide!;

        Assert.Equal(new DateTimeOffset(2026, 12, 24, 7, 0, 0, TimeSpan.Zero), slide.ActiveFromUtc);
        Assert.Equal(new DateTimeOffset(2026, 12, 24, 15, 0, 0, TimeSpan.Zero), slide.ActiveUntilUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Until_must_be_after_from(int minutesAfterFrom)
    {
        var from = new DateTime(2026, 12, 24, 8, 0, 0);
        var result = Validate(SlideType.Image, i => { i.ActiveFrom = from; i.ActiveUntil = from.AddMinutes(minutesAfterFrom); });

        Assert.Equal(SlideErrors.UntilBeforeFrom, result.Errors[nameof(SlideInput.ActiveUntil)]);
    }

    [Fact]
    public void Either_schedule_bound_may_be_left_open()
    {
        Assert.True(Validate(SlideType.Image, i => i.ActiveFrom = new DateTime(2026, 1, 1)).IsValid);
        Assert.True(Validate(SlideType.Image, i => i.ActiveUntil = new DateTime(2026, 1, 1)).IsValid);
    }

    [Fact]
    public void A_schedule_time_in_a_daylight_saving_gap_is_rejected()
    {
        var input = new SlideInput { Title = "t", Url = "https://example.org/a.png", ActiveFrom = new DateTime(2026, 3, 29, 2, 30, 0) };

        var result = SlideValidator.Validate(SlideType.Image, input, ScheduleClock.Resolve("Europe/Copenhagen"));

        Assert.Equal(SlideErrors.TimeDoesNotExist, result.Errors[nameof(SlideInput.ActiveFrom)]);
    }

    [Fact]
    public void All_errors_are_reported_at_once()
    {
        var result = Validate(SlideType.Image, i => { i.Title = ""; i.Url = "nope"; i.DurationSeconds = 0; });

        Assert.Equal(3, result.Errors.Count);
        Assert.Null(result.Slide);
    }
}
