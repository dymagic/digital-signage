using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>WP-03 slide library: R-01–R-07 through the admin pages, as an editor.</summary>
public sealed partial class SlidePagesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private AdminSession _editor = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        await _app.CreateUserAsync();
        _editor = await _app.SignedInAsync(AdminApp.EditorEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _editor.Dispose();
        await _app.DisposeAsync();
    }

    private static Dictionary<string, string> Form(string title = "Lunch menu", string? url = "https://example.org/menu.png", string? html = null) => new()
    {
        ["Input.Title"] = title,
        ["Input.Url"] = url ?? "",
        ["Input.Html"] = html ?? "",
        ["Input.IsEnabled"] = "true",
    };

    private async Task<HttpResponseMessage> CreateAsync(SlideType type, Dictionary<string, string> form) =>
        await _editor.PostFormAsync($"/admin/slides/create?type={type}", form);

    private async Task<Slide> CreateOkAsync(SlideType type, Dictionary<string, string> form)
    {
        using var response = await CreateAsync(type, form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var id = Guid.Parse(AdminSession.Location(response)!.Split('/')[^1]);
        return await _app.Factory.WithDbAsync(db => db.Slides.AsNoTracking().SingleAsync(s => s.Id == id));
    }

    private Task<int> SetTimeZoneAsync(string zone) =>
        _app.Factory.WithDbAsync(db => db.SystemSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.TimeZoneId, zone)));

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task An_editor_creates_an_image_slide_and_lands_on_its_edit_page()
    {
        var form = Form();
        form["Input.DurationSeconds"] = "15";

        var slide = await CreateOkAsync(SlideType.Image, form);

        Assert.Equal(SlideType.Image, slide.Type);
        Assert.Equal("Lunch menu", slide.Title);
        Assert.Equal("https://example.org/menu.png", slide.Url);
        Assert.Equal(15, slide.DurationSeconds);
        Assert.True(slide.IsEnabled);
        Assert.Equal(_app.Time.GetUtcNow(), slide.CreatedAt);
        var edit = await _editor.GetHtmlAsync($"/admin/slides/{slide.Id}");
        Assert.Contains("Slide created.", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_slide_type_keeps_only_its_own_fields()
    {
        var html = await CreateOkAsync(SlideType.Html, Form(url: "https://example.org/ignored", html: "<h1>Hello</h1>"));
        var page = await CreateOkAsync(SlideType.WebPage, Form(url: "https://intranet.example.org/news", html: "<p>ignored</p>"));
        var videoForm = Form(url: "https://example.org/clip.mp4");
        videoForm["Input.DurationSeconds"] = "99";
        videoForm["Input.PlayAudio"] = "true";
        var video = await CreateOkAsync(SlideType.Video, videoForm);

        Assert.Equal(("<h1>Hello</h1>", (string?)null), (html.Html, html.Url));
        Assert.Equal(((string?)null, "https://intranet.example.org/news"), (page.Html, page.Url));
        Assert.Null(video.DurationSeconds);
        Assert.True(video.PlayAudio);
    }

    [Theory]
    [InlineData("", "https://example.org/a.png", "Enter a title.")]
    [InlineData("Menu", "", "Enter the address (URL).")]
    [InlineData("Menu", "javascript:alert(1)", "Enter a full address starting with http:// or https://.")]
    [InlineData("Menu", "ftp://example.org/a.png", "Enter a full address starting with http:// or https://.")]
    public async Task Invalid_input_is_shown_next_to_the_field_and_nothing_is_saved(string title, string url, string message)
    {
        using var response = await CreateAsync(SlideType.Image, Form(title, url));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, await BodyAsync(response), StringComparison.Ordinal);
        Assert.Equal(0, await _app.Factory.WithDbAsync(db => db.Slides.CountAsync()));
    }

    [Fact]
    public async Task Html_slides_need_html()
    {
        using var response = await CreateAsync(SlideType.Html, Form(html: " "));

        Assert.Contains("Enter the HTML to show.", await BodyAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_type_is_a_404()
    {
        using var response = await _editor.GetAsync("/admin/slides/create?type=Hologram");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_non_mp4_video_address_saves_with_a_warning()
    {
        var slide = await CreateOkAsync(SlideType.Video, Form(url: "https://cdn.example.org/stream?id=1"));

        var edit = await _editor.GetHtmlAsync($"/admin/slides/{slide.Id}");
        Assert.Contains("doesn't end in .mp4", WebUtility.HtmlDecode(edit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schedule_times_are_entered_in_the_system_time_zone_and_stored_as_utc()
    {
        await SetTimeZoneAsync("Europe/Copenhagen");
        var form = Form();
        form["Input.ActiveFrom"] = "2026-12-24T08:00";
        form["Input.ActiveUntil"] = "2027-07-01T18:30";

        var slide = await CreateOkAsync(SlideType.Image, form);

        Assert.Equal(new DateTimeOffset(2026, 12, 24, 7, 0, 0, TimeSpan.Zero), slide.ActiveFromUtc);
        Assert.Equal(new DateTimeOffset(2027, 7, 1, 16, 30, 0, TimeSpan.Zero), slide.ActiveUntilUtc);
        var edit = await _editor.GetHtmlAsync($"/admin/slides/{slide.Id}");
        Assert.Contains("value=\"2026-12-24T08:00\"", edit, StringComparison.Ordinal);
        Assert.Contains("value=\"2027-07-01T18:30\"", edit, StringComparison.Ordinal);
        Assert.Contains("Europe/Copenhagen", edit, StringComparison.Ordinal);
        var list = await _editor.GetHtmlAsync("/admin/slides");
        Assert.Contains("2026-12-24 08:00 – 2027-07-01 18:30", WebUtility.HtmlDecode(list), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_time_skipped_by_daylight_saving_is_rejected()
    {
        await SetTimeZoneAsync("Europe/Copenhagen");
        var form = Form();
        form["Input.ActiveFrom"] = "2026-03-29T02:30";

        using var response = await CreateAsync(SlideType.Image, form);

        Assert.Contains("That time doesn't exist in this time zone", await BodyAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Until_before_from_is_rejected()
    {
        var form = Form();
        form["Input.ActiveFrom"] = "2026-12-24T08:00";
        form["Input.ActiveUntil"] = "2026-12-24T07:00";

        using var response = await CreateAsync(SlideType.Image, form);

        Assert.Contains("\"Active until\" must be after \"Active from\".", await BodyAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editing_saves_changes_and_updates_the_timestamp()
    {
        var slide = await CreateOkAsync(SlideType.Image, Form());
        _app.Time.Advance(TimeSpan.FromMinutes(3));
        var form = Form(title: "Dinner menu", url: "https://example.org/dinner.png");
        form["Version"] = slide.Version.ToString(CultureInfo.InvariantCulture);

        using var response = await _editor.PostFormAsync($"/admin/slides/{slide.Id}", form);

        Assert.Equal($"/admin/slides/{slide.Id}", AdminSession.Location(response));
        var saved = await _app.Factory.WithDbAsync(db => db.Slides.AsNoTracking().SingleAsync(s => s.Id == slide.Id));
        Assert.Equal(("Dinner menu", "https://example.org/dinner.png"), (saved.Title, saved.Url));
        Assert.Equal(slide.CreatedAt, saved.CreatedAt);
        Assert.Equal(slide.CreatedAt.AddMinutes(3), saved.UpdatedAt);
    }

    [Fact]
    public async Task A_stale_edit_form_reports_a_conflict()
    {
        var slide = await CreateOkAsync(SlideType.Image, Form());
        var first = Form(title: "First");
        first["Version"] = slide.Version.ToString(CultureInfo.InvariantCulture);
        (await _editor.PostFormAsync($"/admin/slides/{slide.Id}", first)).Dispose();

        var second = Form(title: "Second");
        second["Version"] = slide.Version.ToString(CultureInfo.InvariantCulture);
        using var response = await _editor.PostFormAsync($"/admin/slides/{slide.Id}", second);

        Assert.Contains("Someone else changed this in the meantime.", await BodyAsync(response), StringComparison.Ordinal);
        Assert.Equal("First", (await _app.Factory.WithDbAsync(db => db.Slides.SingleAsync())).Title);
    }

    [Fact]
    public async Task Slides_can_be_switched_off_and_on_from_the_list()
    {
        var slide = await CreateOkAsync(SlideType.Image, Form());
        _app.Time.Advance(TimeSpan.FromMinutes(1));

        using (var off = await _editor.PostFormAsync("/admin/slides", new Dictionary<string, string>(), action: $"/admin/slides?handler=Toggle&id={slide.Id}&enabled=false"))
        {
            Assert.Equal("/admin/slides", AdminSession.Location(off));
        }

        var disabled = await _app.Factory.WithDbAsync(db => db.Slides.AsNoTracking().SingleAsync());
        Assert.False(disabled.IsEnabled);
        Assert.Equal(slide.CreatedAt.AddMinutes(1), disabled.UpdatedAt);

        (await _editor.PostFormAsync("/admin/slides", new Dictionary<string, string>(), action: $"/admin/slides?handler=Toggle&id={slide.Id}&enabled=true")).Dispose();
        Assert.True((await _app.Factory.WithDbAsync(db => db.Slides.AsNoTracking().SingleAsync())).IsEnabled);
    }

    [Fact]
    public async Task Search_matches_titles_case_insensitively_and_treats_wildcards_literally()
    {
        await CreateOkAsync(SlideType.Image, Form(title: "Canteen Menu"));
        await CreateOkAsync(SlideType.Image, Form(title: "Quarterly results"));
        await CreateOkAsync(SlideType.Image, Form(title: "100% happy"));

        var menu = await _editor.GetHtmlAsync("/admin/slides?q=menu");
        var percent = await _editor.GetHtmlAsync("/admin/slides?q=%25");
        var none = await _editor.GetHtmlAsync("/admin/slides?q=zzz");

        Assert.Contains("Canteen Menu", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Quarterly results", menu, StringComparison.Ordinal);
        Assert.Contains("100% happy", percent, StringComparison.Ordinal);
        Assert.DoesNotContain("Canteen Menu", percent, StringComparison.Ordinal);
        Assert.Contains("No slides match your search.", WebUtility.HtmlDecode(none), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_slide_in_use_shows_where_and_removes_the_links()
    {
        var slide = await CreateOkAsync(SlideType.Image, Form());
        await _app.Factory.WithDbAsync(async db =>
        {
            var playlist = new Playlist { Name = "Canteen" };
            playlist.Slides.Add(new PlaylistSlide { SlideId = slide.Id, Position = 0 });
            var screen = new Screen { Name = "Lobby TV" };
            screen.Slides.Add(new ScreenSlide { SlideId = slide.Id, Placement = SlidePlacement.Append, Position = 0 });
            db.AddRange(playlist, screen);
            await db.SaveChangesAsync();
        });

        var confirm = WebUtility.HtmlDecode(await _editor.GetHtmlAsync($"/admin/slides/{slide.Id}/delete"));
        Assert.Contains("This slide is in use.", confirm, StringComparison.Ordinal);
        Assert.Contains("Canteen", confirm, StringComparison.Ordinal);
        Assert.Contains("Lobby TV", confirm, StringComparison.Ordinal);
        var list = WebUtility.HtmlDecode(await _editor.GetHtmlAsync("/admin/slides"));
        Assert.Contains("Playlists: 1 · Screens: 1", list, StringComparison.Ordinal);

        using var deleted = await _editor.PostFormAsync($"/admin/slides/{slide.Id}/delete", new Dictionary<string, string>());

        Assert.Equal("/admin/slides", AdminSession.Location(deleted));
        await _app.Factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Slides.CountAsync());
            Assert.Equal(0, await db.PlaylistSlides.CountAsync());
            Assert.Equal(0, await db.ScreenSlides.CountAsync());
            Assert.Equal(1, await db.Playlists.CountAsync());
        });
    }

    [Theory]
    [InlineData("/admin/slides/{0}")]
    [InlineData("/admin/slides/{0}/delete")]
    public async Task Unknown_slides_are_a_404(string path)
    {
        using var response = await _editor.GetAsync(string.Format(CultureInfo.InvariantCulture, path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Titles_and_html_are_encoded_and_html_previews_never_get_same_origin()
    {
        var slide = await CreateOkAsync(SlideType.Html, Form(title: "<script>alert('t')</script>", html: "<script>parent.document.title='x'</script>\"><img src=x onerror=alert(1)>"));

        var list = await _editor.GetHtmlAsync("/admin/slides");
        var edit = await _editor.GetHtmlAsync($"/admin/slides/{slide.Id}");

        foreach (var html in new[] { list, edit })
        {
            Assert.DoesNotContain("<script>alert('t')</script>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<img src=x onerror", html, StringComparison.Ordinal);
            var frames = SrcdocFrame().Matches(html);
            Assert.NotEmpty(frames);
            Assert.All(frames, f => Assert.Equal("allow-scripts", f.Groups["sandbox"].Value));
        }
    }

    [GeneratedRegex("""<iframe srcdoc="[^"]*" sandbox="(?<sandbox>[^"]*)"[^>]*>""")]
    private static partial Regex SrcdocFrame();
}
