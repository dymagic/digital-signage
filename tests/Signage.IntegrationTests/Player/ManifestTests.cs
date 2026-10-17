using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;
using Signage.Web.Features.Pairing;

namespace Signage.IntegrationTests.Player;

/// <summary>WP-08: the player manifest (ARCHITECTURE §4), its ETag, schedule windows (R-24) and the admin preview (R-13).</summary>
public sealed class ManifestTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private Guid _screenId;
    private PlayerClient _device = null!;
    private Slide _now = null!, _future = null!, _expired = null!, _video = null!, _off = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        var now = _app.Time.GetUtcNow();
        _now = new Slide { Title = "now", Type = SlideType.Html, Html = "<p>now</p>", DurationSeconds = 7 };
        _future = new Slide { Title = "future", Type = SlideType.Image, Url = "https://example.org/f.png", ActiveFromUtc = now.AddHours(1) };
        _expired = new Slide { Title = "expired", Type = SlideType.Image, Url = "https://example.org/e.png", ActiveUntilUtc = now.AddMinutes(-1) };
        _video = new Slide { Title = "video", Type = SlideType.Video, Url = "https://example.org/v.mp4", DurationSeconds = 99, PlayAudio = true };
        _off = new Slide { Title = "off", Type = SlideType.Html, Html = "x", IsEnabled = false };
        var playlist = new Playlist { Name = "P" };
        var all = new[] { _now, _future, _expired, _video, _off };
        for (var i = 0; i < all.Length; i++)
        {
            playlist.Slides.Add(new PlaylistSlide { Slide = all[i], Position = i });
        }
        var screen = new Screen { Name = "Lobby TV" };
        screen.Playlists.Add(new ScreenPlaylist { Playlist = playlist, Position = 0 });
        await _app.Factory.WithDbAsync(async db =>
        {
            db.Screens.Add(screen);
            await db.SaveChangesAsync();
        });
        _screenId = screen.Id;

        await using var scope = _app.Factory.Services.CreateAsyncScope();
        var token = await scope.ServiceProvider.GetRequiredService<ScreenAccessService>().CreateAddressTokenAsync(_screenId, TestContext.Current.CancellationToken);
        _device = new PlayerClient(_app.Factory);
        (await _device.ExchangeAsync(token!)).Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _device.Dispose();
        await _app.DisposeAsync();
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task The_manifest_lists_enabled_slides_in_sequence_order_with_their_windows_but_not_expired_ones()
    {
        using var response = await _device.GetManifestAsync();
        var json = Json(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Lobby TV", json.GetProperty("screen").GetProperty("name").GetString());
        Assert.Equal("Carousel", json.GetProperty("mode").GetString());
        Assert.Equal(10, json.GetProperty("settings").GetProperty("defaultDuration").GetInt32());
        Assert.Equal("UTC", json.GetProperty("settings").GetProperty("timeZone").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("appVersion").GetString()));
        var slides = json.GetProperty("slides").EnumerateArray().ToList();
        Assert.Equal([_now.Id, _future.Id, _video.Id], slides.Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal(7, slides[0].GetProperty("durationSeconds").GetInt32());
        Assert.Equal("<p>now</p>", slides[0].GetProperty("html").GetString());
        Assert.Equal(_future.ActiveFromUtc, slides[1].GetProperty("activeFromUtc").GetDateTimeOffset());
        Assert.Equal(10, slides[1].GetProperty("durationSeconds").GetInt32()); // the default, resolved
        Assert.False(slides[2].TryGetProperty("durationSeconds", out _)); // video: plays to the end
        Assert.True(slides[2].GetProperty("playAudio").GetBoolean());
    }

    [Fact]
    public async Task An_unchanged_manifest_answers_304_and_any_change_gives_a_new_etag()
    {
        using var first = await _device.GetManifestAsync();
        var etag = first.Headers.ETag!.Tag;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/player/manifest", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var unchanged = await _device.Http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);

        await _app.Factory.WithDbAsync(db => db.SystemSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultSlideDurationSeconds, 15)));
        using var again = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/player/manifest", UriKind.Relative));
        again.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var changed = await _device.Http.SendAsync(again, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(etag, changed.Headers.ETag!.Tag);
        Assert.Equal("no-cache", changed.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Editors_and_admins_can_preview_a_screen_with_their_admin_cookie()
    {
        await _app.CreateUserAsync();
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);

        using var response = await editor.GetAsync($"/api/player/preview/{_screenId}/manifest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = Json(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, json.GetProperty("slides").GetArrayLength());
        Assert.False(json.TryGetProperty("observedIp", out _));
        var html = await editor.GetHtmlAsync($"/admin/screens/{_screenId}");
        Assert.Contains($"href=\"/screen?preview={_screenId}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_without_an_admin_session_is_a_401_not_a_redirect()
    {
        using var anonymous = new AdminSession(_app.Factory);

        using var response = await anonymous.GetAsync($"/api/player/preview/{_screenId}/manifest");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_screen_token_is_not_an_admin_session_for_previews()
    {
        using var response = await _device.Http.GetAsync(new Uri($"/api/player/preview/{_screenId}/manifest", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_who_must_change_their_password_cannot_preview()
    {
        await using var scope = _app.Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<Signage.Web.Features.Users.UserAccounts>();
        var (_, temporary) = await accounts.CreateAsync("fresh@example.org", "Fresh", UserRole.Editor, TestContext.Current.CancellationToken);
        using var fresh = new AdminSession(_app.Factory);
        await fresh.SignInSuccessfullyAsync("fresh@example.org", temporary!);

        using var response = await fresh.GetAsync($"/api/player/preview/{_screenId}/manifest");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Previewing_an_unknown_screen_is_a_404()
    {
        using var admin = await _app.SignedInAsync(AdminApp.AdminEmail);

        using var response = await admin.GetAsync($"/api/player/preview/{Guid.NewGuid()}/manifest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Html_slide_previews_in_the_admin_use_the_shared_base_style()
    {
        using var admin = await _app.SignedInAsync(AdminApp.AdminEmail);

        var html = WebUtility.HtmlDecode(await admin.GetHtmlAsync($"/admin/slides/{_now.Id}"));

        Assert.Contains(Signage.Web.Features.Slides.HtmlSlideDocument.BaseStyle, html, StringComparison.Ordinal);
    }
}
