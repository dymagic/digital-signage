using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Infrastructure;

/// <summary>
/// Base for player tests: the admin (Page) builds a screen with content through the UI; a separate browser context
/// is the device. An init script records every slide activation in window.__activations so tests read exact order and
/// timing from the page instead of sleeping.
/// </summary>
public abstract class PlayerTest(BrowserFixture fixture) : E2ETest(fixture)
{
    private const string Recorder = """
        window.__activations = [];
        window.__maxLayers = 0;
        new MutationObserver((records) => {
            for (const r of records) {
                const el = r.target;
                if (r.type === 'attributes' && el.classList?.contains('slide') && el.dataset.state === 'active') {
                    window.__activations.push({ id: el.dataset.slideId, t: performance.now() });
                }
            }
            const slides = document.querySelector('.slides');
            if (slides) window.__maxLayers = Math.max(window.__maxLayers, slides.childElementCount);
        }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-state'] });
        """;

    protected override bool SignedInAsAdmin => true;

    protected static string Tag() => Guid.NewGuid().ToString("N")[..6];

    /// <summary>A screen whose sequence is one playlist of Html slides (title, seconds, background colour), in order.</summary>
    protected async Task<(Guid ScreenId, string Token, Guid[] SlideIds)> ScreenWithSlidesAsync(params (string Title, int Seconds, string Color)[] slides)
    {
        var tag = Tag();
        var ids = new List<Guid>();
        foreach (var (title, seconds, color) in slides)
        {
            ids.Add(await AdminActions.CreateHtmlSlideAsync(Page, $"{title} {tag}", seconds,
                $"<body style='background:{color};display:grid;place-content:center'><h1 style='font:bold 12vmin system-ui;color:#fff'>{title}</h1></body>"));
        }

        var screenId = await EmptyScreenAsync($"Player {tag}", playlist: slides.Length == 0 ? null : $"Loop {tag}",
            slideTitles: slides.Select(s => $"{s.Title} {tag}").ToArray());
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        return (screenId, token, ids.ToArray());
    }

    protected async Task<Guid> EmptyScreenAsync(string name, string? playlist = null, string[]? slideTitles = null)
    {
        if (playlist is not null)
        {
            await AdminActions.CreatePlaylistAsync(Page, playlist, slideTitles ?? []);
        }
        var screenId = await AdminActions.CreateScreenAsync(Page, name);
        if (playlist is not null)
        {
            await AdminActions.AssignPlaylistAsync(Page, playlist);
        }
        return screenId;
    }

    /// <summary>Opens the player as a device (own context, the screen token from the address).</summary>
    protected async Task<IPage> OpenDeviceAsync(string token, double timeScale, Func<IBrowserContext, Task>? beforeOpen = null)
    {
        var context = await Browser.NewContextAsync(new() { BaseURL = BaseUrl.ToString(), ViewportSize = new() { Width = 1920, Height = 1080 } });
        await context.AddInitScriptAsync(Recorder);
        if (beforeOpen is not null)
        {
            await beforeOpen(context);
        }
        var device = await context.NewPageAsync();
        await device.GotoAsync($"/screen?test=1&timeScale={timeScale.ToString(CultureInfo.InvariantCulture)}&token={token}");
        return device;
    }

    protected static async Task<string[]> WaitForActivationsAsync(IPage device, int count, int timeoutMs = 60_000)
    {
        await device.WaitForFunctionAsync($"() => window.__activations.length >= {count}", null, new() { Timeout = timeoutMs, PollingInterval = 100 });
        return await device.EvaluateAsync<string[]>("() => window.__activations.map(a => a.id)");
    }

    protected static Task<int> ActivationCountAsync(IPage device) => device.EvaluateAsync<int>("() => window.__activations.length");

    protected static async Task ExpectModeAsync(IPage device, string mode) =>
        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-mode", mode, new() { Timeout = 20_000 });
}
