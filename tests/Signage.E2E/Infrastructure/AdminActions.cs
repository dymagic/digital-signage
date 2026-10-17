using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Infrastructure;

/// <summary>Common admin actions done through the UI, the way a person would.</summary>
public static partial class AdminActions
{
    public static async Task CreateHtmlSlideAsync(IPage page, string title)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GotoAsync("/admin/slides/create?type=Html");
        await page.GetByLabel("Title").FillAsync(title);
        await page.GetByLabel("HTML", new() { Exact = true }).FillAsync($"<h1 style='color:#fff;font:bold 96px system-ui'>{title}</h1>");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create slide" }).ClickAsync();
        await page.WaitForURLAsync(SlideEditUrl());
    }

    /// <summary>Creates a playlist and adds the slides (by title) in order. Returns with the playlist page open.</summary>
    public static async Task CreatePlaylistAsync(IPage page, string name, params string[] slideTitles)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GotoAsync("/admin/playlists/create");
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "New playlist" }).ClickAsync();
        await Expect(page.GetByText("Playlist created.")).ToBeVisibleAsync();
        foreach (var title in slideTitles)
        {
            await page.GetByLabel("Add a slide").SelectOptionAsync(new SelectOptionValue { Label = $"{title} (HTML)" });
            await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
            await Expect(page.Locator("ol.entries > li").Filter(new() { HasText = title })).ToBeVisibleAsync();
        }
    }

    [GeneratedRegex("/admin/slides/[0-9a-f-]{36}$")]
    public static partial Regex SlideEditUrl();
}
