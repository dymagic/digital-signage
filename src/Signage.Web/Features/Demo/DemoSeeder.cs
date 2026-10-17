using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.Web.Features.Demo;

/// <summary>
/// Demo content for a first look (README quick start): a playlist with two public-domain NASA photos and HTML slides
/// (a welcome, a "how it works" and a clock), and an unpaired screen "Demo screen" that plays it. Pair a device to that
/// screen and it shows the demo. Runs in Development or with SIGNAGE__SEEDDEMO=true, and only while there are no slides,
/// so it never touches real content. The HTML slides need no internet; the photos do.
/// </summary>
internal static partial class DemoSeeder
{
    public const string ScreenName = "Demo screen";
    public const string PlaylistName = "Welcome (demo)";
    private const long SeedLockKey = 0x5167_6E61_6765_0003;

    public static async Task SeedDemoIfRequestedAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<SignageOptions>>().Value;
            if (!env.IsDevelopment() && !options.SeedDemo)
            {
                return;
            }

            var db = scope.ServiceProvider.GetRequiredService<SignageDbContext>();
            var seeded = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Replicas starting together on an empty database (R-63): one seeds, the others then see its slides.
                await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SeedLockKey})", ct).ConfigureAwait(false);
                if (await db.Slides.AnyAsync(ct).ConfigureAwait(false) || await db.Screens.AnyAsync(s => s.Name == ScreenName, ct).ConfigureAwait(false))
                {
                    return false;
                }
                AddDemo(db);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            if (seeded)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoSeeder));
                LogSeeded(logger);
            }
        }
    }

    private static void AddDemo(SignageDbContext db)
    {
        var playlist = new Playlist { Name = PlaylistName, Description = "Sample content created by SIGNAGE__SEEDDEMO. Delete it whenever you like." };
        Slide[] slides =
        [
            Html("Welcome", WelcomeHtml, 12),
            Image("Aurora seen from the International Space Station (NASA)", "https://images-assets.nasa.gov/image/iss071e564695/iss071e564695~large.jpg"),
            Html("How it works", HowItWorksHtml, 15),
            Image("Apollo 11: Buzz Aldrin on the Moon (NASA)", "https://images-assets.nasa.gov/image/as11-40-5875/as11-40-5875~large.jpg"),
            Html("Clock", ClockHtml, 10),
        ];
        for (var i = 0; i < slides.Length; i++)
        {
            playlist.Slides.Add(new PlaylistSlide { Slide = slides[i], Position = i });
        }

        var screen = new Screen { Name = ScreenName, Location = "Created by the demo. Pair a device to it to see the demo playlist." };
        screen.Playlists.Add(new ScreenPlaylist { Playlist = playlist, Position = 0 });
        db.Screens.Add(screen);
    }

    private static Slide Html(string title, string html, int seconds) =>
        new() { Title = title, Type = SlideType.Html, Html = html, DurationSeconds = seconds };

    private static Slide Image(string title, string url) =>
        new() { Title = title, Type = SlideType.Image, Url = url };

    private const string WelcomeHtml = """
        <style>
          body{display:grid;place-content:center;text-align:center;background:linear-gradient(135deg,#0f2027,#203a43,#2c5364)}
          h1{font-size:9vmin;margin:0;font-weight:600}
          p{font-size:3.4vmin;opacity:.8;margin:2vmin 0 0}
        </style>
        <h1>Welcome to Signage</h1>
        <p>This is a demo playlist. Edit it in the admin under Playlists.</p>
        """;

    private const string HowItWorksHtml = """
        <style>
          body{display:grid;place-content:center;background:#111}
          ol{font-size:4vmin;line-height:1.6;margin:0}
          h2{font-size:5vmin;margin:0 0 2vmin;font-weight:600}
        </style>
        <div><h2>How it works</h2>
        <ol><li>Add slides: images, videos, web pages or HTML.</li>
        <li>Put them in playlists.</li>
        <li>Assign playlists to screens or groups.</li></ol></div>
        """;

    private const string ClockHtml = """
        <style>body{display:grid;place-content:center;background:#000}#t{font-size:22vmin;font-weight:200;font-variant-numeric:tabular-nums}</style>
        <div id="t"></div>
        <script>
          const t = document.getElementById('t');
          const tick = () => { t.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }); };
          tick();
          setInterval(tick, 1000);
        </script>
        """;

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo content created: pair a device to the screen \"Demo screen\" to see it.")]
    private static partial void LogSeeded(ILogger logger);
}
