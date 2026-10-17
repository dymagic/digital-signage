using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>
/// Time-dependent player behaviour, driven by Playwright's Clock API (no test-only code in the player):
/// schedules re-checked offline (R-24, R-03) and the daily reload (PLAYER_SPEC §9, ADR-0008).
/// </summary>
[Trait("Category", "Player")]
public class ClockTests(BrowserFixture fixture) : PlayerTest(fixture)
{
    // The device lives in 2030 (the server's clock is real): every schedule below is in the server's future.
    private static readonly DateTimeOffset DeviceStart = new(2030, 1, 2, 10, 0, 0, TimeSpan.Zero);

    private static Task InstallClock(IBrowserContext context, DateTimeOffset at) =>
        context.Clock.InstallAsync(new() { TimeDate = at.UtcDateTime });

    [Fact]
    public async Task An_offline_screen_stops_showing_a_slide_when_its_schedule_ends()
    {
        var tag = Tag();
        var stayA = await AdminActions.CreateHtmlSlideAsync(Page, $"Stays A {tag}", 2);
        var stayB = await AdminActions.CreateHtmlSlideAsync(Page, $"Stays B {tag}", 2);
        var ends = await AdminActions.CreateHtmlSlideAsync(Page, $"Ends {tag}", 2, activeUntilLocal: "2030-01-02T10:02");
        await EmptyScreenAsync($"Schedule {tag}", $"Schedule {tag}", [$"Stays A {tag}", $"Ends {tag}", $"Stays B {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        var device = await OpenDeviceAsync(token, timeScale: 0.25, ctx => InstallClock(ctx, DeviceStart));
        var first = await WaitForActivationsAsync(device, 4);
        Assert.Contains(ends.ToString(), first);

        await device.Context.RouteAsync("**/api/player/**", route => route.AbortAsync("connectionrefused"));
        await device.Clock.FastForwardAsync("03:00"); // 10:03: the window has ended, and the server can't be asked
        var before = await ActivationCountAsync(device);
        var after = (await WaitForActivationsAsync(device, before + 4)).Skip(before + 1).ToList(); // the slide on screen may finish

        Assert.DoesNotContain(ends.ToString(), after);
        Assert.Contains(stayA.ToString(), after);
        Assert.Contains(stayB.ToString(), after);
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task The_daily_reload_waits_while_the_server_is_unreachable_and_happens_at_a_transition_once_it_answers()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("Day", 2, "#0c4a6e"), ("Night", 2, "#3b0764"));
        var device = await OpenDeviceAsync(token, timeScale: 0.25, ctx => InstallClock(ctx, DeviceStart));
        await WaitForActivationsAsync(device, 2);
        await device.EvaluateAsync("() => { window.__marker = 'still the same page'; }");

        // Offline: the server can't confirm it answers, so the reload is postponed and playback carries on (R-24).
        Func<string, bool> pageItself = url => new Uri(url).AbsolutePath == "/screen";
        var refused = 0;
        await device.Context.RouteAsync(pageItself, route => { Interlocked.Increment(ref refused); return route.AbortAsync("connectionrefused"); });
        await device.Clock.FastForwardAsync("26:01:00"); // past the 26 h limit
        var before = await ActivationCountAsync(device);
        await WaitForActivationsAsync(device, before + 3);
        Assert.True(refused > 0, "the player checked the server before reloading");
        Assert.Equal("still the same page", await device.EvaluateAsync<string>("() => window.__marker"));

        // The server answers again: the next check schedules the reload, which happens at the next transition.
        await device.Context.UnrouteAsync(pageItself);
        var reload = device.WaitForRequestAsync(r => r.IsNavigationRequest && new Uri(r.Url).AbsolutePath == "/screen", new() { Timeout = 30_000 });
        await device.Clock.FastForwardAsync("01:01");
        await reload;
        await ExpectModeAsync(device, "player");
        Assert.Null(await device.EvaluateAsync<string?>("() => window.__marker ?? null"));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_screen_that_never_transitions_reloads_at_the_check_inside_the_night_window()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("Still", 5, "#422006"));
        var device = await OpenDeviceAsync(token, timeScale: 1, ctx => InstallClock(ctx, new DateTimeOffset(2030, 1, 2, 6, 30, 0, TimeSpan.Zero)));
        await WaitForActivationsAsync(device, 1);

        // 20 h later it is 02:30 the next night: inside 02:00–05:00 and past the minimum uptime.
        var reload = device.WaitForRequestAsync(r => r.IsNavigationRequest && new Uri(r.Url).AbsolutePath == "/screen", new() { Timeout = 30_000 });
        await device.Clock.FastForwardAsync("20:01:00");
        await reload;
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task No_reload_happens_outside_the_night_window_before_26_hours()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("Busy", 5, "#14532d"));
        var device = await OpenDeviceAsync(token, timeScale: 1, ctx => InstallClock(ctx, new DateTimeOffset(2030, 1, 2, 10, 0, 0, TimeSpan.Zero)));
        await WaitForActivationsAsync(device, 1);
        await device.EvaluateAsync("() => { window.__marker = 'same'; }");

        await device.Clock.FastForwardAsync("21:00:00"); // 07:00 next day: past 20 h, outside the window, before 26 h
        await device.Clock.RunForAsync(120_000); // two more checks

        Assert.Equal("same", await device.EvaluateAsync<string>("() => window.__marker"));
        await device.Context.CloseAsync();
    }
}
