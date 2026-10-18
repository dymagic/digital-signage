using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>WP-10: pushed updates (R-25), status in the admin (R-33) and the self-update reload (R-26).</summary>
[Trait("Category", "Player")]
public class LiveUpdateTests(BrowserFixture fixture) : PlayerTest(fixture)
{
    [Fact]
    public async Task An_edit_in_the_admin_reaches_the_screen_within_5_seconds()
    {
        // Real time: the fallback poll is 60 s, so only the push can make it in time.
        var (_, token, ids) = await ScreenWithSlidesAsync(("Menu", 30, "#14532d"));
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 1);
        await device.WaitForFunctionAsync("() => document.body.dataset.events === 'open'"); // the push stream is connected

        await Page.GotoAsync($"/admin/slides/{ids[0]}");
        await Page.GetByLabel("HTML", new() { Exact = true }).FillAsync("<h1 style='color:#fff'>Soup of the day: tomato</h1>");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Changes saved.")).ToBeVisibleAsync();
        var saved = DateTimeOffset.UtcNow;

        await device.WaitForFunctionAsync(
            "() => document.querySelector('.slide[data-state=active] iframe')?.srcdoc.includes('Soup of the day')",
            null, new() { Timeout = 5000, PollingInterval = 100 });
        Assert.True(DateTimeOffset.UtcNow - saved < TimeSpan.FromSeconds(5));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task An_edit_saved_while_the_screen_is_fetching_its_manifest_still_arrives_within_5_seconds()
    {
        var (_, token, ids) = await ScreenWithSlidesAsync(("Board", 30, "#1e293b"));
        var hold = 0; // 1: hold the next manifest response (already read from the server) until released
        var held = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var device = await OpenDeviceAsync(token, timeScale: 1, ctx => ctx.RouteAsync("**/api/player/manifest", async route =>
        {
            if (Interlocked.CompareExchange(ref hold, 2, 1) != 1)
            {
                await route.ContinueAsync();
                return;
            }
            var response = await route.FetchAsync();
            held.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await route.FulfillAsync(new() { Response = response });
        }));
        await WaitForActivationsAsync(device, 1);
        await device.WaitForFunctionAsync("() => document.body.dataset.events === 'open'");

        Volatile.Write(ref hold, 1);
        await EditHtmlAsync(ids[0], "First edit");
        await held.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); // the screen is fetching; its answer predates the next edit
        await EditHtmlAsync(ids[0], "Second edit");
        var saved = DateTimeOffset.UtcNow;
        release.TrySetResult();

        await device.WaitForFunctionAsync(
            "() => document.querySelector('.slide[data-state=active] iframe')?.srcdoc.includes('Second edit')",
            null, new() { Timeout = 5000, PollingInterval = 100 });
        Assert.True(DateTimeOffset.UtcNow - saved < TimeSpan.FromSeconds(5));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_version_reload_that_could_not_happen_is_tried_again()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("Rollout", 2, "#0f766e"), ("Retry", 2, "#9a3412"));
        var refused = 0;
        var device = await OpenDeviceAsync(token, timeScale: 0.25, async ctx =>
        {
            await ctx.Clock.InstallAsync();
            await ctx.RouteAsync(url => new Uri(url).AbsolutePath == "/screen", async route =>
            {
                if (route.Request.ResourceType == "fetch" && Interlocked.Increment(ref refused) == 1)
                {
                    await route.AbortAsync("connectionrefused"); // the server is mid-rollout at the first attempt
                    return;
                }
                await route.ContinueAsync();
            });
        });
        await WaitForActivationsAsync(device, 2);
        await device.Context.RouteAsync("**/api/player/manifest", async route =>
        {
            var headers = route.Request.Headers.Where(h => !h.Key.Equals("if-none-match", StringComparison.OrdinalIgnoreCase)).ToDictionary();
            var response = await route.FetchAsync(new() { Headers = headers });
            var manifest = JsonNode.Parse(await response.TextAsync())!.AsObject();
            manifest["appVersion"] = "999.0.0";
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = manifest.ToJsonString() });
        });

        for (var i = 0; i < 300 && Volatile.Read(ref refused) < 1; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken); // the next poll (15 s here) sees the new version
        }
        Assert.Equal(1, refused);
        var reload = device.WaitForRequestAsync(r => r.IsNavigationRequest && new Uri(r.Url).AbsolutePath == "/screen", new() { Timeout = 30_000 });
        await device.Clock.FastForwardAsync("01:01"); // a minute later it tries again

        await reload;
        await device.Context.CloseAsync();
    }

    private async Task EditHtmlAsync(Guid slideId, string text)
    {
        await Page.GotoAsync($"/admin/slides/{slideId}");
        await Page.GetByLabel("HTML", new() { Exact = true }).FillAsync($"<h1 style='color:#fff'>{text}</h1>");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Changes saved.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_screens_list_shows_a_playing_screen_as_online_with_its_player_details()
    {
        var tag = Tag();
        await EmptyScreenAsync($"Reporting {tag}");
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await ExpectModeAsync(device, "empty");

        await Page.GotoAsync("/admin/screens");
        var row = Page.Locator("tr", new() { HasText = $"Reporting {tag}" });
        await Expect(row.GetByTestId("screen-status")).ToHaveTextAsync("Online", new() { Timeout = 20_000 }); // refreshes itself
        await Expect(row).ToContainTextAsync("HeadlessChrome");
        await Page.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "admin-screens-status.png") });
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_screen_reloads_once_when_the_server_runs_a_new_version()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("Old", 2, "#1e3a8a"), ("Code", 2, "#7c2d12"));
        var device = await OpenDeviceAsync(token, timeScale: 0.25);
        await WaitForActivationsAsync(device, 2);
        var navigations = 0;
        device.Request += (_, r) => { if (r.IsNavigationRequest && r.Frame == device.MainFrame) Interlocked.Increment(ref navigations); };

        // From now on the server reports a newer version than the page was served with.
        await device.Context.RouteAsync("**/api/player/manifest", async route =>
        {
            var headers = route.Request.Headers.Where(h => !h.Key.Equals("if-none-match", StringComparison.OrdinalIgnoreCase)).ToDictionary();
            var response = await route.FetchAsync(new() { Headers = headers });
            var manifest = JsonNode.Parse(await response.TextAsync())!.AsObject();
            manifest["appVersion"] = "999.0.0";
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = manifest.ToJsonString() });
        });
        await device.EvaluateAsync("() => { window.__marker = 'old page'; }");
        var reload = device.WaitForRequestAsync(r => r.IsNavigationRequest && new Uri(r.Url).AbsolutePath == "/screen", new() { Timeout = 30_000 }); // the next poll (15 s here)
        await reload;

        await ExpectModeAsync(device, "player");
        Assert.Null(await device.EvaluateAsync<string?>("() => window.__marker ?? null"));
        // The page still says the old version (this test's server didn't change): no reload loop.
        await device.WaitForTimeoutAsync(20_000); // longer than one manifest poll at this scale
        Assert.Equal(1, navigations);
        await device.Context.CloseAsync();
    }
}
