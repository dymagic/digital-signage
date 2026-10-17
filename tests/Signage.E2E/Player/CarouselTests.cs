using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>WP-08 acceptance: order, transition, progress bar, layer cap, offline (R-20, R-21, R-23, R-24, R-13).</summary>
[Trait("Category", "Player")]
public class CarouselTests(BrowserFixture fixture) : PlayerTest(fixture)
{
    [Fact]
    public async Task Slides_play_in_the_resolved_sequence_and_loop()
    {
        var (screenId, token, ids) = await ScreenWithSlidesAsync(("One", 2, "#7f1d1d"), ("Two", 2, "#14532d"), ("Three", 2, "#1e3a8a"));
        var device = await OpenDeviceAsync(token, timeScale: 0.25);

        var played = await WaitForActivationsAsync(device, 7);

        // The admin's resolved sequence (R-13) is what the screen plays.
        await Page.GotoAsync($"/admin/screens/{screenId}");
        var sequence = await Page.Locator("[data-testid=resolved-sequence] tbody tr").EvaluateAllAsync<string[]>("rows => rows.map(r => r.dataset.slideId)");
        Assert.Equal(ids.Select(i => i.ToString()), sequence);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => sequence[i % 3]), played.Take(7));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task The_incoming_slide_slides_over_a_still_outgoing_slide_with_a_shadow_and_firm_easing()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("White", 2, "#ffffff"), ("Black", 2, "#000000"));
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 1);

        // While the white slide shows, the next one waits off-screen and must cast no shadow into view (PLAYER_SPEC §2).
        await device.WaitForFunctionAsync("() => document.querySelector('.slide[data-state=entering]')");
        Assert.Equal("none", await device.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.slide[data-state=entering]')).boxShadow"));

        // Sample every animation frame of the next transition, in the page.
        var samples = await device.EvaluateAsync<JsonElement>("""
            () => new Promise((resolve) => {
                const frames = [];
                const x = (el) => new DOMMatrixReadOnly(getComputedStyle(el).transform).m41;
                function frame() {
                    const incoming = document.querySelector('.slide.animating');
                    const outgoing = document.querySelector('.slide[data-state="leaving"]');
                    if (incoming && outgoing) {
                        frames.push({ t: performance.now(), inX: x(incoming), outX: x(outgoing), shadow: getComputedStyle(incoming).boxShadow, layers: document.querySelector('.slides').childElementCount });
                    } else if (frames.length > 0) {
                        resolve(frames);
                        return;
                    }
                    requestAnimationFrame(frame);
                }
                requestAnimationFrame(frame);
            })
            """);

        var frames = samples.EnumerateArray().Select(f => (T: f.GetProperty("t").GetDouble(), In: f.GetProperty("inX").GetDouble(), Out: f.GetProperty("outX").GetDouble(),
            Shadow: f.GetProperty("shadow").GetString()!, Layers: f.GetProperty("layers").GetInt32())).ToList();
        Assert.True(frames.Count >= 10, $"only {frames.Count} frames sampled");
        Assert.All(frames, f => Assert.Equal(0, f.Out)); // the outgoing slide never moves
        Assert.All(frames, f => Assert.Contains("rgba(0, 0, 0, 0.45)", f.Shadow, StringComparison.Ordinal));
        Assert.All(frames, f => Assert.InRange(f.Layers, 2, 3));
        Assert.True(frames[0].In > 1000, $"starts off-screen right, was {frames[0].In}");
        for (var i = 1; i < frames.Count; i++)
        {
            Assert.True(frames[i].In <= frames[i - 1].In + 0.5, "the incoming slide only moves left");
        }

        // Firm easing (cubic-bezier(0.16, 1, 0.3, 1)): fast start, soft landing — more than half the way after a quarter of the time.
        var start = frames[0].T;
        var quarter = frames.First(f => f.T - start >= 225);
        Assert.True(quarter.In < 1920 / 2.0, $"at 225 ms the slide should be past half way, was at {quarter.In}px");
        Assert.InRange(frames[^1].T - start, 600, 1300);
        await device.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "player-after-transition.png") });
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task The_progress_bar_fills_over_the_slide_duration_and_never_more_than_three_layers_exist()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("A", 4, "#334155"), ("B", 4, "#7c2d12"));
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 2, timeoutMs: 30_000);

        var readings = await device.EvaluateAsync<double[]>("""
            async () => {
                const fill = document.querySelector('[data-testid=progress] .fill');
                const scale = () => new DOMMatrixReadOnly(getComputedStyle(fill).transform).a;
                const at = (ms) => new Promise((r) => setTimeout(() => r(scale()), ms));
                return [await at(1000), await at(1000), await at(1000)]; // about 1 s, 2 s and 3 s into a 4 s slide
            }
            """);

        Assert.True(readings[0] is > 0.1 and < 0.45, $"1 s: {readings[0]}");
        Assert.True(readings[1] is > 0.35 and < 0.7, $"2 s: {readings[1]}");
        Assert.True(readings[2] is > 0.6 and < 0.95, $"3 s: {readings[2]}");
        await WaitForActivationsAsync(device, 5, timeoutMs: 40_000);
        Assert.InRange(await device.EvaluateAsync<int>("() => window.__maxLayers"), 1, 3);
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task It_keeps_cycling_when_the_server_is_unreachable_and_starts_offline_from_its_cache()
    {
        var (_, token, ids) = await ScreenWithSlidesAsync(("North", 2, "#0f766e"), ("South", 2, "#a16207"));
        var device = await OpenDeviceAsync(token, timeScale: 0.25);
        await WaitForActivationsAsync(device, 2);

        // The server goes away.
        await device.Context.RouteAsync("**/api/player/**", route => route.AbortAsync("connectionrefused"));
        var before = await ActivationCountAsync(device);
        var played = await WaitForActivationsAsync(device, before + 4);
        Assert.Subset(ids.Select(i => i.ToString()).ToHashSet(), played.Skip(before).ToHashSet());

        // The device restarts while the server is still away: it plays its cached manifest at once (R-24).
        await device.ReloadAsync();
        await ExpectModeAsync(device, "player");
        var afterRestart = await WaitForActivationsAsync(device, 3);
        Assert.Subset(ids.Select(i => i.ToString()).ToHashSet(), afterRestart.ToHashSet());
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_screen_without_content_shows_its_name_and_a_clock_not_an_error()
    {
        var tag = Tag();
        await EmptyScreenAsync($"Quiet corner {tag}");
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        var device = await OpenDeviceAsync(token, timeScale: 1);

        await ExpectModeAsync(device, "empty");
        await Expect(device.GetByTestId("empty-name")).ToHaveTextAsync($"Quiet corner {tag}");
        await Expect(device.GetByTestId("empty-clock")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d{2}:\d{2}$"));
        await Expect(device.GetByTestId("progress")).ToBeHiddenAsync();
        await device.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "player-empty.png") });
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_single_slide_stays_without_transitions_or_progress_bar()
    {
        var (_, token, _) = await ScreenWithSlidesAsync(("Only", 1, "#4c1d95"));
        var device = await OpenDeviceAsync(token, timeScale: 0.25);

        await WaitForActivationsAsync(device, 1);
        await device.WaitForTimeoutAsync(2000); // 8 slide lengths at this time scale

        Assert.Equal(1, await ActivationCountAsync(device));
        await Expect(device.GetByTestId("progress")).ToBeHiddenAsync();
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task An_edit_to_the_only_slide_of_a_screen_shows_up_without_a_reload()
    {
        var (_, token, ids) = await ScreenWithSlidesAsync(("Notice", 10, "#1e293b"));
        // From the second manifest poll on (every 6 s at this scale) the slide has new HTML, as after an edit (R-25).
        var polls = 0;
        var device = await OpenDeviceAsync(token, timeScale: 0.1, ctx => ctx.RouteAsync("**/api/player/manifest", async route =>
        {
            var headers = route.Request.Headers.Where(h => !h.Key.Equals("if-none-match", StringComparison.OrdinalIgnoreCase)).ToDictionary();
            var response = await route.FetchAsync(new() { Headers = headers });
            var manifest = JsonNode.Parse(await response.TextAsync())!.AsObject();
            if (Interlocked.Increment(ref polls) > 1)
            {
                manifest["slides"]![0]!["html"] = "<h1 data-edited>Edited notice</h1>";
            }
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = manifest.ToJsonString() });
        }));
        await WaitForActivationsAsync(device, 1);
        await device.EvaluateAsync("() => { window.__marker = 'same page'; }");

        var played = await WaitForActivationsAsync(device, 2, timeoutMs: 30_000);

        Assert.Equal([ids[0].ToString(), ids[0].ToString()], played);
        var active = device.Locator(".slide[data-state=active] iframe");
        Assert.Contains("Edited notice", await active.GetAttributeAsync("srcdoc"), StringComparison.Ordinal);
        Assert.Equal("same page", await device.EvaluateAsync<string>("() => window.__marker"));
        await device.WaitForTimeoutAsync(1500); // and it settles: no further swaps for an unchanged slide
        Assert.Equal(2, await ActivationCountAsync(device));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task An_admin_previews_the_screen_in_their_own_browser()
    {
        var (screenId, _, ids) = await ScreenWithSlidesAsync(("Preview A", 1, "#1d4ed8"), ("Preview B", 1, "#be123c"));
        await Page.Context.AddInitScriptAsync("window.__activations = []; new MutationObserver(rs => rs.forEach(r => { if (r.target.classList?.contains('slide') && r.target.dataset.state === 'active') window.__activations.push({ id: r.target.dataset.slideId }); })).observe(document, { subtree: true, attributes: true, attributeFilter: ['data-state'] });");

        await Page.GotoAsync($"/screen?preview={screenId}&test=1&timeScale=0.5");

        await Expect(Page.Locator("body")).ToHaveAttributeAsync("data-preview", "true");
        var played = await WaitForActivationsAsync(Page, 3);
        Assert.Equal([ids[0].ToString(), ids[1].ToString(), ids[0].ToString()], played.Take(3));
    }

    [Fact]
    public async Task A_new_manifest_during_a_slide_takes_effect_at_the_next_transition_without_lengthening_the_slide()
    {
        var (_, token, ids) = await ScreenWithSlidesAsync(("Red", 20, "#7f1d1d"), ("Green", 20, "#14532d"), ("Blue", 20, "#1e3a8a"));
        // Every other manifest (polled every 6 s at this scale) drops "Green", so the prepared next slide keeps changing.
        var polls = 0;
        var device = await OpenDeviceAsync(token, timeScale: 0.1, ctx => ctx.RouteAsync("**/api/player/manifest", async route =>
        {
            var headers = route.Request.Headers.Where(h => !h.Key.Equals("if-none-match", StringComparison.OrdinalIgnoreCase)).ToDictionary();
            var response = await route.FetchAsync(new() { Headers = headers });
            var manifest = JsonNode.Parse(await response.TextAsync())!.AsObject();
            if (Interlocked.Increment(ref polls) % 2 == 0)
            {
                var slides = manifest["slides"]!.AsArray();
                slides.Remove(slides.First(s => (string?)s!["id"] == ids[1].ToString()));
            }
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = manifest.ToJsonString() });
        }));

        var played = await WaitForActivationsAsync(device, 14);

        Assert.True(polls >= 3, $"only {polls} manifest polls");
        Assert.Contains(ids[1].ToString(), played); // with Green
        Assert.Contains(played.Zip(played.Skip(1)), p => p.First == ids[0].ToString() && p.Second == ids[2].ToString()); // and without
        // 2 s per slide plus a 90 ms transition; a second dwell on the same slide would take over 4 s.
        var gap = await LongestGapAsync(device);
        Assert.True(gap < 3000, $"a slide stayed {gap:F0} ms");
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_kiosk_opened_with_its_screen_address_plays_its_cache_while_the_server_is_unreachable()
    {
        var (_, token, ids) = await ScreenWithSlidesAsync(("East", 2, "#155e75"), ("West", 2, "#9a3412"));
        var device = await OpenDeviceAsync(token, timeScale: 0.25);
        await WaitForActivationsAsync(device, 2);

        // Power cut: the kiosk restarts with its configured address while the server is still down (R-24, R-41).
        await device.Context.RouteAsync("**/api/player/**", route => route.AbortAsync("connectionrefused"));
        await device.GotoAsync($"/screen?test=1&timeScale=0.25&token={token}");

        await ExpectModeAsync(device, "player");
        var played = await WaitForActivationsAsync(device, 3);
        Assert.Subset(ids.Select(i => i.ToString()).ToHashSet(), played.ToHashSet());
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task Previewing_a_screen_that_no_longer_exists_says_so()
    {
        await Page.GotoAsync($"/screen?preview={Guid.NewGuid()}");

        await Expect(Page.Locator(".message")).ToHaveTextAsync("This screen no longer exists.");
    }
}
