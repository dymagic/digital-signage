using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Data;

public sealed class ModelRulesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private SignageWebFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("playlists")]
    [InlineData("screens")]
    [InlineData("screen_groups")]
    public async Task Names_are_unique_regardless_of_case(string table)
    {
        await _factory.WithDbAsync(async db =>
        {
            AddNamed(db, table, "Grand Hall");
            await db.SaveChangesAsync();
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _factory.WithDbAsync(async db =>
        {
            AddNamed(db, table, "grand hall");
            await db.SaveChangesAsync();
        }));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Case_insensitive_names_still_keep_their_original_spelling_and_can_be_looked_up_by_equality()
    {
        await _factory.WithDbAsync(async db =>
        {
            db.Playlists.Add(new Playlist { Name = "Canteen Menu" });
            await db.SaveChangesAsync();
        });

        var found = await _factory.WithDbAsync(db => db.Playlists.SingleAsync(p => p.Name == "CANTEEN menu"));
        Assert.Equal("Canteen Menu", found.Name);
    }

    [Fact]
    public async Task Concurrent_edits_of_the_same_slide_are_detected()
    {
        var slide = new Slide { Title = "Original", Type = SlideType.Image, Url = "https://example.org/a.png" };
        await _factory.WithDbAsync(async db =>
        {
            db.Slides.Add(slide);
            await db.SaveChangesAsync();
        });

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<SignageDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<SignageDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var a = await dbA.Slides.SingleAsync(s => s.Id == slide.Id, ct);
        var b = await dbB.Slides.SingleAsync(s => s.Id == slide.Id, ct);

        a.Title = "Edited by A";
        await dbA.SaveChangesAsync(ct);
        b.Title = "Edited by B";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Deleting_a_slide_removes_its_playlist_and_screen_links()
    {
        var slide = new Slide { Title = "Doomed", Type = SlideType.Html, Html = "<p>hi</p>" };
        var keep = new Slide { Title = "Keep", Type = SlideType.Html, Html = "<p>keep</p>" };
        var playlist = new Playlist { Name = "P" };
        playlist.Slides.Add(new PlaylistSlide { Slide = slide, Position = 0 });
        playlist.Slides.Add(new PlaylistSlide { Slide = keep, Position = 1 });
        var screen = new Screen { Name = "S" };
        screen.Slides.Add(new ScreenSlide { Slide = slide, Placement = SlidePlacement.Prepend, Position = 0 });
        await _factory.WithDbAsync(async db =>
        {
            db.AddRange(playlist, screen);
            await db.SaveChangesAsync();
        });

        await _factory.WithDbAsync(db => db.Slides.Where(s => s.Id == slide.Id).ExecuteDeleteAsync());

        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal([keep.Id], await db.PlaylistSlides.Select(ps => ps.SlideId).ToListAsync());
            Assert.Empty(await db.ScreenSlides.ToListAsync());
            Assert.Equal(1, await db.Playlists.CountAsync());
            Assert.Equal(1, await db.Screens.CountAsync());
        });
    }

    [Fact]
    public async Task Deleting_a_playlist_removes_its_assignments_but_not_screens_groups_or_slides()
    {
        var playlist = new Playlist { Name = "P" };
        playlist.Slides.Add(new PlaylistSlide { Slide = new Slide { Title = "s", Type = SlideType.Html, Html = "x" }, Position = 0 });
        var screen = new Screen { Name = "S" };
        screen.Playlists.Add(new ScreenPlaylist { Playlist = playlist, Position = 0 });
        var group = new ScreenGroup { Name = "G" };
        group.Playlists.Add(new GroupPlaylist { Playlist = playlist, Position = 0 });
        await _factory.WithDbAsync(async db =>
        {
            db.AddRange(screen, group);
            await db.SaveChangesAsync();
        });

        await _factory.WithDbAsync(db => db.Playlists.Where(p => p.Id == playlist.Id).ExecuteDeleteAsync());

        await _factory.WithDbAsync(async db =>
        {
            Assert.Empty(await db.ScreenPlaylists.ToListAsync());
            Assert.Empty(await db.GroupPlaylists.ToListAsync());
            Assert.Empty(await db.PlaylistSlides.ToListAsync());
            Assert.Equal(1, await db.Slides.CountAsync());
            Assert.Equal(1, await db.Screens.CountAsync());
            Assert.Equal(1, await db.ScreenGroups.CountAsync());
        });
    }

    [Fact]
    public async Task Only_one_settings_row_can_exist()
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _factory.WithDbAsync(async db =>
        {
            db.SystemSettings.Add(new SystemSettings { Id = 2 });
            await db.SaveChangesAsync();
        }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Schedule_times_round_trip_as_utc_instants()
    {
        var from = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero);
        var slide = new Slide { Title = "t", Type = SlideType.Image, Url = "https://example.org/t.png", ActiveFromUtc = from, ActiveUntilUtc = from.AddHours(2) };
        await _factory.WithDbAsync(async db =>
        {
            db.Slides.Add(slide);
            await db.SaveChangesAsync();
        });

        var loaded = await _factory.WithDbAsync(db => db.Slides.AsNoTracking().SingleAsync(s => s.Id == slide.Id));

        Assert.Equal(from, loaded.ActiveFromUtc);
        Assert.Equal(TimeSpan.Zero, loaded.ActiveFromUtc!.Value.Offset);
        Assert.Equal(from.AddHours(2), loaded.ActiveUntilUtc);
    }

    private static void AddNamed(SignageDbContext db, string table, string name)
    {
        switch (table)
        {
            case "playlists": db.Playlists.Add(new Playlist { Name = name }); break;
            case "screens": db.Screens.Add(new Screen { Name = name }); break;
            case "screen_groups": db.ScreenGroups.Add(new ScreenGroup { Name = name }); break;
            default: throw new ArgumentOutOfRangeException(nameof(table));
        }
    }
}
