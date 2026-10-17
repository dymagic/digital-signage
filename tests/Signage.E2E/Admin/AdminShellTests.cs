using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Signage.E2E.Infrastructure;

namespace Signage.E2E.Admin;

[Trait("Category", "Admin")]
public class AdminShellTests(BrowserFixture fixture) : E2ETest(fixture)
{
    protected override bool SignedInAsAdmin => true;

    [Fact]
    public async Task The_admin_reaches_every_admin_section_from_the_navigation()
    {
        await Page.GotoAsync("/admin");
        Assert.Equal("Overview", await Page.Locator("h1").InnerTextAsync());
        await SaveScreenshotAsync("admin-overview");

        await Page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Users" }).ClickAsync();
        await Page.WaitForURLAsync("**/admin/users");
        await Expect(Page.GetByRole(AriaRole.Cell, new() { Name = AdminLogin.Email }).First).ToBeVisibleAsync();
        await SaveScreenshotAsync("admin-users");

        await Page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Settings" }).ClickAsync();
        await Page.WaitForURLAsync("**/admin/settings");
        Assert.Equal("10", await Page.GetByLabel("Default slide duration (seconds)").InputValueAsync());
        await SaveScreenshotAsync("admin-settings");
    }

    [Fact]
    public async Task Signing_out_returns_to_the_sign_in_page()
    {
        await Page.GotoAsync("/admin");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await Page.WaitForURLAsync("**/admin/login");
    }
}

[Trait("Category", "Admin")]
public class SignInPageTests(BrowserFixture fixture) : E2ETest(fixture)
{
    [Fact]
    public async Task A_wrong_password_shows_a_friendly_message_and_stays_on_the_page()
    {
        await Page.GotoAsync("/admin");
        await Page.WaitForURLAsync("**/admin/login**");
        await SaveScreenshotAsync("admin-login");

        Assert.False(await AdminLogin.SignInAsync(Page, "definitely wrong"));

        await Expect(Page.GetByRole(AriaRole.Alert).GetByText("Sign-in failed")).ToBeVisibleAsync();
    }
}
