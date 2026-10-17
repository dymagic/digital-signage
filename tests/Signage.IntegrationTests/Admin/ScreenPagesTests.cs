using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Data;
using Signage.Web.Domain;
using Signage.Web.Features.Events;

namespace Signage.IntegrationTests.Admin;

/// <summary>WP-05: screens (R-10, R-14 config), composition (R-12), resolved sequence (R-13), role split (§3).</summary>
public sealed partial class ScreenPagesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly RecordingScreenNotifier _notifier = new();
    private AdminApp _app = null!;
    private AdminSession _admin = null!;
    private AdminSession _editor = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres, configureServices: s => s.Replace(ServiceDescriptor.Singleton<IScreenNotifier>(_notifier)));
        await _app.CreateAdminAsync();
        await _app.CreateUserAsync();
        _admin = await _app.SignedInAsync(AdminApp.AdminEmail);
        _editor = await _app.SignedInAsync(AdminApp.EditorEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _editor.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<Guid> CreateScreenAsync(string name = "Lobby TV", string? location = null)
    {
        using var response = await _admin.PostFormAsync("/admin/screens/create", new Dictionary<string, string> { ["Name"] = name, ["Location"] = location ?? "" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(AdminSession.Location(response)!.Split('/')[^1]);
    }

    private Task<Screen> ScreenAsync(Guid id) => _app.Factory.WithDbAsync(db => db.Screens.AsNoTracking().SingleAsync(s => s.Id == id));

    private async Task<string> VersionAsync(Guid id) => (await ScreenAsync(id)).Version.ToString(CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> PostDetailsAsync(AdminSession session, Guid id, string name, ScreenMode mode = ScreenMode.Carousel, string? url = null, string? version = null) =>
        await session.PostFormAsync($"/admin/screens/{id}", new Dictionary<string, string>
        {
            ["Details.Name"] = name,
            ["Details.Location"] = "",
            ["Details.Mode"] = mode.ToString(),
            ["Details.SingleVideoUrl"] = url ?? "",
            ["Version"] = version ?? await VersionAsync(id),
        }, action: $"/admin/screens/{id}?handler=Details");

    private async Task<HttpResponseMessage> HtmxAsync(AdminSession session, Guid id, string handler, Dictionary<string, string> fields, string? version = null, string? tokenPage = null)
    {
        fields["Version"] = version ?? await VersionAsync(id);
        return await session.PostHtmxAsync(tokenPage ?? $"/admin/screens/{id}", $"/admin/screens/{id}?handler={handler}", fields);
    }

    private async Task<T> AddAsync<T>(T entity) where T : class
    {
        await _app.Factory.WithDbAsync(async db =>
        {
            db.Add(entity);
            await db.SaveChangesAsync();
        });
        return entity;
    }

    private static Slide NewSlide(string title, bool enabled = true) => new() { Title = title, Type = SlideType.Html, Html = $"<p>{title}</p>", IsEnabled = enabled };

    private static Playlist NewPlaylist(string name, params Slide[] slides)
    {
        var p = new Playlist { Name = name };
        for (var i = 0; i < slides.Length; i++)
        {
            p.Slides.Add(new PlaylistSlide { Slide = slides[i], Position = i });
        }
        return p;
    }

    [Fact]
    public async Task An_admin_creates_a_screen_and_names_are_unique_regardless_of_case()
    {
        var id = await CreateScreenAsync("Lobby TV", "Ground floor");

        var screen = await ScreenAsync(id);
        Assert.Equal(("Lobby TV", "Ground floor", ScreenMode.Carousel), (screen.Name, screen.Location, screen.Mode));
        using var duplicate = await _admin.PostFormAsync("/admin/screens/create", new Dictionary<string, string> { ["Name"] = "lobby tv" });
        Assert.Contains("A screen with this name already exists.", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Single_video_mode_needs_a_valid_video_url_and_non_mp4_addresses_only_warn()
    {
        var id = await CreateScreenAsync();

        using (var missing = await PostDetailsAsync(_admin, id, "Lobby TV", ScreenMode.SingleVideo))
        {
            Assert.Contains("Single video mode needs a video address.", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        }
        using (var invalid = await PostDetailsAsync(_admin, id, "Lobby TV", ScreenMode.SingleVideo, "javascript:alert(1)"))
        {
            Assert.Contains("Enter a full video address", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        }
        Assert.Equal(ScreenMode.Carousel, (await ScreenAsync(id)).Mode);

        using var ok = await PostDetailsAsync(_admin, id, "Lobby TV", ScreenMode.SingleVideo, "https://cdn.example.org/loop?id=3");
        Assert.Equal($"/admin/screens/{id}", AdminSession.Location(ok));
        var screen = await ScreenAsync(id);
        Assert.Equal((ScreenMode.SingleVideo, "https://cdn.example.org/loop?id=3"), (screen.Mode, screen.SingleVideoUrl));
        Assert.Contains("doesn't end in .mp4", WebUtility.HtmlDecode(await _admin.GetHtmlAsync($"/admin/screens/{id}")), StringComparison.Ordinal);
        Assert.Contains(id, _notifier.NotifiedScreens);
    }

    [Fact]
    public async Task Editors_see_the_screen_but_cannot_change_details_or_groups()
    {
        var id = await CreateScreenAsync();
        var group = await AddAsync(new ScreenGroup { Name = "Grand hall" });
        var before = await ScreenAsync(id);

        var html = await _editor.GetHtmlAsync($"/admin/screens/{id}");
        using var details = await PostDetailsAsync(_editor, id, "Hijacked", ScreenMode.SingleVideo, "https://example.org/a.mp4");
        using var groups = await _editor.PostFormAsync($"/admin/screens/{id}",
            new Dictionary<string, string> { ["GroupIds"] = group.Id.ToString(), ["Version"] = before.Version.ToString(CultureInfo.InvariantCulture) },
            action: $"/admin/screens/{id}?handler=Groups");

        Assert.DoesNotContain("handler=Details", html, StringComparison.Ordinal);
        Assert.Contains("Only admins can change", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        foreach (var response in new[] { details, groups })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(response), StringComparison.Ordinal);
        }
        var after = await ScreenAsync(id);
        Assert.Equal((before.Name, before.Mode, before.Version), (after.Name, after.Mode, after.Version));
        Assert.Equal(0, await _app.Factory.WithDbAsync(db => db.ScreenGroupMembers.CountAsync()));
    }

    [Fact]
    public async Task Admins_set_group_memberships()
    {
        var id = await CreateScreenAsync();
        var a = await AddAsync(new ScreenGroup { Name = "A" });
        var b = await AddAsync(new ScreenGroup { Name = "B" });

        var form = new FormUrlEncodedContent(
        [
            new("GroupIds", a.Id.ToString()),
            new("GroupIds", b.Id.ToString()),
            new("Version", await VersionAsync(id)),
            new("__RequestVerificationToken", await _admin.GetAntiforgeryTokenAsync($"/admin/screens/{id}")),
        ]);
        using var response = await _admin.Http.PostAsync(new Uri($"/admin/screens/{id}?handler=Groups", UriKind.Relative), form, TestContext.Current.CancellationToken);

        Assert.Equal($"/admin/screens/{id}", AdminSession.Location(response));
        var groups = await _app.Factory.WithDbAsync(db => db.ScreenGroupMembers.Where(m => m.ScreenId == id).Select(m => m.GroupId).ToListAsync());
        Assert.Equal(new[] { a.Id, b.Id }.Order(), groups.Order());
    }

    [Fact]
    public async Task Editors_compose_prepended_slides_playlists_and_appended_slides_with_dense_positions()
    {
        var id = await CreateScreenAsync();
        var s1 = await AddAsync(NewSlide("S1"));
        var s2 = await AddAsync(NewSlide("S2"));
        var p1 = await AddAsync(NewPlaylist("P1"));
        var p2 = await AddAsync(NewPlaylist("P2"));

        foreach (var (placement, slide) in new[] { ("Prepend", s1), ("Prepend", s2), ("Append", s2) })
        {
            using var r = await HtmxAsync(_editor, id, "AddSlide", new() { ["placement"] = placement, ["slideId"] = slide.Id.ToString() });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        (await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = p1.Id.ToString() })).Dispose();
        (await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = p2.Id.ToString() })).Dispose();

        var prepended = await _app.Factory.WithDbAsync(db => db.ScreenSlides.Where(s => s.ScreenId == id && s.Placement == SlidePlacement.Prepend).OrderBy(s => s.Position).ToListAsync());
        (await HtmxAsync(_editor, id, "MoveSlide", new() { ["entryId"] = prepended[1].Id.ToString(), ["toIndex"] = "0" })).Dispose();
        (await HtmxAsync(_editor, id, "MovePlaylist", new() { ["entryId"] = p2.Id.ToString(), ["toIndex"] = "0" })).Dispose();
        (await HtmxAsync(_editor, id, "RemoveSlide", new() { ["entryId"] = prepended[0].Id.ToString() })).Dispose();

        await _app.Factory.WithDbAsync(async db =>
        {
            var slides = await db.ScreenSlides.Where(s => s.ScreenId == id).OrderBy(s => s.Placement).ThenBy(s => s.Position)
                .Select(s => new { s.Placement, s.Position, s.Slide.Title }).ToListAsync();
            Assert.Equal([(SlidePlacement.Append, 0, "S2"), (SlidePlacement.Prepend, 0, "S2")], slides.Select(s => (s.Placement, s.Position, s.Title)));
            var playlists = await db.ScreenPlaylists.Where(sp => sp.ScreenId == id).OrderBy(sp => sp.Position).Select(sp => sp.Playlist.Name).ToListAsync();
            Assert.Equal(["P2", "P1"], playlists);
        });
        Assert.Contains(id, _notifier.NotifiedScreens);
    }

    [Fact]
    public async Task A_playlist_can_be_assigned_to_a_screen_only_once()
    {
        var id = await CreateScreenAsync();
        var p = await AddAsync(NewPlaylist("P"));
        (await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = p.Id.ToString() })).Dispose();

        using var again = await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = p.Id.ToString() });

        Assert.Contains("That playlist is already assigned to this screen.", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.ScreenPlaylists.CountAsync()));
    }

    [Fact]
    public async Task A_composition_change_from_a_stale_page_is_refused()
    {
        var id = await CreateScreenAsync();
        var slide = await AddAsync(NewSlide("S"));
        var stale = await VersionAsync(id);
        (await HtmxAsync(_editor, id, "AddSlide", new() { ["placement"] = "Prepend", ["slideId"] = slide.Id.ToString() })).Dispose();

        using var response = await HtmxAsync(_admin, id, "AddSlide", new() { ["placement"] = "Append", ["slideId"] = slide.Id.ToString() }, stale);

        Assert.Contains("Someone else changed this in the meantime.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.ScreenSlides.CountAsync()));
    }

    [Fact]
    public async Task A_heartbeat_between_loading_and_saving_the_form_does_not_cause_a_conflict()
    {
        var id = await CreateScreenAsync();
        var formVersion = await VersionAsync(id);

        // What WP-10's heartbeat will do: write status fields on the screen row.
        await _app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.LastSeenAt, _app.Time.GetUtcNow())
            .SetProperty(x => x.LastIp, "10.0.0.7")
            .SetProperty(x => x.PlayerVersion, "1.0")));

        using var response = await PostDetailsAsync(_admin, id, "Lobby TV (renamed)", version: formVersion);

        Assert.Equal($"/admin/screens/{id}", AdminSession.Location(response));
        Assert.Equal("Lobby TV (renamed)", (await ScreenAsync(id)).Name);
    }

    [Fact]
    public async Task The_resolved_sequence_on_the_page_equals_the_resolvers_output()
    {
        var id = await CreateScreenAsync();
        var now = _app.Time.GetUtcNow();
        var shared = NewPlaylist("Shared", NewSlide("shared-1"), NewSlide("shared-2"));
        var off = NewSlide("off", enabled: false);
        var expired = NewSlide("expired");
        expired.ActiveUntilUtc = now.AddMinutes(-1);
        var groupB = new ScreenGroup { Name = "B", SortOrder = 0 };
        groupB.Playlists.Add(new GroupPlaylist { Playlist = shared, Position = 0 });
        var groupA = new ScreenGroup { Name = "A", SortOrder = 0 };
        groupA.Playlists.Add(new GroupPlaylist { Playlist = NewPlaylist("A-list", NewSlide("a-1"), off, expired), Position = 0 });
        groupA.Playlists.Add(new GroupPlaylist { Playlist = shared, Position = 1 });
        await _app.Factory.WithDbAsync(async db =>
        {
            var screen = await db.Screens.SingleAsync(s => s.Id == id);
            screen.Groups.Add(new ScreenGroupMember { Group = groupB });
            screen.Groups.Add(new ScreenGroupMember { Group = groupA });
            screen.Slides.Add(new ScreenSlide { Slide = NewSlide("first"), Placement = SlidePlacement.Prepend, Position = 0 });
            screen.Slides.Add(new ScreenSlide { Slide = NewSlide("last"), Placement = SlidePlacement.Append, Position = 0 });
            screen.Playlists.Add(new ScreenPlaylist { Playlist = NewPlaylist("Direct", NewSlide("direct")), Position = 0 });
            screen.Playlists.Add(new ScreenPlaylist { Playlist = shared, Position = 1 });
            await db.SaveChangesAsync();
        });

        var html = await _editor.GetHtmlAsync($"/admin/screens/{id}");
        var table = Regex.Match(html, "data-testid=\"resolved-sequence\".*?</table>", RegexOptions.Singleline).Value;
        var onPage = SequenceRow().Matches(table).Select(m => Guid.Parse(m.Groups[1].Value)).ToList();

        var expected = await _app.Factory.WithDbAsync(async db =>
            SequenceResolver.Resolve((await db.LoadScreenForResolutionAsync(id, TestContext.Current.CancellationToken))!, now).Select(r => r.Slide.Id).ToList());
        Assert.Equal(expected, onPage);
        Assert.Equal(6, onPage.Count); // first, a-1, shared-1, shared-2, direct, last
        Assert.Contains("Group \"A\" → playlist \"A-list\"", WebUtility.HtmlDecode(table), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_made_after_the_screen_was_deleted_send_the_browser_back_to_the_list()
    {
        var id = await CreateScreenAsync();
        var slide = await AddAsync(NewSlide("S"));
        var version = await VersionAsync(id);
        await _app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == id).ExecuteDeleteAsync());

        using var response = await HtmxAsync(_editor, id, "AddSlide", new() { ["placement"] = "Prepend", ["slideId"] = slide.Id.ToString() }, version, tokenPage: "/admin/screens");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/admin/screens", response.Headers.GetValues("HX-Redirect").Single());
        Assert.Contains("deleted by someone else", WebUtility.HtmlDecode(await _editor.GetHtmlAsync("/admin/screens")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admins_delete_screens_and_editors_cannot()
    {
        var id = await CreateScreenAsync();
        await AddAsync(new ScreenSlide { ScreenId = id, Slide = NewSlide("S"), Placement = SlidePlacement.Prepend, Position = 0 });

        using (var editor = await _editor.GetAsync($"/admin/screens/{id}/delete"))
        {
            Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(editor), StringComparison.Ordinal);
        }

        using var deleted = await _admin.PostFormAsync($"/admin/screens/{id}/delete", new Dictionary<string, string>());
        Assert.Equal("/admin/screens", AdminSession.Location(deleted));
        await _app.Factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Screens.CountAsync());
            Assert.Equal(0, await db.ScreenSlides.CountAsync());
            Assert.Equal(1, await db.Slides.CountAsync());
        });
    }

    [Fact]
    public async Task A_failed_list_change_does_not_refresh_the_details_form_so_saving_it_still_reports_the_conflict()
    {
        var id = await CreateScreenAsync();
        var slide = await AddAsync(NewSlide("S"));
        var pageVersion = await VersionAsync(id);

        // Another admin renames the screen.
        (await PostDetailsAsync(_admin, id, "Renamed by B")).Dispose();

        // This admin's page (opened before the rename) tries a list change: refused as a conflict...
        using var listChange = await HtmxAsync(_editor, id, "AddSlide", new() { ["placement"] = "Prepend", ["slideId"] = slide.Id.ToString() }, pageVersion);
        var fragment = await listChange.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Someone else changed this", WebUtility.HtmlDecode(fragment), StringComparison.Ordinal);
        Assert.DoesNotContain("hx-swap-oob", fragment, StringComparison.Ordinal);

        // ...and the details form, still on the old version, can't overwrite the rename.
        using var details = await PostDetailsAsync(_admin, id, "Stale name", version: pageVersion);
        Assert.Contains("Someone else changed this", WebUtility.HtmlDecode(await details.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal("Renamed by B", (await ScreenAsync(id)).Name);
    }

    [Fact]
    public async Task A_successful_list_change_refreshes_the_side_forms_versions()
    {
        var id = await CreateScreenAsync();
        var slide = await AddAsync(NewSlide("S"));

        using var listChange = await HtmxAsync(_editor, id, "AddSlide", new() { ["placement"] = "Prepend", ["slideId"] = slide.Id.ToString() });

        var fragment = await listChange.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var newVersion = await VersionAsync(id);
        Assert.Contains($"id=\"details-version\" name=\"Version\" value=\"{newVersion}\" hx-swap-oob=\"true\"", fragment, StringComparison.Ordinal);
        Assert.Contains($"id=\"groups-version\" name=\"Version\" value=\"{newVersion}\" hx-swap-oob=\"true\"", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_membership_change_on_the_screen_page_makes_a_stale_members_form_on_the_group_page_conflict()
    {
        var id = await CreateScreenAsync();
        var group = await AddAsync(new ScreenGroup { Name = "G" });
        var groupPageVersion = group.Version;

        var form = new FormUrlEncodedContent(
        [
            new("GroupIds", group.Id.ToString()),
            new("Version", await VersionAsync(id)),
            new("__RequestVerificationToken", await _admin.GetAntiforgeryTokenAsync($"/admin/screens/{id}")),
        ]);
        (await _admin.Http.PostAsync(new Uri($"/admin/screens/{id}?handler=Groups", UriKind.Relative), form, TestContext.Current.CancellationToken)).Dispose();

        // The group page was open before: its (empty) members form must not silently remove the screen again.
        var stale = new FormUrlEncodedContent(
        [
            new("Version", groupPageVersion.ToString(CultureInfo.InvariantCulture)),
            new("__RequestVerificationToken", await _admin.GetAntiforgeryTokenAsync($"/admin/groups/{group.Id}")),
        ]);
        using var response = await _admin.Http.PostAsync(new Uri($"/admin/groups/{group.Id}?handler=Members", UriKind.Relative), stale, TestContext.Current.CancellationToken);

        Assert.Contains("Someone else changed this", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.ScreenGroupMembers.CountAsync()));
    }

    [Fact]
    public async Task Deleting_a_screen_notifies_it()
    {
        var id = await CreateScreenAsync();
        _notifier.Clear();

        (await _admin.PostFormAsync($"/admin/screens/{id}/delete", new Dictionary<string, string>())).Dispose();

        Assert.Equal([id], _notifier.NotifiedScreens);
    }

    [Theory]
    [InlineData("Details", "Details.Name=X&Details.Mode=Hologram")]
    [InlineData("Details", "Details.Name=X&Details.Mode=7")]
    [InlineData("Details", "Details.Name=X&Details.Mode=Carousel&Version=abc")]
    public async Task Values_the_form_cannot_produce_are_refused_not_turned_into_defaults(string handler, string body)
    {
        var id = await CreateScreenAsync("Original");
        await _app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Mode, ScreenMode.SingleVideo).SetProperty(x => x.SingleVideoUrl, "https://example.org/a.mp4")));
        var version = await VersionAsync(id);
        var token = await _admin.GetAntiforgeryTokenAsync($"/admin/screens/{id}");
        var withVersion = body.Contains("Version=", StringComparison.Ordinal) ? body : $"{body}&Version={version}";
        using var content = new StringContent($"{withVersion}&__RequestVerificationToken={Uri.EscapeDataString(token)}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await _admin.Http.PostAsync(new Uri($"/admin/screens/{id}?handler={handler}", UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("contained a value this page can't use", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        var screen = await ScreenAsync(id);
        Assert.Equal(("Original", ScreenMode.SingleVideo), (screen.Name, screen.Mode));
    }

    [Fact]
    public async Task An_unknown_placement_is_refused()
    {
        var id = await CreateScreenAsync();
        var slide = await AddAsync(NewSlide("S"));

        using var response = await HtmxAsync(_editor, id, "AddSlide", new() { ["placement"] = "Sideways", ["slideId"] = slide.Id.ToString() });

        Assert.Contains("contained a value this page can't use", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(0, await _app.Factory.WithDbAsync(db => db.ScreenSlides.CountAsync()));
    }

    [GeneratedRegex("<tr data-slide-id=\"([0-9a-f-]{36})\"")]
    private static partial Regex SequenceRow();
}
