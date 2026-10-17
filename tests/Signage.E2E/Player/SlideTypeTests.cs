using Signage.E2E.Infrastructure;

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
        Assert.True(await device.EvaluateAsync<bool>($$"""
            async () => {
                while (document.querySelector('.slide[data-state=active]')?.dataset.slideId !== '{{image}}') await new Promise(r => setTimeout(r, 50));
                const img = document.querySelector('.slide[data-state=active] img');
                return img.complete && img.naturalWidth > 0;
            }
            """), "the image is decoded when it is shown");
        await device.Context.CloseAsync();
    }
}
