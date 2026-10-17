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
}
