using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Data;

public class SequenceFromDatabaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_screen_graph_loaded_from_the_database_resolves_per_R12()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        Slide S(string title, bool enabled = true, DateTimeOffset? until = null) =>
            new() { Title = title, Type = SlideType.Image, Url = $"https://example.org/{title}.png", IsEnabled = enabled, ActiveUntilUtc = until, CreatedAt = now, UpdatedAt = now };
        Playlist P(string name, params Slide[] slides)
        {
            var p = new Playlist { Name = name };
            for (var i = 0; i < slides.Length; i++)
            {
                p.Slides.Add(new PlaylistSlide { Slide = slides[i], Position = i });
            }
            return p;
        }

        var repeated = S("repeated");
        var shared = P("Shared", S("shared"));
        var groupA = new ScreenGroup { Name = "A", SortOrder = 0 };
        groupA.Playlists.Add(new GroupPlaylist { Playlist = P("A-list", repeated, S("off", enabled: false), repeated), Position = 0 });
        groupA.Playlists.Add(new GroupPlaylist { Playlist = shared, Position = 1 });
        var groupB = new ScreenGroup { Name = "B", SortOrder = 0 };
        groupB.Playlists.Add(new GroupPlaylist { Playlist = shared, Position = 0 });
        var screen = new Screen { Name = "Lobby" };
        screen.Slides.Add(new ScreenSlide { Slide = S("pre"), Placement = SlidePlacement.Prepend, Position = 0 });
        screen.Slides.Add(new ScreenSlide { Slide = S("post"), Placement = SlidePlacement.Append, Position = 0 });
        screen.Playlists.Add(new ScreenPlaylist { Playlist = P("Direct", S("expired", until: now.AddMinutes(-1)), S("direct")), Position = 0 });
        screen.Playlists.Add(new ScreenPlaylist { Playlist = shared, Position = 1 });
        screen.Groups.Add(new ScreenGroupMember { Group = groupB });
        screen.Groups.Add(new ScreenGroupMember { Group = groupA });

        await factory.WithDbAsync(async db =>
        {
            db.Screens.Add(screen);
            await db.SaveChangesAsync();
        });

        var loaded = await factory.WithDbAsync(db => db.LoadScreenForResolutionAsync(screen.Id, TestContext.Current.CancellationToken));
        var sequence = SequenceResolver.Resolve(loaded!, now);

        Assert.Equal(["pre", "repeated", "repeated", "shared", "direct", "post"], sequence.Select(r => r.Slide.Title));
        Assert.Equal("A", sequence[3].Source.GroupName);
    }

    [Fact]
    public async Task Loading_an_unknown_screen_returns_null()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());

        var loaded = await factory.WithDbAsync(db => db.LoadScreenForResolutionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Null(loaded);
    }
}
