using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Admin;

[Trait("Category", "Admin")]
public class ScreenCompositionTests(BrowserFixture fixture) : E2ETest(fixture)
{
    protected override bool SignedInAsAdmin => true;

    [Fact]
    public async Task A_screen_composed_from_a_group_playlist_and_its_own_slides_shows_the_resolved_sequence()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        string Name(string s) => $"{s} {tag}";
        foreach (var title in new[] { "Welcome", "Menu", "News", "Bye" })
        {
            await AdminActions.CreateHtmlSlideAsync(Page, Name(title));
        }
        await AdminActions.CreatePlaylistAsync(Page, Name("Canteen"), Name("Menu"), Name("News"));

        await Page.GotoAsync("/admin/screens/create");
        await Page.GetByLabel("Name").FillAsync(Name("Lobby TV"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "New screen" }).ClickAsync();
        await Expect(Page.GetByText("Screen created.")).ToBeVisibleAsync();
        var screenUrl = Page.Url;

        await Page.GotoAsync("/admin/groups/create");
        await Page.GetByLabel("Name").FillAsync(Name("Grand hall"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "New group" }).ClickAsync();
        await Expect(Page.GetByText("Group created.")).ToBeVisibleAsync();
        await Page.GetByLabel(Name("Lobby TV")).CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save members" }).ClickAsync();
        await Expect(Page.GetByText("Changes saved.")).ToBeVisibleAsync();
        await Page.GetByLabel("Add a playlist").SelectOptionAsync(new SelectOptionValue { Label = Name("Canteen") });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(Page.Locator("#group-playlists ol.entries > li")).ToHaveCountAsync(1);

        await Page.GotoAsync(screenUrl);
        await Page.GetByLabel("Add a slide at the start").SelectOptionAsync(new SelectOptionValue { Label = $"{Name("Welcome")} (HTML)" });
        await Page.Locator("form.add-slide").Filter(new() { Has = Page.GetByLabel("Add a slide at the start") }).GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await Expect(Page.Locator("[data-testid=resolved-sequence] tbody tr")).ToHaveCountAsync(3);
        await Page.GetByLabel("Add a slide at the end").SelectOptionAsync(new SelectOptionValue { Label = $"{Name("Bye")} (HTML)" });
        await Page.Locator("form.add-slide").Filter(new() { Has = Page.GetByLabel("Add a slide at the end") }).GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();

        var rows = Page.Locator("[data-testid=resolved-sequence] tbody tr td:nth-child(2)");
        await Expect(rows).ToHaveCountAsync(4);
        var titles = (await rows.AllInnerTextsAsync()).Select(t => t.Split(" · ")[0]).ToArray();
        Assert.Equal([Name("Welcome"), Name("Menu"), Name("News"), Name("Bye")], titles);
        await Expect(Page.Locator("[data-testid=resolved-sequence]")).ToContainTextAsync($"Group \"{Name("Grand hall")}\" → playlist \"{Name("Canteen")}\"");
        await SaveScreenshotAsync("screen-detail");
    }

    [Fact]
    public async Task Keyboard_reordering_keeps_focus_on_the_moved_entry()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        string[] titles = [$"First {tag}", $"Second {tag}"];
        foreach (var title in titles)
        {
            await AdminActions.CreateHtmlSlideAsync(Page, title);
        }
        await AdminActions.CreatePlaylistAsync(Page, $"Focus {tag}", titles);

        await Page.GetByRole(AriaRole.Button, new() { Name = $"Move \"{titles[0]}\" down" }).FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        // After the swap the moved entry is last, so "down" is disabled and focus lands on its "up" button.
        await Expect(Page.Locator("ol.entries > li .entry-title a")).ToHaveTextAsync([titles[1], titles[0]]);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Move \"{titles[0]}\" up" })).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("ol.entries > li .entry-title a")).ToHaveTextAsync(titles);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Move \"{titles[0]}\" down" })).ToBeFocusedAsync();

        // Removing an entry moves focus to the remove button of the entry that took its place.
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Remove \"{titles[0]}\" from the playlist" }).FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("ol.entries > li .entry-title a")).ToHaveTextAsync([titles[1]]);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Remove \"{titles[1]}\" from the playlist" })).ToBeFocusedAsync();
    }
}
