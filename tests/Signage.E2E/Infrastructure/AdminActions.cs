using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Infrastructure;

/// <summary>Common admin actions done through the UI, the way a person would.</summary>
public static partial class AdminActions
{
    /// <summary>Creates an Html slide; returns its id. Without <paramref name="html"/> it shows the title in big letters.</summary>
    public static async Task<Guid> CreateHtmlSlideAsync(IPage page, string title, int? durationSeconds = null, string? html = null, string? activeUntilLocal = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GotoAsync("/admin/slides/create?type=Html");
        await page.GetByLabel("Title").FillAsync(title);
        await page.GetByLabel("HTML", new() { Exact = true }).FillAsync(html ?? $"<h1 style='color:#fff;font:bold 96px system-ui'>{title}</h1>");
        if (durationSeconds is { } seconds)
        {
            await page.GetByLabel("Duration (seconds)").FillAsync(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (activeUntilLocal is not null)
        {
            await page.GetByLabel("Active until").FillAsync(activeUntilLocal);
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "Create slide" }).ClickAsync();
        await page.WaitForURLAsync(SlideEditUrl());
        return Guid.Parse(new Uri(page.Url).Segments[^1]);
    }

    /// <summary>Creates an Image or WebPage slide pointing at <paramref name="url"/>; returns its id.</summary>
    public static async Task<Guid> CreateUrlSlideAsync(IPage page, string type, string title, Uri url, int durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(url);
        await page.GotoAsync($"/admin/slides/create?type={type}");
        await page.GetByLabel("Title").FillAsync(title);
        await page.GetByLabel("Address (URL)").FillAsync(url.ToString());
        await page.GetByLabel("Duration (seconds)").FillAsync(durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByRole(AriaRole.Button, new() { Name = "Create slide" }).ClickAsync();
        await page.WaitForURLAsync(SlideEditUrl());
        return Guid.Parse(new Uri(page.Url).Segments[^1]);
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
            var option = page.GetByLabel("Add a slide").Locator("option").Filter(new() { HasTextRegex = new Regex($"^{Regex.Escape(title)} \\(") });
            await page.GetByLabel("Add a slide").SelectOptionAsync(await option.GetAttributeAsync("value") ?? "");
            await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
            await Expect(page.Locator("ol.entries > li").Filter(new() { HasText = title })).ToBeVisibleAsync();
        }
    }

    /// <summary>Creates a screen; returns its id (the screen page is open afterwards).</summary>
    public static async Task<Guid> CreateScreenAsync(IPage page, string name)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GotoAsync("/admin/screens/create");
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "New screen" }).ClickAsync();
        await Expect(page.GetByText("Screen created.")).ToBeVisibleAsync();
        return Guid.Parse(new Uri(page.Url).Segments[^1]);
    }

    /// <summary>Assigns a playlist directly to the screen whose page is open.</summary>
    public static async Task AssignPlaylistAsync(IPage page, string playlistName)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.GetByLabel("Add a playlist").SelectOptionAsync(new SelectOptionValue { Label = playlistName });
        await page.Locator("form.add-slide").Filter(new() { Has = page.GetByLabel("Add a playlist") }).GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await Expect(page.Locator("#composition ol.entries > li").Filter(new() { HasText = playlistName })).ToBeVisibleAsync();
    }

    /// <summary>Creates a pre-configured screen address for the screen whose page is open; returns its token.</summary>
    public static async Task<string> CreateScreenAddressTokenAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        void Accept(object? sender, IDialog dialog) => _ = dialog.AcceptAsync();
        page.Dialog += Accept;
        await page.GetByRole(AriaRole.Button, new() { Name = "Create screen address" }).ClickAsync();
        var address = await page.GetByTestId("screen-address").InnerTextAsync();
        page.Dialog -= Accept;
        return new Uri(address).Query.Split("token=")[1];
    }

    [GeneratedRegex("/admin/slides/[0-9a-f-]{36}$")]
    public static partial Regex SlideEditUrl();
}
