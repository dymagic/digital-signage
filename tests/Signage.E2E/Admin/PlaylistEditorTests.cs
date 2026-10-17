using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Admin;

[Trait("Category", "Admin")]
public class PlaylistEditorTests(BrowserFixture fixture) : E2ETest(fixture)
{
    protected override bool SignedInAsAdmin => true;

    private ILocator Entries => Page.Locator("ol.entries > li .entry-title a");

    [Fact]
    public async Task Slides_are_added_reordered_with_buttons_and_by_dragging_and_the_order_persists()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        string[] titles = [$"One {tag}", $"Two {tag}", $"Three {tag}"];
        foreach (var title in titles)
        {
            await AdminActions.CreateHtmlSlideAsync(Page, title);
        }

        await Page.GotoAsync("/admin/playlists/create");
        await Page.GetByLabel("Name").FillAsync($"Lobby loop {tag}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "New playlist" }).ClickAsync();
        await Expect(Page.GetByText("Playlist created.")).ToBeVisibleAsync();

        foreach (var title in titles)
        {
            await Page.GetByLabel("Add a slide").SelectOptionAsync(new SelectOptionValue { Label = $"{title} (HTML)" });
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
            await Expect(Page.Locator("ol.entries > li").Filter(new() { HasText = title })).ToBeVisibleAsync();
        }
        await Expect(Entries).ToHaveTextAsync(titles);

        // Keyboard-accessible path: the "move down" button of the first entry.
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Move \"{titles[0]}\" down" }).ClickAsync();
        await Expect(Entries).ToHaveTextAsync([titles[1], titles[0], titles[2]]);

        // Mouse path: drag the last entry above the first.
        await Page.Locator("ol.entries > li").Nth(2).DragToAsync(Page.Locator("ol.entries > li").Nth(0), new() { TargetPosition = new() { X = 40, Y = 5 } });
        await Expect(Entries).ToHaveTextAsync([titles[2], titles[1], titles[0]]);
        await SaveScreenshotAsync("playlist-editor");

        // Renaming on the same page right after those htmx changes must not hit a stale-version conflict.
        await Page.GetByLabel("Name").FillAsync($"Lobby loop {tag} (renamed)");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(Page.GetByText("Changes saved.")).ToBeVisibleAsync();

        // The order survived the round trip.
        await Expect(Entries).ToHaveTextAsync([titles[2], titles[1], titles[0]]);
    }
}
