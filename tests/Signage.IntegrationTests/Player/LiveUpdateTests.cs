using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features.Hosting;
using Signage.Web.Features.Pairing;
using Signage.Web.Features.Player;

namespace Signage.IntegrationTests.Player;

/// <summary>WP-10: push events over SSE fanned out through Postgres (R-25, R-63), heartbeat and status (R-33, R-30), R-26.</summary>
public sealed class LiveUpdateTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);

    [Fact]
    public async Task A_change_reaches_the_screens_event_stream()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, screenId) = await DeviceAsync(app.Factory, "Hall TV");
        await using var events = await EventStream.OpenAsync(device);
        await ListeningAsync(app.Factory);

        await Notifier(app.Factory).ScreensChangedAsync([screenId], TestContext.Current.CancellationToken);

        Assert.Equal(ScreenEvent.ManifestChanged, await events.NextAsync(Soon));
        device.Dispose();
    }

    [Fact]
    public async Task Two_app_instances_sharing_one_database_both_deliver_an_event()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var first = new SignageWebFactory(connectionString);
        await using var second = new SignageWebFactory(connectionString);
        var (deviceOnFirst, screenA) = await DeviceAsync(first, "Screen A");
        var (deviceOnSecond, screenB) = await DeviceAsync(second, "Screen B");
        await using var eventsA = await EventStream.OpenAsync(deviceOnFirst);
        await using var eventsB = await EventStream.OpenAsync(deviceOnSecond);
        await ListeningAsync(first);
        await ListeningAsync(second);

        // The change is made on the first instance; the second instance's player hears about it too (R-63).
        await Notifier(first).ScreensChangedAsync([screenA, screenB], TestContext.Current.CancellationToken);

        Assert.Equal(ScreenEvent.ManifestChanged, await eventsA.NextAsync(Soon));
        Assert.Equal(ScreenEvent.ManifestChanged, await eventsB.NextAsync(Soon));
        deviceOnFirst.Dispose();
        deviceOnSecond.Dispose();
    }

    [Fact]
    public async Task A_screen_only_hears_about_its_own_changes_and_about_changes_to_all()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, screenId) = await DeviceAsync(app.Factory, "Mine");
        var (other, otherId) = await DeviceAsync(app.Factory, "Other");
        await using var events = await EventStream.OpenAsync(device);
        await ListeningAsync(app.Factory);
        var notifier = Notifier(app.Factory);

        await notifier.ScreensChangedAsync([otherId], TestContext.Current.CancellationToken);
        Assert.Null(await events.NextAsync(Quiet));

        await notifier.AllScreensChangedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ScreenEvent.ManifestChanged, await events.NextAsync(Soon));
        await notifier.ScreensChangedAsync([screenId], TestContext.Current.CancellationToken);
        Assert.Equal(ScreenEvent.ManifestChanged, await events.NextAsync(Soon));
        device.Dispose();
        other.Dispose();
    }

    [Fact]
    public async Task Hundreds_of_affected_screens_fit_in_notifications()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, screenId) = await DeviceAsync(app.Factory, "Last of many");
        await using var events = await EventStream.OpenAsync(device);
        await ListeningAsync(app.Factory);
        var many = Enumerable.Range(0, 500).Select(_ => Guid.CreateVersion7()).Append(screenId).ToArray();

        await Notifier(app.Factory).ScreensChangedAsync(many, TestContext.Current.CancellationToken); // > 8000 bytes as one payload

        Assert.Equal(ScreenEvent.ManifestChanged, await events.NextAsync(Soon));
        device.Dispose();
    }

    [Fact]
    public async Task Stopping_the_app_ends_open_event_streams_at_once()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, _) = await DeviceAsync(app.Factory, "Restarting TV");
        await using var events = await EventStream.OpenAsync(device);

        app.Factory.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        await events.Closed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); // not the 30 s shutdown timeout
        device.Dispose();
    }

    [Fact]
    public async Task After_the_listener_loses_its_connection_players_fetch_and_later_events_still_arrive()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, screenId) = await DeviceAsync(app.Factory, "Patient TV");
        await using var events = await EventStream.OpenAsync(device);
        await ListeningAsync(app.Factory);

        // The database drops the listening session (a failover, a restart, an idle-connection killer).
        var killed = await app.Factory.WithDbAsync(db => db.Database.SqlQueryRaw<bool>(
            "SELECT pg_terminate_backend(pid) AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND query LIKE 'LISTEN %'").ToListAsync());
        Assert.Equal([true], killed);

        // The listener waits out its back-off (on the app's clock), reconnects and tells every player to fetch.
        string? afterReconnect = null;
        for (var i = 0; i < 100 && afterReconnect is null; i++)
        {
            app.Time.Advance(TimeSpan.FromSeconds(1));
            afterReconnect = await events.NextAsync(TimeSpan.FromMilliseconds(100));
        }
        Assert.Equal(ScreenEvent.ManifestChanged, afterReconnect);

        await Notifier(app.Factory).ScreensChangedAsync([screenId], TestContext.Current.CancellationToken);
        Assert.Equal(ScreenEvent.ManifestChanged, await events.NextAsync(Soon));
        device.Dispose();
    }

    [Fact]
    public async Task Identify_reaches_only_that_screen_and_identify_all_reaches_every_screen_with_30_seconds()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        await app.CreateUserAsync(); // editors may identify (PROJECT_BRIEF §3)
        using var editor = await app.SignedInAsync(AdminApp.EditorEmail);
        var (device, screenId) = await DeviceAsync(app.Factory, "Identified TV");
        var (other, _) = await DeviceAsync(app.Factory, "Bystander TV");
        await using var events = await EventStream.OpenAsync(device);
        await using var otherEvents = await EventStream.OpenAsync(other);
        await ListeningAsync(app.Factory);

        using var identify = await editor.PostFormAsync($"/admin/screens/{screenId}", new Dictionary<string, string>(), action: $"/admin/screens/{screenId}?handler=Identify");
        Assert.Equal(HttpStatusCode.Redirect, identify.StatusCode);
        Assert.Equal((ScreenEvent.Identify, "{\"seconds\":30}"), await events.NextEventAsync(Soon));
        Assert.Null(await otherEvents.NextAsync(Quiet));

        using var all = await editor.PostFormAsync("/admin/screens", new Dictionary<string, string>(), action: "/admin/screens?handler=IdentifyAll");
        Assert.Equal(HttpStatusCode.Redirect, all.StatusCode);
        Assert.Equal((ScreenEvent.IdentifyAll, "{\"seconds\":30}"), await events.NextEventAsync(Soon));
        Assert.Equal((ScreenEvent.IdentifyAll, "{\"seconds\":30}"), await otherEvents.NextEventAsync(Soon));
        Assert.Equal((ScreenEvent.ManifestChanged, "{}"), await NextManifestChangeAsync(app, events, screenId));
        device.Dispose();
        other.Dispose();
    }

    [Fact]
    public async Task Identifying_needs_a_signed_in_user_and_an_existing_screen()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        await app.CreateAdminAsync();
        using var admin = await app.SignedInAsync(AdminApp.AdminEmail);
        var (device, screenId) = await DeviceAsync(app.Factory, "Doomed TV");
        await app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == screenId).ExecuteDeleteAsync());

        using var gone = await admin.PostFormAsync("/admin/screens", new Dictionary<string, string>(), action: $"/admin/screens/{screenId}?handler=Identify");
        using var anonymous = new AdminSession(app.Factory);
        using var signedOut = await anonymous.PostWithoutTokenAsync("/admin/screens?handler=IdentifyAll", new Dictionary<string, string>());

        Assert.Equal("/admin/screens", AdminSession.Location(gone));
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.StartsWith("/admin/login", new Uri(new Uri("http://x"), AdminSession.Location(signedOut)).PathAndQuery, StringComparison.Ordinal);
        device.Dispose();
    }

    private static async Task<(string Name, string Data)?> NextManifestChangeAsync(AdminApp app, EventStream events, Guid screenId)
    {
        await Notifier(app.Factory).ScreensChangedAsync([screenId], TestContext.Current.CancellationToken);
        return await events.NextEventAsync(Soon);
    }

    [Fact]
    public async Task The_event_stream_and_the_heartbeat_need_a_screen_token()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        using var anonymous = new PlayerClient(app.Factory);

        using var events = await anonymous.Http.GetAsync(new Uri("/api/player/events", UriKind.Relative), TestContext.Current.CancellationToken);
        using var heartbeat = await anonymous.Http.PostAsJsonAsync(new Uri("/api/player/heartbeat", UriKind.Relative), new { playerVersion = "1" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, events.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, heartbeat.StatusCode);
    }

    [Fact]
    public async Task A_heartbeat_records_status_without_touching_the_screens_version()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, screenId) = await DeviceAsync(app.Factory, "Reporting TV");
        var versionBefore = await app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == screenId).Select(s => s.Version).SingleAsync());

        using var response = await HeartbeatAsync(device, userAgent: "SignageTest/1.0 (Kiosk)");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var screen = await app.Factory.WithDbAsync(db => db.Screens.AsNoTracking().SingleAsync(s => s.Id == screenId));
        Assert.Equal(app.Time.GetUtcNow(), screen.LastSeenAt);
        Assert.Equal("2.3.4", screen.PlayerVersion);
        Assert.Equal("SignageTest/1.0 (Kiosk)", screen.UserAgent);
        Assert.Equal(versionBefore, screen.Version); // an open edit form doesn't see a conflict (ADR-0004)
        device.Dispose();
    }

    [Fact]
    public async Task The_screens_list_shows_online_until_90_seconds_without_a_heartbeat()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        await app.CreateAdminAsync();
        using var admin = await app.SignedInAsync(AdminApp.AdminEmail);
        var (device, _) = await DeviceAsync(app.Factory, "Status TV");
        (await HeartbeatAsync(device)).Dispose();

        Assert.Contains(">Online<", await StatusCellAsync(admin, "Status TV"), StringComparison.Ordinal);
        app.Time.Advance(TimeSpan.FromSeconds(89));
        Assert.Contains(">Online<", await StatusCellAsync(admin, "Status TV"), StringComparison.Ordinal);
        app.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Contains(">Offline<", await StatusCellAsync(admin, "Status TV"), StringComparison.Ordinal);

        var html = WebUtility.HtmlDecode(await admin.GetHtmlAsync("/admin/screens"));
        Assert.Contains("2026-10-16 08:00", html, StringComparison.Ordinal); // last seen, system time zone (UTC)
        Assert.Contains("2.3.4", html, StringComparison.Ordinal);
        Assert.Contains("hx-trigger=\"every 15s\"", html, StringComparison.Ordinal);
        device.Dispose();
    }

    [Fact]
    public async Task The_status_refresh_neither_keeps_a_session_alive_nor_hides_a_signed_out_session()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        await app.CreateAdminAsync();
        using var admin = await app.SignedInAsync(AdminApp.AdminEmail);
        app.Time.Advance(TimeSpan.FromHours(7)); // past half of the 12 h sliding window: a normal request would renew

        using var refresh = await GetAsync(admin.Http, "/admin/screens", htmx: true, background: true);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.DoesNotContain(refresh.Headers.TryGetValues("Set-Cookie", out var set) ? set : [], c => c.StartsWith("signage.auth=", StringComparison.Ordinal)); // not extended
        using var visit = await GetAsync(admin.Http, "/admin/screens", htmx: false, background: false);
        Assert.Contains(visit.Headers.GetValues("Set-Cookie"), c => c.StartsWith("signage.auth=", StringComparison.Ordinal));

        // Signed out (expired, elsewhere, deactivated): htmx is told to go to the sign-in page instead of swapping it in.
        using var anonymous = new AdminSession(app.Factory);
        using var expired = await GetAsync(anonymous.Http, "/admin/screens", htmx: true, background: true);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.StartsWith("/admin/login", new Uri(new Uri("http://x"), expired.Headers.GetValues("HX-Redirect").Single()).PathAndQuery, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string path, bool htmx, bool background)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }
        if (background)
        {
            request.Headers.Add("X-Background-Refresh", "1");
        }
        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_the_forwarded_client_ip_is_recorded()
    {
        await using var app = await AdminApp.StartAsync(postgres, new Dictionary<string, string?> { ["Signage:ForwardedHeaders:Enabled"] = "true" });
        var (device, screenId) = await DeviceAsync(app.Factory, "Proxied TV");

        (await HeartbeatAsync(device, forwardedFor: "203.0.113.7")).Dispose();

        Assert.Equal("203.0.113.7", await app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == screenId).Select(s => s.LastIp).SingleAsync()));
        device.Dispose();
    }

    [Fact]
    public async Task Without_forwarded_headers_enabled_the_header_is_ignored()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, screenId) = await DeviceAsync(app.Factory, "Direct TV");

        (await HeartbeatAsync(device, forwardedFor: "203.0.113.7")).Dispose();

        Assert.NotEqual("203.0.113.7", await app.Factory.WithDbAsync(db => db.Screens.Where(s => s.Id == screenId).Select(s => s.LastIp).SingleAsync()));
        device.Dispose();
    }

    [Theory]
    [InlineData("10.0.0.0/8, 192.168.1.0/24", 2)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Known_networks_are_comma_separated_cidrs(string? value, int count) =>
        Assert.Equal(count, ForwardedHeadersSetup.ParseNetworks(value).Count);

    [Fact]
    public void An_invalid_known_network_stops_start_up_with_a_clear_message()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.ParseNetworks("10.0.0.0/8,not-a-network"));
        Assert.Contains("not-a-network", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_player_page_and_the_manifest_carry_the_same_player_build()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, _) = await DeviceAsync(app.Factory, "Versioned TV");
        var build = app.Factory.Services.GetRequiredService<PlayerBuild>().Id;

        var html = await device.Http.GetStringAsync(new Uri("/screen", UriKind.Relative), TestContext.Current.CancellationToken);
        using var manifest = await device.GetManifestAsync();
        var json = System.Text.Json.JsonDocument.Parse(await manifest.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.StartsWith($"{AppInfo.Version}+", build, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"signage-version\" content=\"{build}\">", html, StringComparison.Ordinal);
        Assert.Equal(build, json.RootElement.GetProperty("appVersion").GetString());
        device.Dispose();
    }

    [Fact]
    public void The_player_build_changes_when_any_player_file_changes()
    {
        var root = Directory.CreateTempSubdirectory("signage-build-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "screen", "slides"));
            File.WriteAllText(Path.Combine(root.FullName, "screen", "player.js"), "one");
            File.WriteAllText(Path.Combine(root.FullName, "screen", "slides", "video.js"), "two");
            var before = new PlayerBuild(new WebRoot(root.FullName)).Id;
            Assert.Equal(before, new PlayerBuild(new WebRoot(root.FullName)).Id); // stable

            File.WriteAllText(Path.Combine(root.FullName, "screen", "slides", "video.js"), "two, fixed");

            Assert.NotEqual(before, new PlayerBuild(new WebRoot(root.FullName)).Id);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_heartbeat_with_more_problems_than_a_player_keeps_is_refused()
    {
        // (Kestrel also caps the body at 16 KB; the in-memory test server doesn't enforce body sizes.)
        await using var app = await AdminApp.StartAsync(postgres);
        var (device, _) = await DeviceAsync(app.Factory, "Chatty TV");
        var problems = Enumerable.Range(0, 21).Select(i => new { kind = "video", slideId = Guid.CreateVersion7(), reason = new string('x', 50) });

        using var response = await device.Http.PostAsJsonAsync(new Uri("/api/player/heartbeat", UriKind.Relative), new { playerVersion = "1", problems }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        device.Dispose();
    }

    private sealed class WebRoot(string path) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = path;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(path);
        public string ApplicationName { get; set; } = "test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = path;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private static IScreenNotifier Notifier(SignageWebFactory factory) => factory.Services.GetRequiredService<IScreenNotifier>();

    private static Task ListeningAsync(SignageWebFactory factory) =>
        factory.Services.GetRequiredService<ScreenEventListener>().Listening.WaitAsync(Soon, TestContext.Current.CancellationToken);

    private static async Task<(PlayerClient Device, Guid ScreenId)> DeviceAsync(SignageWebFactory factory, string name)
    {
        var screen = new Screen { Name = name };
        await factory.WithDbAsync(async db =>
        {
            db.Screens.Add(screen);
            await db.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var token = await scope.ServiceProvider.GetRequiredService<ScreenAccessService>().CreateAddressTokenAsync(screen.Id, TestContext.Current.CancellationToken);
        var device = new PlayerClient(factory);
        (await device.ExchangeAsync(token!)).Dispose();
        return (device, screen.Id);
    }

    private static async Task<HttpResponseMessage> HeartbeatAsync(PlayerClient device, string? userAgent = null, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/player/heartbeat", UriKind.Relative))
        {
            Content = JsonContent.Create(new { playerVersion = "2.3.4", currentSlideId = (Guid?)null, problems = new[] { new { kind = "video", slideId = Guid.CreateVersion7(), reason = "stalled" } } }),
        };
        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }
        if (forwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }
        return await device.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> StatusCellAsync(AdminSession admin, string screenName)
    {
        var html = await admin.GetHtmlAsync("/admin/screens");
        var row = html[html.IndexOf(screenName, StringComparison.Ordinal)..];
        var cell = row[row.IndexOf("data-testid=\"screen-status\"", StringComparison.Ordinal)..];
        return cell[..cell.IndexOf("</td>", StringComparison.Ordinal)];
    }

    /// <summary>Reads a player's event stream in the background; event names arrive in order.</summary>
    private sealed class EventStream : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly CancellationTokenSource _stop = new();
        private readonly Channel<(string Name, string Data)> _events = Channel.CreateUnbounded<(string Name, string Data)>();
        private readonly Task _reader;

        private EventStream(HttpResponseMessage response)
        {
            _response = response;
            _reader = Task.Run(ReadAsync);
        }

        public static async Task<EventStream> OpenAsync(PlayerClient device)
        {
            var response = await device.Http.GetAsync(new Uri("/api/player/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            return new EventStream(response);
        }

        /// <summary>Completes when the server ends the stream.</summary>
        public Task Closed => _reader;

        /// <summary>The next event's name, or null if none arrives in time.</summary>
        public async Task<string?> NextAsync(TimeSpan timeout) => (await NextEventAsync(timeout))?.Name;

        /// <summary>The next event's name and data, or null if none arrives in time.</summary>
        public async Task<(string Name, string Data)?> NextEventAsync(TimeSpan timeout)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                return await _events.Reader.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        private async Task ReadAsync()
        {
            try
            {
                await using var stream = await _response.Content.ReadAsStreamAsync(_stop.Token);
                using var reader = new StreamReader(stream);
                string? name = null;
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        name = line["event: ".Length..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal) && name is not null)
                    {
                        await _events.Writer.WriteAsync((name, line["data: ".Length..]), _stop.Token);
                        name = null;
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException)
            {
                // closed
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _response.Dispose();
            await _reader;
            _stop.Dispose();
        }
    }
}
