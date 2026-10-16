using Microsoft.Playwright;

namespace Signage.E2E.Infrastructure;

/// <summary>
/// Signs in as the E2E admin once per run and shares the session (Playwright storage state) with every test.
/// The compose stack bootstraps that admin with a temporary password, so the first sign-in also goes
/// through the forced password change (R-51/R-52).
/// </summary>
public sealed class AdminLogin(BrowserFixture fixture) : IDisposable
{
    /// <summary>The password the admin ends up with after the forced change. Test-only value.</summary>
    public const string Password = "e2e admin passphrase";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _storageState;

    public static string Email => Environment.GetEnvironmentVariable("SIGNAGE_E2E_ADMIN_EMAIL") ?? "e2e-admin@example.org";

    private static string BootstrapPassword => Environment.GetEnvironmentVariable("SIGNAGE_E2E_ADMIN_BOOTSTRAP_PASSWORD") ?? "e2e-bootstrap-password";

    public async Task<string> StorageStateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_storageState is not null)
            {
                return _storageState;
            }

            await using var context = await fixture.Browser.NewContextAsync(new() { BaseURL = E2EEnvironment.RequireBaseUrl().ToString() });
            var page = await context.NewPageAsync();
            if (!await SignInAsync(page, Password))
            {
                Assert.True(await SignInAsync(page, BootstrapPassword), "Could not sign in with either the bootstrap or the changed password.");
                await page.WaitForURLAsync("**/admin/account/password");
                await page.GetByLabel("Current password").FillAsync(BootstrapPassword);
                await page.GetByLabel("New password").FillAsync(Password);
                await page.GetByLabel("Repeat password").FillAsync(Password);
                await page.GetByRole(AriaRole.Button, new() { Name = "Change password" }).ClickAsync();
                await page.WaitForURLAsync("**/admin");
            }

            _storageState = await context.StorageStateAsync();
            return _storageState;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    /// <summary>Fills the sign-in form; true when the browser leaves the sign-in page.</summary>
    public static async Task<bool> SignInAsync(IPage page, string password)
    {
        await page.GotoAsync("/admin/login");
        await page.GetByLabel("Email").FillAsync(Email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // Either outcome: an admin page (its body has no "auth" class) or a visible sign-in error.
        // Locators retry across the navigation, so this is race-free.
        await page.Locator("body:not(.auth), .notice-error li").First.WaitForAsync();
        return !page.Url.Contains("/admin/login", StringComparison.OrdinalIgnoreCase);
    }
}
