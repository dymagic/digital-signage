using Microsoft.Playwright;
using Signage.E2E.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Signage.E2E.Player;

/// <summary>R-40–R-42 end to end: a device and an admin in two separate browser contexts.</summary>
[Trait("Category", "Player")]
public class PairingFlowTests(BrowserFixture fixture) : E2ETest(fixture)
{
    // The device runs with sped-up timing (manifest re-check every 3 s instead of 60 s).
    private const string DeviceUrl = "/screen?test=1&timeScale=0.05";

    protected override bool SignedInAsAdmin => true;

    private async Task<IPage> NewDeviceAsync()
    {
        var context = await Browser.NewContextAsync(new() { BaseURL = BaseUrl.ToString(), ViewportSize = new() { Width = 1920, Height = 1080 } });
        return await context.NewPageAsync();
    }

    [Fact]
    public async Task A_device_shows_a_code_the_admin_claims_it_the_device_plays_and_a_revoke_sends_it_back()
    {
        var device = await NewDeviceAsync();
        await device.GotoAsync(DeviceUrl);
        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-mode", "pairing");
        var codeLocator = device.GetByTestId("pairing-code");
        await Expect(codeLocator).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^[A-Z0-9]{3}-[A-Z0-9]{3}$"));
        var code = await codeLocator.InnerTextAsync();
        await device.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, "screens", "player-pairing.png") });

        var name = $"Lobby {Guid.NewGuid():N}"[..14];
        await Page.GotoAsync("/admin/screens/pair");
        await Page.GetByLabel("Code shown on the screen").FillAsync(code.ToLowerInvariant());
        await Page.GetByLabel("Name of the new screen").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Pair screen" }).ClickAsync();
        await Expect(Page.GetByText("Code accepted.")).ToBeVisibleAsync();

        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-mode", "player", new() { Timeout = 15_000 });
        await Expect(device.GetByTestId("screen-name")).ToHaveTextAsync(name);

        await Page.ReloadAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Revoke" }).ClickAsync();
        await Expect(Page.GetByText("Access revoked.")).ToBeVisibleAsync();

        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-mode", "pairing", new() { Timeout = 15_000 });
        await Expect(device.GetByTestId("pairing-code")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^[A-Z0-9]{3}-[A-Z0-9]{3}$"));
        await device.Context.CloseAsync();
    }

    [Fact]
    public async Task A_pre_configured_screen_address_pairs_the_device_and_disappears_from_the_address_bar()
    {
        var name = $"Kiosk {Guid.NewGuid():N}"[..13];
        await Page.GotoAsync("/admin/screens/create");
        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByRole(AriaRole.Button, new() { Name = "New screen" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create screen address" }).ClickAsync();
        var address = await Page.GetByTestId("screen-address").InnerTextAsync();
        var token = new Uri(address).Query.Split("token=")[1];

        var device = await NewDeviceAsync();
        await device.GotoAsync($"{DeviceUrl}&token={token}");

        await Expect(device.Locator("body")).ToHaveAttributeAsync("data-mode", "player", new() { Timeout = 15_000 });
        await Expect(device.GetByTestId("screen-name")).ToHaveTextAsync(name);
        Assert.DoesNotContain("token=", device.Url, StringComparison.Ordinal);
        Assert.Contains("timeScale=0.05", device.Url, StringComparison.Ordinal);
        await device.Context.CloseAsync();
    }
}
