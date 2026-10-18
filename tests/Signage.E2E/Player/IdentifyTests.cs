using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>WP-11: identify one (R-31), identify all (R-32) and the info overlay (R-30), all pushed live.</summary>
[Collection(GlobalStateTests.Name)]
[Trait("Category", "Player")]
public class IdentifyTests(BrowserFixture fixture) : PlayerTest(fixture)
{
    private static async Task<IPage> ConnectedDeviceAsync(PlayerTest test, string token)
    {
        var device = await test.OpenDeviceForIdentifyAsync(token);
        await device.WaitForFunctionAsync("() => document.body.dataset.events === 'open'");
        return device;
    }

    [Fact]
    public async Task Identify_shows_a_pulsing_red_border_and_the_name_and_a_new_identify_restarts_the_timer()
    {
        var (screenId, token, _) = await ScreenWithSlidesAsync(("Lobby", 20, "#f8fafc"));
        var name = await Page.Locator("h1").InnerTextAsync();
        var device = await ConnectedDeviceAsync(this, token);
        await WaitForActivationsAsync(device, 1);

        await Page.GotoAsync($"/admin/screens/{screenId}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Identify", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("The screen now shows a red border and its name for 30 seconds.")).ToBeVisibleAsync();
        var first = DateTimeOffset.UtcNow;

        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-identify", "one", new() { Timeout = 5000 });
        await Expect(device.GetByTestId("identify-name")).ToHaveTextAsync(name.Replace("Screen: ", "", StringComparison.Ordinal));
        var border = await device.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-testid=identify]'), '::before').boxShadow");
        Assert.Contains("rgb(225, 29, 72)", border, StringComparison.Ordinal); // #e11d48
        Assert.Contains("24px", border, StringComparison.Ordinal);
        // It pulses at 1 Hz between 100 % and 55 % (0.5 s each way), and fades out over 500 ms (100 ms at this time scale).
        var pulse = await device.EvaluateAsync<string[]>("""
            () => { const s = getComputedStyle(document.querySelector('[data-testid=identify]'), '::before');
                    return [s.animationName, s.animationDuration, s.animationDirection, s.animationIterationCount,
                            getComputedStyle(document.querySelector('[data-testid=identify]')).transitionDuration]; }
            """);
        Assert.Equal(["identify-pulse", "0.5s", "alternate", "infinite", "0.1s"], pulse);
        await device.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "player-identify.png") });

        // Halfway through, identify again: it now lasts 6 s from the second click, not the first.
        await device.WaitForTimeoutAsync(3000);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Identify", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("The screen now shows a red border and its name for 30 seconds.")).ToBeVisibleAsync();
        var second = DateTimeOffset.UtcNow;
        await device.WaitForTimeoutAsync((int)Math.Max(0, (first.AddSeconds(7) - DateTimeOffset.UtcNow).TotalMilliseconds));
        Assert.Equal("one", await device.EvaluateAsync<string?>("() => document.body.dataset.identify ?? null")); // past the first 6 s

        await Expect(device.Locator("body")).Not.ToHaveAttributeAsync("data-identify", "one", new() { Timeout = 10_000 });
        Assert.True(DateTimeOffset.UtcNow - second > TimeSpan.FromSeconds(5.5), "it ran for its full time after the second click");
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task Identify_all_shows_every_screens_name_without_the_border()
    {
        var (_, tokenA, _) = await ScreenWithSlidesAsync(("North wing", 20, "#1e3a8a"));
        var (_, tokenB, _) = await ScreenWithSlidesAsync(("South wing", 20, "#7c2d12"));
        var deviceA = await ConnectedDeviceAsync(this, tokenA);
        var deviceB = await ConnectedDeviceAsync(this, tokenB);

        await Page.GotoAsync("/admin/screens");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Identify all" }).ClickAsync();
        await Expect(Page.GetByText("Every screen now shows its name for 30 seconds.")).ToBeVisibleAsync();

        foreach (var device in new[] { deviceA, deviceB })
        {
            await Expect(device.Locator("body")).ToHaveAttributeAsync("data-identify", "all", new() { Timeout = 5000 });
            await Expect(device.GetByTestId("identify-name")).ToContainTextAsync("Player ");
            await Expect(device.GetByTestId("identify")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("with-border"));
        }
        await deviceA.Context.CloseAsync();
        await deviceB.Context.CloseAsync();
    }

    [Fact]
    public async Task Identification_shows_over_single_video_mode()
    {
        var (screenId, token, _) = await ScreenWithSlidesAsync(("Before video", 20, "#334155"));
        var device = await ConnectedDeviceAsync(this, token);
        await AdminActions.SetScreenModeAsync(Page, screenId, new Uri(E2EEnvironment.MediaBaseUrl, "/media/clip-c-h264.mp4"));
        await ExpectModeAsync(device, "single-video");
        await device.WaitForFunctionAsync("() => document.querySelector('[data-testid=single-video].visible')", null, new() { Timeout = 15_000 });

        await Page.GotoAsync($"/admin/screens/{screenId}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Identify", Exact = true }).ClickAsync();

        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-identify", "one", new() { Timeout = 5000 });
        // Both are absolutely positioned children of the stage (overlays ignore the pointer, so elementFromPoint can't tell).
        var stacking = await device.EvaluateAsync<int[]>("""
            () => [document.querySelector('[data-testid=identify]'), document.querySelector('[data-testid=single-video]')]
                .map((el) => el.parentElement === document.getElementById('stage') ? Number(getComputedStyle(el).zIndex) : -1)
            """);
        Assert.True(stacking[0] > stacking[1] && stacking[1] >= 0, $"identify z {stacking[0]} over single video z {stacking[1]}");
        Assert.Equal("1", await device.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-testid=identify]')).opacity"));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task The_info_overlay_appears_and_disappears_live_with_the_global_setting()
    {
        var tag = Tag();
        await EmptyScreenAsync($"Info {tag}", $"Info {tag}", [await HtmlTitleAsync(tag)]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await ConnectedDeviceAsync(this, token);
        await WaitForActivationsAsync(device, 1);
        try
        {
            await SetInfoOverlayAsync(true);

            var overlay = device.GetByTestId("info-overlay");
            await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 5000 }); // pushed, and applied without waiting for a slide change
            await Expect(overlay).ToHaveTextAsync(new System.Text.RegularExpressions.Regex($"^Info {tag} · \\d{{1,3}}(\\.\\d{{1,3}}){{3}}$"));
            await device.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "player-info-overlay.png") });

            await SetInfoOverlayAsync(false);
            await Expect(overlay).ToBeHiddenAsync(new() { Timeout = 5000 });
        }
        finally
        {
            await SetInfoOverlayAsync(false); // the setting is global: leave it as other tests expect
        }
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task The_info_overlay_shows_a_dim_dot_while_the_screen_is_offline()
    {
        var tag = Tag();
        await EmptyScreenAsync($"Dot {tag}", $"Dot {tag}", [await HtmlTitleAsync(tag)]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await ConnectedDeviceAsync(this, token);
        await WaitForActivationsAsync(device, 1);
        try
        {
            await SetInfoOverlayAsync(true);
            var overlay = device.GetByTestId("info-overlay");
            await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 5000 });
            await Expect(overlay).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("offline"));

            await device.Context.RouteAsync("**/api/player/**", route => route.AbortAsync("connectionrefused"));
            await device.EvaluateAsync("() => document.body.dataset.online === 'false' || new Promise((r) => new MutationObserver(() => document.body.dataset.online === 'false' && r()).observe(document.body, { attributes: true }))");
            await Expect(overlay).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("offline"));
            Assert.Equal("\"\"", await device.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-testid=info-overlay]'), '::after').content"));

            await device.Context.UnrouteAllAsync();
            await Expect(overlay).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("offline"), new() { Timeout = 20_000 });
        }
        finally
        {
            await SetInfoOverlayAsync(false);
        }
        await device.Context.CloseAsync();
    }

    private async Task<string> HtmlTitleAsync(string tag)
    {
        await AdminActions.CreateHtmlSlideAsync(Page, $"Info slide {tag}", 30);
        return $"Info slide {tag}";
    }

    private async Task SetInfoOverlayAsync(bool on)
    {
        await Page.GotoAsync("/admin/settings");
        await Page.GetByLabel("Show each screen's name and IP address on the screen").SetCheckedAsync(on);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Changes saved.")).ToBeVisibleAsync();
    }
}
