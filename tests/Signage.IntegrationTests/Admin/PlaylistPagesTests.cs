using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>WP-04 / R-08: playlists, their ordered slides, reordering and conflicts, as an editor.</summary>
public sealed class PlaylistPagesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private AdminSession _editor = null!;
    private static readonly string[] SlideTitles = ["A", "B", "C"];
    private Slide[] _slides = [];

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        await _app.CreateUserAsync();
        _editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        _slides = SlideTitles.Select(t => new Slide { Title = t, Type = SlideType.Html, Html = $"<p>{t}</p>" }).ToArray();
        await _app.Factory.WithDbAsync(async db =>
        {
            db.Slides.AddRange(_slides);
            await db.SaveChangesAsync();
        });
    }

    public async ValueTask DisposeAsync()
    {
        _editor.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<Guid> CreatePlaylistAsync(string name = "Canteen")
    {
        using var response = await _editor.PostFormAsync("/admin/playlists/create", new Dictionary<string, string> { ["Input.Name"] = name, ["Input.Description"] = "Menus" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(AdminSession.Location(response)!.Split('/')[^1]);
    }

    private Task<uint> VersionAsync(Guid id) => _app.Factory.WithDbAsync(db => db.Playlists.Where(p => p.Id == id).Select(p => p.Version).SingleAsync());

    private Task<List<(string Title, int Position)>> EntriesAsync(Guid id) => _app.Factory.WithDbAsync(async db =>
        (await db.PlaylistSlides.Where(ps => ps.PlaylistId == id).OrderBy(ps => ps.Position)
            .Select(ps => new { ps.Slide.Title, ps.Position }).ToListAsync())
        .Select(x => (x.Title, x.Position)).ToList());

    private Task<List<Guid>> EntryIdsAsync(Guid id) => _app.Factory.WithDbAsync(db =>
        db.PlaylistSlides.Where(ps => ps.PlaylistId == id).OrderBy(ps => ps.Position).Select(ps => ps.Id).ToListAsync());

    private async Task<HttpResponseMessage> HtmxAsync(Guid id, string handler, Dictionary<string, string> fields, uint? version = null)
    {
        fields["Version"] = (version ?? await VersionAsync(id)).ToString(CultureInfo.InvariantCulture);
        return await _editor.PostHtmxAsync($"/admin/playlists/{id}", $"/admin/playlists/{id}?handler={handler}", fields);
    }

    private async Task AddAsync(Guid id, params Slide[] slides)
    {
        foreach (var slide in slides)
        {
            using var response = await HtmxAsync(id, "Add", new() { ["slideId"] = slide.Id.ToString() });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_editor_creates_a_playlist_and_adds_slides_in_order_including_duplicates()
    {
        var id = await CreatePlaylistAsync();

        await AddAsync(id, _slides[0], _slides[1], _slides[0]);

        Assert.Equal([("A", 0), ("B", 1), ("A", 2)], await EntriesAsync(id));
        var html = await _editor.GetHtmlAsync($"/admin/playlists/{id}");
        Assert.Contains("Canteen", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_an_entry_rewrites_positions_densely()
    {
        var id = await CreatePlaylistAsync();
        await AddAsync(id, _slides);
        var ids = await EntryIdsAsync(id);

        (await HtmxAsync(id, "Move", new() { ["entryId"] = ids[2].ToString(), ["toIndex"] = "0" })).Dispose();
        Assert.Equal([("C", 0), ("A", 1), ("B", 2)], await EntriesAsync(id));

        (await HtmxAsync(id, "Move", new() { ["entryId"] = ids[2].ToString(), ["toIndex"] = "99" })).Dispose();
        Assert.Equal([("A", 0), ("B", 1), ("C", 2)], await EntriesAsync(id));

        (await HtmxAsync(id, "Move", new() { ["entryId"] = ids[0].ToString(), ["toIndex"] = "-5" })).Dispose();
        Assert.Equal([("A", 0), ("B", 1), ("C", 2)], await EntriesAsync(id));
    }

    [Fact]
    public async Task Removing_an_entry_closes_the_gap()
    {
        var id = await CreatePlaylistAsync();
        await AddAsync(id, _slides);
        var ids = await EntryIdsAsync(id);

        (await HtmxAsync(id, "Remove", new() { ["entryId"] = ids[1].ToString() })).Dispose();

        Assert.Equal([("A", 0), ("C", 1)], await EntriesAsync(id));
        Assert.Equal(3, await _app.Factory.WithDbAsync(db => db.Slides.CountAsync()));
    }

    [Fact]
    public async Task Htmx_requests_get_the_entries_block_with_a_fresh_version_and_plain_posts_get_a_redirect()
    {
        var id = await CreatePlaylistAsync();

        using var htmx = await HtmxAsync(id, "Add", new() { ["slideId"] = _slides[0].Id.ToString() });
        var fragment = await htmx.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("<div id=\"entries\"", fragment.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("<html", fragment, StringComparison.Ordinal);
        Assert.Contains($"id=\"playlist-version\" name=\"Version\" value=\"{await VersionAsync(id)}\" hx-swap-oob=\"true\"", fragment, StringComparison.Ordinal);

        using var plain = await _editor.PostFormAsync($"/admin/playlists/{id}",
            new Dictionary<string, string> { ["slideId"] = _slides[1].Id.ToString(), ["Version"] = (await VersionAsync(id)).ToString(CultureInfo.InvariantCulture) },
            action: $"/admin/playlists/{id}?handler=Add");
        Assert.Equal($"/admin/playlists/{id}", AdminSession.Location(plain));
        Assert.Equal([("A", 0), ("B", 1)], await EntriesAsync(id));
    }

    [Fact]
    public async Task A_change_based_on_a_stale_version_is_refused_with_a_friendly_message()
    {
        var id = await CreatePlaylistAsync();
        await AddAsync(id, _slides);
        var ids = await EntryIdsAsync(id);
        var stale = await VersionAsync(id);

        // Someone else moves C to the top...
        (await HtmxAsync(id, "Move", new() { ["entryId"] = ids[2].ToString(), ["toIndex"] = "0" })).Dispose();

        // ...while this editor's page still shows the old order and removes A.
        using var response = await HtmxAsync(id, "Remove", new() { ["entryId"] = ids[0].ToString() }, stale);

        var fragment = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Someone else changed this in the meantime.", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("hx-swap-oob", fragment, StringComparison.Ordinal); // the details form keeps its old version
        Assert.Equal([("C", 0), ("A", 1), ("B", 2)], await EntriesAsync(id));
    }

    [Fact]
    public async Task Renaming_after_reordering_on_the_same_page_does_not_conflict()
    {
        var id = await CreatePlaylistAsync();
        await AddAsync(id, _slides[0], _slides[1]);
        var ids = await EntryIdsAsync(id);
        using var moved = await HtmxAsync(id, "Move", new() { ["entryId"] = ids[1].ToString(), ["toIndex"] = "0" });
        var oobVersion = System.Text.RegularExpressions.Regex.Match(await moved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "id=\"playlist-version\" name=\"Version\" value=\"(\\d+)\"").Groups[1].Value;

        using var saved = await _editor.PostFormAsync($"/admin/playlists/{id}", new Dictionary<string, string>
        {
            ["Input.Name"] = "Canteen (weekdays)",
            ["Version"] = oobVersion,
        });

        Assert.Equal($"/admin/playlists/{id}", AdminSession.Location(saved));
        Assert.Equal("Canteen (weekdays)", await _app.Factory.WithDbAsync(db => db.Playlists.Select(p => p.Name).SingleAsync()));
    }

    [Theory]
    [InlineData("", "Enter a name.")]
    [InlineData("canteen", "A playlist with this name already exists.")]
    public async Task Names_are_required_and_unique_regardless_of_case(string name, string message)
    {
        await CreatePlaylistAsync("Canteen");

        using var response = await _editor.PostFormAsync("/admin/playlists/create", new Dictionary<string, string> { ["Input.Name"] = name });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_edit_page_shows_which_screens_and_groups_use_the_playlist()
    {
        var id = await CreatePlaylistAsync();
        await _app.Factory.WithDbAsync(async db =>
        {
            var screen = new Screen { Name = "Lobby TV" };
            screen.Playlists.Add(new ScreenPlaylist { PlaylistId = id, Position = 0 });
            var group = new ScreenGroup { Name = "Grand hall" };
            group.Playlists.Add(new GroupPlaylist { PlaylistId = id, Position = 0 });
            db.AddRange(screen, group);
            await db.SaveChangesAsync();
        });

        var html = await _editor.GetHtmlAsync($"/admin/playlists/{id}");
        var list = WebUtility.HtmlDecode(await _editor.GetHtmlAsync("/admin/playlists"));

        Assert.Contains("Lobby TV", html, StringComparison.Ordinal);
        Assert.Contains("Grand hall", html, StringComparison.Ordinal);
        Assert.Contains("Screens: 1 · Groups: 1", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_playlist_removes_its_assignments_and_keeps_the_other_assignments_dense()
    {
        var doomed = await CreatePlaylistAsync("Doomed");
        var keep = await CreatePlaylistAsync("Keep");
        await AddAsync(doomed, _slides[0]);
        Guid screenId = default, groupId = default;
        await _app.Factory.WithDbAsync(async db =>
        {
            var screen = new Screen { Name = "Lobby TV" };
            screen.Playlists.Add(new ScreenPlaylist { PlaylistId = doomed, Position = 0 });
            screen.Playlists.Add(new ScreenPlaylist { PlaylistId = keep, Position = 1 });
            var group = new ScreenGroup { Name = "Grand hall" };
            group.Playlists.Add(new GroupPlaylist { PlaylistId = doomed, Position = 0 });
            group.Playlists.Add(new GroupPlaylist { PlaylistId = keep, Position = 1 });
            db.AddRange(screen, group);
            await db.SaveChangesAsync();
            (screenId, groupId) = (screen.Id, group.Id);
        });

        var confirm = WebUtility.HtmlDecode(await _editor.GetHtmlAsync($"/admin/playlists/{doomed}/delete"));
        Assert.Contains("This playlist is in use.", confirm, StringComparison.Ordinal);
        using var deleted = await _editor.PostFormAsync($"/admin/playlists/{doomed}/delete", new Dictionary<string, string>());

        Assert.Equal("/admin/playlists", AdminSession.Location(deleted));
        await _app.Factory.WithDbAsync(async db =>
        {
            Assert.Equal([(keep, 0)], (await db.ScreenPlaylists.Where(sp => sp.ScreenId == screenId).ToListAsync()).Select(sp => (sp.PlaylistId, sp.Position)));
            Assert.Equal([(keep, 0)], (await db.GroupPlaylists.Where(gp => gp.GroupId == groupId).ToListAsync()).Select(gp => (gp.PlaylistId, gp.Position)));
            Assert.Equal(3, await db.Slides.CountAsync());
        });
    }

    [Fact]
    public async Task Deleting_a_slide_keeps_playlist_and_screen_positions_dense()
    {
        var id = await CreatePlaylistAsync();
        await AddAsync(id, _slides[0], _slides[1], _slides[2], _slides[1]);
        Guid screenId = default;
        await _app.Factory.WithDbAsync(async db =>
        {
            var screen = new Screen { Name = "Lobby TV" };
            screen.Slides.Add(new ScreenSlide { SlideId = _slides[1].Id, Placement = SlidePlacement.Prepend, Position = 0 });
            screen.Slides.Add(new ScreenSlide { SlideId = _slides[2].Id, Placement = SlidePlacement.Prepend, Position = 1 });
            screen.Slides.Add(new ScreenSlide { SlideId = _slides[0].Id, Placement = SlidePlacement.Append, Position = 0 });
            db.Add(screen);
            await db.SaveChangesAsync();
            screenId = screen.Id;
        });

        (await _editor.PostFormAsync($"/admin/slides/{_slides[1].Id}/delete", new Dictionary<string, string>())).Dispose();

        Assert.Equal([("A", 0), ("C", 1)], await EntriesAsync(id));
        var screenSlides = await _app.Factory.WithDbAsync(db => db.ScreenSlides.Where(s => s.ScreenId == screenId).OrderBy(s => s.Placement).ThenBy(s => s.Position)
            .Select(s => new { s.Placement, s.Position }).ToListAsync());
        Assert.Equal([(SlidePlacement.Append, 0), (SlidePlacement.Prepend, 0)], screenSlides.Select(s => (s.Placement, s.Position))); // ordered by the stored name
    }

    [Fact]
    public async Task Adding_a_slide_that_was_deleted_meanwhile_reports_it_without_changing_anything()
    {
        var id = await CreatePlaylistAsync();
        var ghost = Guid.NewGuid();

        using var response = await HtmxAsync(id, "Add", new() { ["slideId"] = ghost.ToString() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("That item no longer exists.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Empty(await EntriesAsync(id));
    }

    [Fact]
    public async Task Saving_the_details_form_from_a_stale_page_reports_a_conflict()
    {
        var id = await CreatePlaylistAsync();
        var stale = await VersionAsync(id);
        await AddAsync(id, _slides[0]);

        using var saved = await _editor.PostFormAsync($"/admin/playlists/{id}", new Dictionary<string, string>
        {
            ["Input.Name"] = "Renamed from a stale page",
            ["Version"] = stale.ToString(CultureInfo.InvariantCulture),
        });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("Someone else changed this in the meantime.", WebUtility.HtmlDecode(await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal("Canteen", await _app.Factory.WithDbAsync(db => db.Playlists.Select(p => p.Name).SingleAsync()));
    }

    [Fact]
    public async Task Changes_after_the_playlist_was_deleted_send_the_browser_back_to_the_list()
    {
        var id = await CreatePlaylistAsync();
        var version = await VersionAsync(id);
        await _app.Factory.WithDbAsync(db => db.Playlists.Where(p => p.Id == id).ExecuteDeleteAsync());

        using var response = await _editor.PostHtmxAsync("/admin/playlists", $"/admin/playlists/{id}?handler=Add",
            new Dictionary<string, string> { ["slideId"] = _slides[0].Id.ToString(), ["Version"] = version.ToString(CultureInfo.InvariantCulture) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/admin/playlists", response.Headers.GetValues("HX-Redirect").Single());
    }

    [Fact]
    public async Task A_user_who_must_change_their_password_cannot_use_the_htmx_handlers_either()
    {
        var id = await CreatePlaylistAsync();
        var version = await VersionAsync(id);
        using var fresh = new AdminSession(_app.Factory);
        await using (var scope = _app.Factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<Signage.Web.Features.Users.UserAccounts>();
            var (_, temporary) = await accounts.CreateAsync("new@example.org", "New", UserRole.Editor, TestContext.Current.CancellationToken);
            await fresh.SignInSuccessfullyAsync("new@example.org", temporary!);
        }

        using var response = await fresh.PostHtmxAsync("/admin/account/password", $"/admin/playlists/{id}?handler=Add",
            new Dictionary<string, string> { ["slideId"] = _slides[0].Id.ToString(), ["Version"] = version.ToString(CultureInfo.InvariantCulture) });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/account/password", AdminSession.Location(response));
        Assert.Empty(await EntriesAsync(id));
    }

    [Fact]
    public async Task Unknown_playlists_are_a_404()
    {
        using var get = await _editor.GetAsync($"/admin/playlists/{Guid.NewGuid()}");
        using var delete = await _editor.GetAsync($"/admin/playlists/{Guid.NewGuid()}/delete");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Htmx_posts_without_the_antiforgery_header_are_rejected()
    {
        var id = await CreatePlaylistAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/admin/playlists/{id}?handler=Add", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["slideId"] = _slides[0].Id.ToString(), ["Version"] = "0" }),
        };
        request.Headers.Add("HX-Request", "true");

        using var response = await _editor.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
