using System.Text.Json;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>WP-09: video slides (R-22, R-07) and single video mode (R-14), with the generated test clips.</summary>
[Trait("Category", "Player")]
public class VideoTests(BrowserFixture fixture) : PlayerTest(fixture)
{
    private static Uri Media(string path) => new(E2EEnvironment.MediaBaseUrl, path);

    [Fact]
    public async Task A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends()
    {
        var tag = Tag();
        await AdminActions.CreateHtmlSlideAsync(Page, $"Before {tag}", 4);
        var video = await AdminActions.CreateUrlSlideAsync(Page, "Video", $"Clip {tag}", Media("/media/clip-b-h264.mp4"));
        var after = await AdminActions.CreateHtmlSlideAsync(Page, $"After {tag}", 4);
        await EmptyScreenAsync($"Video {tag}", $"Video {tag}", [$"Before {tag}", $"Clip {tag}", $"After {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 1);

        // Every animation frame of the transition into the video, then a moment after it has arrived.
        var observed = await device.EvaluateAsync<JsonElement>("""
            () => new Promise((resolve) => {
                const during = [];
                let seen = false;
                function frame() {
                    const layer = document.querySelector('.slide[data-type="Video"]');
                    const v = layer?.querySelector('video');
                    if (layer?.classList.contains('animating')) {
                        seen = true;
                        during.push({ time: v.currentTime, paused: v.paused, x: new DOMMatrixReadOnly(getComputedStyle(layer).transform).m41 });
                    } else if (seen) {
                        setTimeout(() => resolve({ during, after: v.currentTime, muted: v.muted, autoplay: v.autoplay, inline: v.playsInline, preload: v.preload }), 1000);
                        return;
                    }
                    requestAnimationFrame(frame);
                }
                requestAnimationFrame(frame);
            })
            """);

        var during = observed.GetProperty("during").EnumerateArray().ToList();
        Assert.True(during.Count >= 10, $"only {during.Count} frames sampled");
        Assert.All(during, f => Assert.Equal(0, f.GetProperty("time").GetDouble()));
        Assert.All(during, f => Assert.True(f.GetProperty("paused").GetBoolean()));
        Assert.True(observed.GetProperty("after").GetDouble() > 0.3, "it plays once in view");
        Assert.True(observed.GetProperty("muted").GetBoolean()); // R-07: muted unless the slide plays audio
        Assert.False(observed.GetProperty("autoplay").GetBoolean());
        Assert.True(observed.GetProperty("inline").GetBoolean());
        Assert.Equal("auto", observed.GetProperty("preload").GetString());

        // The progress bar follows playback (R-21), and the next slide comes when the video ends, not after a default time.
        var duration = await device.EvaluateAsync<double>("() => document.querySelector('.slide[data-state=active] video').duration");
        var fill = await device.EvaluateAsync<double>("() => new DOMMatrixReadOnly(getComputedStyle(document.querySelector('[data-testid=progress] .fill')).transform).a");
        Assert.InRange(fill, 0.05, 0.95);
        var played = await WaitForActivationsAsync(device, 3, timeoutMs: 30_000);
        Assert.Equal([video.ToString(), after.ToString()], played[1..3]);
        var times = await ActivationTimesAsync(device);
        var shown = times[2] - times[1];
        Assert.InRange(shown, duration * 1000, duration * 1000 + 2000); // the video's length plus the 900 ms transition
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_video_that_cannot_load_is_skipped()
    {
        var tag = Tag();
        var first = await AdminActions.CreateHtmlSlideAsync(Page, $"One {tag}", 2);
        var broken = await AdminActions.CreateUrlSlideAsync(Page, "Video", $"Missing clip {tag}", Media($"/media/missing-{tag}.mp4"));
        var second = await AdminActions.CreateHtmlSlideAsync(Page, $"Two {tag}", 2);
        await EmptyScreenAsync($"Broken video {tag}", $"Broken video {tag}", [$"One {tag}", $"Missing clip {tag}", $"Two {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        var device = await OpenDeviceAsync(token, timeScale: 0.5);
        var played = await WaitForActivationsAsync(device, 5);

        Assert.DoesNotContain(broken.ToString(), played);
        Assert.Equal([first.ToString(), second.ToString(), first.ToString(), second.ToString(), first.ToString()], played.Take(5));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task Single_video_mode_loops_without_a_progress_bar_and_mode_changes_apply_at_once()
    {
        // 200 s slides at this scale stay 20 s: a mode change must not wait for the slide to end.
        var (screenId, token, _) = await ScreenWithSlidesAsync(("Long A", 200, "#1e3a8a"), ("Long B", 200, "#7c2d12"));
        var device = await OpenDeviceAsync(token, timeScale: 0.1);
        await WaitForActivationsAsync(device, 1);

        await AdminActions.SetScreenModeAsync(Page, screenId, Media("/media/clip-c-h264.mp4"));

        await ExpectModeAsync(device, "single-video");
        Assert.Equal(1, await ActivationCountAsync(device)); // the carousel slide was cut short, no transition
        var single = device.Locator("[data-testid=single-video] video");
        Assert.True(await single.EvaluateAsync<bool>("v => v.loop && v.muted"));
        await Expect(device.GetByTestId("progress")).ToBeHiddenAsync();
        await Expect(device.Locator(".slides > .slide")).ToHaveCountAsync(0); // the carousel slide is released
        await device.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "player-single-video.png") });

        // It loops: playback wraps around instead of ending.
        Assert.True(await single.EvaluateAsync<bool>("""
            (v) => new Promise((resolve) => {
                let last = v.currentTime;
                const timer = setInterval(() => {
                    if (v.ended) { clearInterval(timer); resolve(false); }
                    if (v.currentTime + 0.5 < last) { clearInterval(timer); resolve(true); }
                    last = v.currentTime;
                }, 100);
                setTimeout(() => { clearInterval(timer); resolve(false); }, (v.duration + 3) * 1000);
            })
            """), "the single video wraps around");

        // Back to the carousel, again without waiting.
        await AdminActions.SetScreenModeAsync(Page, screenId, null);
        await ExpectModeAsync(device, "player");
        await Expect(device.GetByTestId("single-video")).ToHaveCountAsync(0);
        await device.Context.CloseAsync();
    }

    private const string Wraps = """
        (v) => new Promise((resolve) => {
            let last = v.currentTime;
            const timer = setInterval(() => {
                if (v.currentTime + 0.5 < last) { clearInterval(timer); resolve(true); }
                last = v.currentTime;
            }, 100);
            setTimeout(() => { clearInterval(timer); resolve(false); }, (v.duration + 4) * 1000);
        })
        """;

    [Fact]
    public async Task A_video_next_to_a_slide_that_cannot_load_plays_again_without_hammering_the_broken_host()
    {
        var tag = Tag();
        await AdminActions.CreateUrlSlideAsync(Page, "Video", $"Clip {tag}", Media("/media/clip-b-h264.mp4"));
        await AdminActions.CreateUrlSlideAsync(Page, "Image", $"Gone {tag}", Media($"/media/gone-{tag}.png"), 2);
        await EmptyScreenAsync($"Video and gone {tag}", $"Video and gone {tag}", [$"Clip {tag}", $"Gone {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var requests = 0;
        var device = await OpenDeviceAsync(token, timeScale: 1, ctx =>
        {
            ctx.Request += (_, r) => { if (r.Url.Contains($"gone-{tag}", StringComparison.Ordinal)) Interlocked.Increment(ref requests); };
            return Task.CompletedTask;
        });
        await WaitForActivationsAsync(device, 1);
        var video = device.Locator(".slide[data-state=active] video");

        // Each time the video ends, the image is tried once and fails; the video then plays again from the start.
        Assert.True(await video.EvaluateAsync<bool>(Wraps), "the video plays again");
        Assert.True(await video.EvaluateAsync<bool>(Wraps), "and again");
        Assert.InRange(requests, 1, 4); // one try per round, not a busy loop
        Assert.False(await video.EvaluateAsync<bool>("v => v.paused || v.ended"));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_video_that_becomes_the_only_slide_while_it_plays_loops()
    {
        var tag = Tag();
        await AdminActions.CreateHtmlSlideAsync(Page, $"Ends {tag}", 2, activeUntilLocal: "2030-01-02T10:02");
        await AdminActions.CreateUrlSlideAsync(Page, "Video", $"Clip {tag}", Media("/media/clip-b-h264.mp4"));
        await EmptyScreenAsync($"Lone video {tag}", $"Lone video {tag}", [$"Ends {tag}", $"Clip {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await OpenDeviceAsync(token, timeScale: 1, ctx => ctx.Clock.InstallAsync(new() { TimeDate = new DateTime(2030, 1, 2, 10, 0, 0, DateTimeKind.Utc) }));
        await WaitForActivationsAsync(device, 2); // "Ends", then the video
        await device.Clock.FastForwardAsync("03:00"); // the neighbour's window ends while the video plays

        var video = device.Locator(".slide[data-state=active] video");
        Assert.True(await video.EvaluateAsync<bool>(Wraps), "the lone video keeps going");
        Assert.True(await video.EvaluateAsync<bool>(Wraps), "and loops");
        Assert.True(await video.EvaluateAsync<bool>("v => v.loop && !v.ended"));
        Assert.Equal(2, await ActivationCountAsync(device));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_video_that_stops_making_progress_is_skipped_after_8_seconds()
    {
        var tag = Tag();
        await AdminActions.CreateUrlSlideAsync(Page, "Video", $"Clip {tag}", Media("/media/clip-a-h264.mp4"));
        var next = await AdminActions.CreateHtmlSlideAsync(Page, $"Next {tag}", 4);
        await EmptyScreenAsync($"Stall {tag}", $"Stall {tag}", [$"Clip {tag}", $"Next {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 1);

        // Playback stops making progress without an error (as when a stream hangs): freeze it from outside.
        var frozenAt = await device.EvaluateAsync<double>("""
            async () => {
                const v = document.querySelector('.slide[data-state=active] video');
                while (v.currentTime < 0.3) await new Promise((r) => setTimeout(r, 50));
                v.pause();
                return performance.now();
            }
            """);
        var played = await WaitForActivationsAsync(device, 2, timeoutMs: 20_000);
        var times = await ActivationTimesAsync(device);

        Assert.Equal(next.ToString(), played[1]);
        Assert.InRange(times[1] - frozenAt, 8000, 11_000); // the 8 s stall limit, then the transition
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_video_with_sound_plays_with_sound_when_the_browser_allows_it_and_muted_when_it_does_not()
    {
        var tag = Tag();
        await AdminActions.CreateHtmlSlideAsync(Page, $"Intro {tag}", 2);
        await Page.GotoAsync("/admin/slides/create?type=Video");
        await Page.GetByLabel("Title").FillAsync($"Loud {tag}");
        await Page.GetByLabel("Address (URL)").FillAsync(Media("/media/clip-b-h264.mp4").ToString());
        await Page.GetByLabel("Play audio").CheckAsync();
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Create slide" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/slides/[0-9a-f-]{36}$"));
        await EmptyScreenAsync($"Sound {tag}", $"Sound {tag}", [$"Intro {tag}", $"Loud {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 2);

        var result = await device.EvaluateAsync<JsonElement>("""
            async () => {
                const v = document.querySelector('.slide[data-state=active] video');
                while (v.currentTime < 0.3) await new Promise((r) => setTimeout(r, 50));
                // Ask the browser the same question with a separate, unmuted element.
                const probe = document.createElement('video');
                probe.src = v.currentSrc;
                const soundAllowed = await probe.play().then(() => true, (e) => (e.name === 'NotAllowedError' ? false : String(e)));
                probe.pause();
                probe.removeAttribute('src');
                probe.load();
                return { muted: v.muted, playing: !v.paused, soundAllowed };
            }
            """);

        // R-07: the slide asks for sound. A kiosk started with the autoplay policy plays it audibly; a browser without it
        // still plays the video, muted (ADR-0009). Either way it plays.
        var soundAllowed = result.GetProperty("soundAllowed");
        TestContext.Current.TestOutputHelper?.WriteLine($"autoplay with sound allowed: {soundAllowed}");
        Assert.True(soundAllowed.ValueKind is JsonValueKind.True or JsonValueKind.False, $"probe: {soundAllowed}");
        Assert.True(result.GetProperty("playing").GetBoolean());
        Assert.Equal(!soundAllowed.GetBoolean(), result.GetProperty("muted").GetBoolean());
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_video_with_sound_plays_muted_in_a_browser_that_refuses_autoplay_with_sound()
    {
        var tag = Tag();
        await AdminActions.CreateHtmlSlideAsync(Page, $"Intro {tag}", 2);
        await Page.GotoAsync("/admin/slides/create?type=Video");
        await Page.GetByLabel("Title").FillAsync($"Loud {tag}");
        await Page.GetByLabel("Address (URL)").FillAsync(Media("/media/clip-b-h264.mp4").ToString());
        await Page.GetByLabel("Play audio").CheckAsync();
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Create slide" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/admin/slides/[0-9a-f-]{36}$"));
        await EmptyScreenAsync($"Quiet kiosk {tag}", $"Quiet kiosk {tag}", [$"Intro {tag}", $"Loud {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        // A kiosk started without the autoplay-with-sound policy (R-07): unmuted play() is refused, as Chromium does.
        var device = await OpenDeviceAsync(token, timeScale: 1, ctx => ctx.AddInitScriptAsync("""
            const play = HTMLMediaElement.prototype.play;
            HTMLMediaElement.prototype.play = function () {
                return this.muted ? play.call(this) : Promise.reject(new DOMException('blocked', 'NotAllowedError'));
            };
            """));
        await WaitForActivationsAsync(device, 2);

        var video = device.Locator(".slide[data-state=active] video");
        await device.WaitForFunctionAsync("() => document.querySelector('.slide[data-state=active] video')?.currentTime > 0.3", null, new() { Timeout = 10_000 });
        Assert.True(await video.EvaluateAsync<bool>("v => v.muted && !v.paused"), "it plays, muted (ADR-0009)");
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task Single_video_mode_recovers_from_failures_and_leaves_nothing_behind()
    {
        var (screenId, token, _) = await ScreenWithSlidesAsync(("Stale", 200, "#7c2d12"));
        var device = await OpenDeviceAsync(token, timeScale: 0.2); // retries after 2 s, manifest polls every 12 s
        await WaitForActivationsAsync(device, 1);

        // A URL that never plays: the old carousel content must not stay on a screen set to single video.
        await AdminActions.SetScreenModeAsync(Page, screenId, Media($"/media/missing-{Tag()}.mp4"));
        await ExpectModeAsync(device, "single-video");
        await Expect(device.Locator(".slides > .slide")).ToHaveCountAsync(0, new() { Timeout = 20_000 });

        // A good URL plays.
        var clip = Media("/media/clip-c-h264.mp4");
        await AdminActions.SetScreenModeAsync(Page, screenId, clip);
        var visible = device.Locator("[data-testid=single-video].visible video");
        await Expect(visible).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // It fails mid-play and the first retries fail too (the host is down), then the host comes back.
        await device.Context.RouteAsync(clip.ToString(), route => route.AbortAsync("connectionrefused"));
        await visible.EvaluateAsync("v => { v.src = v.src.replace('clip-c', 'not-there'); }");
        await device.WaitForTimeoutAsync(5000); // two failed retries at this scale
        Assert.InRange(await device.Locator("[data-testid=single-video]").CountAsync(), 1, 2);
        await device.Context.UnrouteAsync(clip.ToString());
        await device.WaitForFunctionAsync("() => [...document.querySelectorAll('[data-testid=single-video].visible video')].some(v => v.src.includes('clip-c') && v.currentTime > 0.2)", null, new() { Timeout = 20_000 });
        await Expect(device.Locator("[data-testid=single-video]")).ToHaveCountAsync(1);

        // Back to the carousel: no single video layer is left on top of it.
        await AdminActions.SetScreenModeAsync(Page, screenId, null);
        await ExpectModeAsync(device, "player");
        await Expect(device.Locator("[data-testid=single-video]")).ToHaveCountAsync(0);
        await device.Context.CloseAsync();
    }
}
