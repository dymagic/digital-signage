using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>Image and WebPage slides in a real browser, and a broken image being skipped (PLAYER_SPEC §2, §5).</summary>
[Trait("Category", "Player")]
public class SlideTypeTests(BrowserFixture fixture) : PlayerTest(fixture)
{
    [Fact]
    public async Task Images_and_web_pages_play_and_a_broken_image_is_skipped()
    {
        var tag = Tag();
        var media = E2EEnvironment.MediaBaseUrl;
        var image = await AdminActions.CreateUrlSlideAsync(Page, "Image", $"Bars {tag}", new Uri(media, "/media/image-smptebars.png"), 2);
        var broken = await AdminActions.CreateUrlSlideAsync(Page, "Image", $"Missing {tag}", new Uri(media, "/media/does-not-exist.png"), 2);
        var webPage = await AdminActions.CreateUrlSlideAsync(Page, "WebPage", $"Site {tag}", new Uri(media, "/"), 2);
        await EmptyScreenAsync($"Types {tag}", $"Types {tag}", [$"Bars {tag}", $"Missing {tag}", $"Site {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        var device = await OpenDeviceAsync(token, timeScale: 0.5);
        var played = await WaitForActivationsAsync(device, 5);

        Assert.DoesNotContain(broken.ToString(), played);
        Assert.Equal([image.ToString(), webPage.ToString(), image.ToString(), webPage.ToString(), image.ToString()], played.Take(5));
        // 1 s per slide plus a 450 ms transition: skipping the broken image must not show the previous slide twice as long.
        var gap = await LongestGapAsync(device);
        Assert.True(gap < 2200, $"a slide stayed {gap:F0} ms");
        Assert.True(await device.EvaluateAsync<bool>($$"""
            async () => {
                while (document.querySelector('.slide[data-state=active]')?.dataset.slideId !== '{{image}}') await new Promise(r => setTimeout(r, 50));
                const img = document.querySelector('.slide[data-state=active] img');
                return img.complete && img.naturalWidth > 0;
            }
            """), "the image is decoded when it is shown");
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_screen_whose_slides_all_fail_to_load_shows_the_empty_state_and_retries_calmly()
    {
        var tag = Tag();
        var media = E2EEnvironment.MediaBaseUrl;
        await AdminActions.CreateUrlSlideAsync(Page, "Image", $"Gone A {tag}", new Uri(media, $"/media/gone-a-{tag}.png"), 2);
        await AdminActions.CreateUrlSlideAsync(Page, "Image", $"Gone B {tag}", new Uri(media, $"/media/gone-b-{tag}.png"), 2);
        await EmptyScreenAsync($"Broken {tag}", $"Broken {tag}", [$"Gone A {tag}", $"Gone B {tag}"]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        var requests = 0;
        var device = await OpenDeviceAsync(token, timeScale: 0.5, async ctx =>
        {
            ctx.Request += (_, r) => { if (r.Url.Contains($"gone-", StringComparison.Ordinal) && r.Url.Contains(tag, StringComparison.Ordinal)) Interlocked.Increment(ref requests); };
            await ctx.Clock.InstallAsync();
        });

        await ExpectModeAsync(device, "empty");
        await Expect(device.GetByTestId("empty-name")).ToHaveTextAsync($"Broken {tag}");
        // Stop the page clock, then move it by hand: the retry is due 5 s (at this scale) after the failed pass.
        await device.Clock.PauseAtAsync(DateTimeOffset.FromUnixTimeMilliseconds((long)await device.EvaluateAsync<double>("() => Date.now()") + 10).UtcDateTime);
        var afterPass = requests;
        await device.Clock.RunForAsync(4000);
        await device.WaitForTimeoutAsync(500); // real time for any request to show up
        Assert.Equal(afterPass, requests); // no tight loop
        Assert.InRange(afterPass, 2, 4);
        await device.Clock.RunForAsync(1500);
        for (var i = 0; i < 50 && requests == afterPass; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken); // real time for the retry's requests
        }
        Assert.True(requests > afterPass, "it retries after 5 s");
        await device.Context.CloseAsync();
    }
}
