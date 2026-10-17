using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features.Hosting;
using Signage.Web.Features.Pairing;

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
    public async Task The_player_page_carries_the_app_version_it_belongs_to()
    {
        await using var app = await AdminApp.StartAsync(postgres);
        using var device = new PlayerClient(app.Factory);

        var html = await device.Http.GetStringAsync(new Uri("/screen", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains($"<meta name=\"signage-version\" content=\"{AppInfo.Version}\">", html, StringComparison.Ordinal);
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
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();
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

        /// <summary>The next event's name, or null if none arrives in time.</summary>
        public async Task<string?> NextAsync(TimeSpan timeout)
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
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        await _events.Writer.WriteAsync(line["event: ".Length..], _stop.Token);
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
