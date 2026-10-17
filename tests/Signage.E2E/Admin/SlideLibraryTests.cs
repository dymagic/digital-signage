using Microsoft.Playwright;
using Signage.E2E.Infrastructure;

namespace Signage.E2E.Admin;

[Trait("Category", "Admin")]
public class SlideLibraryTests(BrowserFixture fixture) : E2ETest(fixture)
{
    protected override bool SignedInAsAdmin => true;

    [Fact]
    public async Task An_html_slide_created_in_the_browser_shows_up_in_the_library_with_a_sandboxed_preview()
    {
        var title = $"Welcome board {Guid.NewGuid():N}"[..24];
        await Page.GotoAsync("/admin/slides");
        await Page.GetByRole(AriaRole.Link, new() { Name = "+ HTML" }).ClickAsync();
        await Page.WaitForURLAsync("**/admin/slides/create?type=Html");

        await Page.GetByLabel("Title").FillAsync(title);
        await Page.GetByLabel("HTML", new() { Exact = true }).FillAsync(
            "<style>body{display:grid;place-items:center;height:100vh;color:#fff;font:bold 120px system-ui}</style><body>Welcome!</body>");
        await Page.GetByLabel("Duration (seconds)").FillAsync("20");
        await SaveScreenshotAsync("slides-create-html");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create slide" }).ClickAsync();

        await Page.WaitForURLAsync("**/admin/slides/*");
        Assert.True(await Page.GetByRole(AriaRole.Status).GetByText("Slide created.").IsVisibleAsync());
        var preview = Page.Locator(".preview-pane iframe");
        Assert.Equal("allow-scripts", await preview.GetAttributeAsync("sandbox"));
        await SaveScreenshotAsync("slides-edit-html");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to the slide library" }).ClickAsync();
        var row = Page.Locator("tr", new() { HasText = title });
        Assert.True(await row.IsVisibleAsync());
        Assert.Contains("20 s", await row.InnerTextAsync(), StringComparison.Ordinal);
        await SaveScreenshotAsync("slides-library");
    }

    [Fact]
    public async Task A_web_page_slide_explains_that_some_sites_refuse_framing()
    {
        await Page.GotoAsync("/admin/slides/create?type=WebPage");

        Assert.True(await Page.GetByText("Some websites refuse to be shown inside another page").IsVisibleAsync());
        Assert.False(await Page.GetByLabel("Play audio").IsVisibleAsync());
    }

    [Fact]
    public async Task A_video_slide_offers_play_audio_with_a_kiosk_hint_and_no_duration()
    {
        await Page.GotoAsync("/admin/slides/create?type=Video");

        Assert.True(await Page.GetByLabel("Play audio").IsVisibleAsync());
        Assert.True(await Page.GetByText("autoplay-with-sound policy").IsVisibleAsync());
        Assert.Equal(0, await Page.GetByLabel("Duration (seconds)").CountAsync());
    }
}
