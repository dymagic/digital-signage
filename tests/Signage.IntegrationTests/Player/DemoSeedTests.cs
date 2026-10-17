using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Player;

/// <summary>WP-08 demo content (SIGNAGE__SEEDDEMO): created on an empty database only, never twice, never over real content.</summary>
public class DemoSeedTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> SeedDemo = new() { ["Signage:SeedDemo"] = "true" };

    [Fact]
    public async Task With_seed_demo_an_empty_database_gets_a_demo_playlist_and_an_unpaired_demo_screen()
    {
        await using var app = await AdminApp.StartAsync(postgres, SeedDemo);

        await app.Factory.WithDbAsync(async db =>
        {
            var screen = await db.Screens.Include(s => s.Playlists).ThenInclude(p => p.Playlist).ThenInclude(p => p.Slides).SingleAsync();
            Assert.Equal("Demo screen", screen.Name);
            Assert.Null(screen.TokenHash);
            var playlist = Assert.Single(screen.Playlists).Playlist;
            Assert.Equal("Welcome (demo)", playlist.Name);
            Assert.Equal(5, playlist.Slides.Count);
            Assert.All(await db.Slides.Where(s => s.Type == SlideType.Image).ToListAsync(), s => Assert.StartsWith("https://images-assets.nasa.gov/", s.Url, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task The_demo_is_not_created_twice_or_on_top_of_existing_slides()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = new SignageWebFactory(connectionString, SeedDemo))
        {
            _ = first.Server;
            await first.WithDbAsync(db => db.Screens.ExecuteDeleteAsync()); // the operator removed the demo screen, kept slides
        }

        await using var second = new SignageWebFactory(connectionString, SeedDemo);
        _ = second.Server;

        Assert.Equal(0, await second.WithDbAsync(db => db.Screens.CountAsync()));
        Assert.Equal(5, await second.WithDbAsync(db => db.Slides.CountAsync()));
    }

    [Fact]
    public async Task Replicas_starting_together_on_an_empty_database_seed_once_and_both_start()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var a = new SignageWebFactory(connectionString, SeedDemo);
        await using var b = new SignageWebFactory(connectionString, SeedDemo);

        await Task.WhenAll(Task.Run(() => _ = a.Server), Task.Run(() => _ = b.Server)); // throws if either start-up fails

        Assert.Equal(1, await a.WithDbAsync(db => db.Screens.CountAsync()));
        Assert.Equal(5, await a.WithDbAsync(db => db.Slides.CountAsync()));
    }

    [Fact]
    public async Task Without_the_setting_outside_development_nothing_is_seeded()
    {
        await using var app = await AdminApp.StartAsync(postgres);

        Assert.Equal(0, await app.Factory.WithDbAsync(db => db.Slides.CountAsync()));
    }
}
