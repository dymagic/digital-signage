using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Docs;

/// <summary>
/// Takes the README screenshots (R-65) from the running app with tidy example content. Opt-in: run
/// <c>scripts/update-readme-screenshots.sh</c>, which uses a fresh database and copies the JPEGs to <c>docs/images/</c>.
/// </summary>
[Trait("Category", "Docs")]
public class ReadmeScreenshots(BrowserFixture fixture) : PlayerTest(fixture)
{
    private static string Shot(string name) => Path.Combine(E2EEnvironment.ArtifactsDir, "readme", name);

    private static async Task SaveAsync(IPage page, string name) =>
        await page.ScreenshotAsync(new() { Path = Shot(name), Type = ScreenshotType.Jpeg, Quality = 80 });

    private const string Welcome = """
        <style>body{display:grid;place-content:center;text-align:center;background:linear-gradient(135deg,#0f2027,#203a43,#2c5364)}
        h1{font-size:9vmin;margin:0;font-weight:600}p{font-size:3.4vmin;opacity:.8;margin:2vmin 0 0}</style>
        <h1>Welcome to the office</h1><p>Lunch is served from 11:30 in the canteen.</p>
        """;

    private const string Menu = """
        <style>body{display:grid;place-content:center;background:#111}h2{font-size:6vmin;margin:0 0 2vmin;font-weight:600}
        li{font-size:4vmin;line-height:1.6}</style>
        <div><h2>This week</h2><ul><li>Tuesday: town hall at 14:00</li><li>Thursday: fire drill</li><li>Friday: breakfast together</li></ul></div>
        """;

    [Fact]
    public async Task Take_the_README_screenshots()
    {
        Assert.SkipUnless(E2EEnvironment.ReadmeScreenshots, "README screenshots: run scripts/update-readme-screenshots.sh.");
        Directory.CreateDirectory(Path.Combine(E2EEnvironment.ArtifactsDir, "readme"));
        await Page.SetViewportSizeAsync(1440, 900);

        // Content, the way an editor builds it.
        await AdminActions.CreateHtmlSlideAsync(Page, "Welcome", 12, Welcome);
        await AdminActions.CreateUrlSlideAsync(Page, "Image", "Fractal poster", new Uri(E2EEnvironment.MediaBaseUrl, "/media/image-mandelbrot.png"), 10);
        await AdminActions.CreateHtmlSlideAsync(Page, "This week", 15, Menu);
        await AdminActions.CreateUrlSlideAsync(Page, "Video", "Test pattern clip", new Uri(E2EEnvironment.MediaBaseUrl, "/media/clip-a-h264.mp4"));
        await AdminActions.CreatePlaylistAsync(Page, "Reception loop", "Welcome", "Fractal poster", "This week", "Test pattern clip");
        await SaveAsync(Page, "admin-playlist.jpg");
        await Page.GotoAsync("/admin/slides");
        await SaveAsync(Page, "admin-slides.jpg");

        var screenId = await AdminActions.CreateScreenAsync(Page, "Reception");
        await AdminActions.AssignPlaylistAsync(Page, "Reception loop");
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);

        // A screen playing it.
        var device = await OpenDeviceAsync(token, timeScale: 1);
        await WaitForActivationsAsync(device, 1);
        await device.WaitForTimeoutAsync(4000); // the progress bar a third of the way
        await SaveAsync(device, "player.jpg");

        // The admin sees it online.
        await Page.GotoAsync($"/admin/screens/{screenId}");
        await SaveAsync(Page, "admin-screen.jpg");
        await Page.GotoAsync("/admin/screens");
        await Expect(Page.GetByTestId("screen-status")).ToHaveTextAsync("Online", new() { Timeout = 20_000 });
        await SaveAsync(Page, "admin-screens.jpg");

        // Identify it.
        await Page.GotoAsync($"/admin/screens/{screenId}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Identify", Exact = true }).ClickAsync();
        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-identify", "one", new() { Timeout = 5000 });
        await SaveAsync(device, "player-identify.jpg");
        await device.Context.CloseAsync();

        // A new device asks to be paired.
        var fresh = await Browser.NewContextAsync(new() { BaseURL = BaseUrl.ToString(), ViewportSize = new() { Width = 1920, Height = 1080 } });
        var unpaired = await fresh.NewPageAsync();
        await unpaired.GotoAsync("/screen");
        await Expect(unpaired.GetByTestId("pairing-code")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^[A-Z0-9]{3}-[A-Z0-9]{3}$"));
        await SaveAsync(unpaired, "player-pairing.jpg");
        await fresh.CloseAsync();
    }
}
