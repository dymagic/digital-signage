using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;
using Signage.Web.Features.Events;

namespace Signage.IntegrationTests.Admin;

/// <summary>WP-05: screen groups (R-11), role split (§3) and which screens get notified (R-25 groundwork).</summary>
public sealed class GroupPagesTests(PostgresFixture postgres) : IAsyncLifetime
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

    private async Task<Guid> CreateGroupAsync(string name = "Grand hall", int sortOrder = 0)
    {
        using var response = await _admin.PostFormAsync("/admin/groups/create", new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.SortOrder"] = sortOrder.ToString(CultureInfo.InvariantCulture),
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Guid.Parse(AdminSession.Location(response)!.Split('/')[^1]);
    }

    private async Task<string> VersionAsync(Guid id) =>
        (await _app.Factory.WithDbAsync(db => db.ScreenGroups.Where(g => g.Id == id).Select(g => g.Version).SingleAsync())).ToString(CultureInfo.InvariantCulture);

    private async Task<T> AddAsync<T>(T entity) where T : class
    {
        await _app.Factory.WithDbAsync(async db =>
        {
            db.Add(entity);
            await db.SaveChangesAsync();
        });
        return entity;
    }

    private async Task<HttpResponseMessage> SetMembersAsync(AdminSession session, Guid id, params Guid[] screenIds)
    {
        var fields = screenIds.Select(s => new KeyValuePair<string, string>("ScreenIds", s.ToString())).ToList();
        fields.Add(new("Version", await VersionAsync(id)));
        fields.Add(new("__RequestVerificationToken", await session.GetAntiforgeryTokenAsync($"/admin/groups/{id}")));
        return await session.Http.PostAsync(new Uri($"/admin/groups/{id}?handler=Members", UriKind.Relative), new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> HtmxAsync(AdminSession session, Guid id, string handler, Dictionary<string, string> fields)
    {
        fields["Version"] = await VersionAsync(id);
        return await session.PostHtmxAsync($"/admin/groups/{id}", $"/admin/groups/{id}?handler={handler}", fields);
    }

    [Fact]
    public async Task Admins_create_groups_set_sort_order_and_members_and_both_old_and_new_members_are_notified()
    {
        var id = await CreateGroupAsync("Grand hall", 5);
        var a = await AddAsync(new Screen { Name = "A" });
        var b = await AddAsync(new Screen { Name = "B" });
        var c = await AddAsync(new Screen { Name = "C" });
        (await SetMembersAsync(_admin, id, a.Id, b.Id)).Dispose();
        _notifier.Clear();

        using var response = await SetMembersAsync(_admin, id, b.Id, c.Id);

        Assert.Equal($"/admin/groups/{id}", AdminSession.Location(response));
        var members = await _app.Factory.WithDbAsync(db => db.ScreenGroupMembers.Where(m => m.GroupId == id).Select(m => m.ScreenId).ToListAsync());
        Assert.Equal(new[] { b.Id, c.Id }.Order(), members.Order());
        Assert.Equal(new[] { a.Id, b.Id, c.Id }.Order(), _notifier.NotifiedScreens.Order());
        Assert.Equal(5, await _app.Factory.WithDbAsync(db => db.ScreenGroups.Select(g => g.SortOrder).SingleAsync()));
    }

    [Fact]
    public async Task Group_names_are_unique_regardless_of_case()
    {
        await CreateGroupAsync("Grand hall");

        using var response = await _admin.PostFormAsync("/admin/groups/create", new Dictionary<string, string> { ["Input.Name"] = "GRAND HALL", ["Input.SortOrder"] = "0" });

        Assert.Contains("A group with this name already exists.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editors_cannot_create_rename_reorder_or_change_members_of_groups()
    {
        var id = await CreateGroupAsync("Grand hall", 1);
        var screen = await AddAsync(new Screen { Name = "A" });

        using var create = await _editor.GetAsync("/admin/groups/create");
        using var delete = await _editor.GetAsync($"/admin/groups/{id}/delete");
        using var details = await _editor.PostFormAsync($"/admin/groups/{id}", new Dictionary<string, string>
        {
            ["Input.Name"] = "Renamed", ["Input.SortOrder"] = "-10", ["Version"] = await VersionAsync(id),
        }, action: $"/admin/groups/{id}?handler=Details");
        using var members = await SetMembersAsync(_editor, id, screen.Id);

        foreach (var response in new[] { create, delete, details, members })
        {
            Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(response), StringComparison.Ordinal);
        }
        var group = await _app.Factory.WithDbAsync(db => db.ScreenGroups.AsNoTracking().SingleAsync());
        Assert.Equal(("Grand hall", 1), (group.Name, group.SortOrder));
        Assert.Equal(0, await _app.Factory.WithDbAsync(db => db.ScreenGroupMembers.CountAsync()));
    }

    [Fact]
    public async Task Editors_manage_a_groups_playlists_and_its_screens_are_notified()
    {
        var id = await CreateGroupAsync();
        var screen = await AddAsync(new Screen { Name = "A" });
        (await SetMembersAsync(_admin, id, screen.Id)).Dispose();
        var p1 = await AddAsync(new Playlist { Name = "P1" });
        var p2 = await AddAsync(new Playlist { Name = "P2" });
        var p3 = await AddAsync(new Playlist { Name = "P3" });
        _notifier.Clear();

        foreach (var p in new[] { p1, p2, p3 })
        {
            using var r = await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = p.Id.ToString() });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        (await HtmxAsync(_editor, id, "MovePlaylist", new() { ["entryId"] = p3.Id.ToString(), ["toIndex"] = "0" })).Dispose();
        (await HtmxAsync(_editor, id, "RemovePlaylist", new() { ["entryId"] = p1.Id.ToString() })).Dispose();
        using var duplicate = await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = p2.Id.ToString() });

        Assert.Contains("already assigned to this group", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        var order = await _app.Factory.WithDbAsync(db => db.GroupPlaylists.Where(gp => gp.GroupId == id).OrderBy(gp => gp.Position)
            .Select(gp => new { gp.Playlist.Name, gp.Position }).ToListAsync());
        Assert.Equal([("P3", 0), ("P2", 1)], order.Select(o => (o.Name, o.Position)));
        Assert.Equal([screen.Id], _notifier.NotifiedScreens);
    }

    [Fact]
    public async Task Deleting_a_group_keeps_its_screens_and_playlists_and_notifies_former_members()
    {
        var id = await CreateGroupAsync();
        var screen = await AddAsync(new Screen { Name = "A" });
        (await SetMembersAsync(_admin, id, screen.Id)).Dispose();
        var playlist = await AddAsync(new Playlist { Name = "P" });
        (await HtmxAsync(_editor, id, "AddPlaylist", new() { ["playlistId"] = playlist.Id.ToString() })).Dispose();
        _notifier.Clear();

        using var deleted = await _admin.PostFormAsync($"/admin/groups/{id}/delete", new Dictionary<string, string>());

        Assert.Equal("/admin/groups", AdminSession.Location(deleted));
        await _app.Factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ScreenGroups.CountAsync());
            Assert.Equal(0, await db.GroupPlaylists.CountAsync());
            Assert.Equal(1, await db.Screens.CountAsync());
            Assert.Equal(1, await db.Playlists.CountAsync());
        });
        Assert.Equal([screen.Id], _notifier.NotifiedScreens);
    }

    [Fact]
    public async Task Content_changes_notify_the_screens_that_show_the_content()
    {
        var group = await CreateGroupAsync();
        var viaGroup = await AddAsync(new Screen { Name = "Via group" });
        var direct = await AddAsync(new Screen { Name = "Direct" });
        var attached = await AddAsync(new Screen { Name = "Attached" });
        var bystander = await AddAsync(new Screen { Name = "Bystander" });
        var slide = await AddAsync(new Slide { Title = "S", Type = SlideType.Html, Html = "x" });
        var playlist = new Playlist { Name = "P" };
        playlist.Slides.Add(new PlaylistSlide { SlideId = slide.Id, Position = 0 });
        await AddAsync(playlist);
        await _app.Factory.WithDbAsync(async db =>
        {
            db.ScreenGroupMembers.Add(new ScreenGroupMember { GroupId = group, ScreenId = viaGroup.Id });
            db.GroupPlaylists.Add(new GroupPlaylist { GroupId = group, PlaylistId = playlist.Id, Position = 0 });
            db.ScreenPlaylists.Add(new ScreenPlaylist { ScreenId = direct.Id, PlaylistId = playlist.Id, Position = 0 });
            db.ScreenSlides.Add(new ScreenSlide { ScreenId = attached.Id, SlideId = slide.Id, Placement = SlidePlacement.Append, Position = 0 });
            await db.SaveChangesAsync();
        });
        var everyoneButBystander = new[] { viaGroup.Id, direct.Id, attached.Id }.Order().ToList();

        _notifier.Clear();
        (await _editor.PostFormAsync("/admin/slides", new Dictionary<string, string>(), action: $"/admin/slides?handler=Toggle&id={slide.Id}&enabled=false")).Dispose();
        Assert.Equal(everyoneButBystander, _notifier.NotifiedScreens.Order().ToList());

        _notifier.Clear();
        var version = await _app.Factory.WithDbAsync(db => db.Playlists.Where(p => p.Id == playlist.Id).Select(p => p.Version).SingleAsync());
        (await _editor.PostHtmxAsync($"/admin/playlists/{playlist.Id}", $"/admin/playlists/{playlist.Id}?handler=Add",
            new Dictionary<string, string> { ["slideId"] = slide.Id.ToString(), ["Version"] = version.ToString(CultureInfo.InvariantCulture) })).Dispose();
        Assert.Equal(new[] { viaGroup.Id, direct.Id }.Order(), _notifier.NotifiedScreens.Order());
        Assert.DoesNotContain(bystander.Id, _notifier.NotifiedScreens);

        _notifier.Clear();
        var settingsVersion = await _app.Factory.WithDbAsync(db => db.SystemSettings.Select(s => s.Version).SingleAsync());
        (await _admin.PostFormAsync("/admin/settings", new Dictionary<string, string>
        {
            ["Input.DefaultSlideDurationSeconds"] = "12", ["Input.TimeZoneId"] = "UTC", ["Input.Version"] = settingsVersion.ToString(CultureInfo.InvariantCulture),
        })).Dispose();
        Assert.Equal(1, _notifier.AllScreensNotifications);
    }
}
