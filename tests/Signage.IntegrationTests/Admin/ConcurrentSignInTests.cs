using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>
/// Someone holding a leaked password can keep signing in and changing the password. That must never undo an
/// admin's reset or block a deactivation (R-52, ADR-0004). A gated hasher parks the user's request in the middle
/// of its password check, so each interleaving is forced deterministically.
/// </summary>
public sealed partial class ConcurrentSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatedPasswordHasher _hasher = new();
    private AdminApp _app = null!;
    private Guid _editorId;
    private AdminSession _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres, configureServices: s => s.Replace(ServiceDescriptor.Singleton<IPasswordHasher<User>>(_hasher)));
        await _app.CreateAdminAsync();
        _editorId = await _app.CreateUserAsync();
        _admin = await _app.SignedInAsync(AdminApp.AdminEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<string> ResetEditorPasswordAsync()
    {
        using var reset = await _admin.PostFormAsync($"/admin/users/{_editorId}", new Dictionary<string, string>(), action: $"/admin/users/{_editorId}?handler=ResetPassword");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        return WebUtility.HtmlDecode(TemporaryPasswordRegex().Match(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Groups[1].Value);
    }

    private async Task<bool> StoredPasswordIsAsync(string password)
    {
        var user = await _app.GetUserAsync(_editorId);
        return new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, password) != PasswordVerificationResult.Failed;
    }

    [Fact]
    public async Task Signing_in_never_changes_the_user_rows_version()
    {
        var before = (await _app.GetUserAsync(_editorId)).Version;

        await new AdminSession(_app.Factory).SignInSuccessfullyAsync(AdminApp.EditorEmail, AdminApp.Password);
        for (var i = 0; i < 6; i++)
        {
            using var session = new AdminSession(_app.Factory);
            (await session.SignInAsync(AdminApp.EditorEmail, "wrong password!")).Dispose();
        }

        Assert.Equal(before, (await _app.GetUserAsync(_editorId)).Version);
    }

    [Fact]
    public async Task An_admin_reset_that_lands_during_the_users_own_password_change_wins()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var gate = _hasher.Arm(AdminApp.Password);

        // The editor's change is parked right inside the check of their current password...
        var change = editor.PostFormAsync("/admin/account/password", new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = AdminApp.Password,
            ["Input.NewPassword"] = "attacker passphrase",
            ["Input.ConfirmPassword"] = "attacker passphrase",
        });
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // ...while the admin resets the password.
        var temporary = await ResetEditorPasswordAsync();
        gate.Release();
        using var changed = await change;

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Contains("Someone else changed this in the meantime.", await changed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.True(await StoredPasswordIsAsync(temporary));
        Assert.False(await StoredPasswordIsAsync("attacker passphrase"));
        Assert.True((await _app.GetUserAsync(_editorId)).MustChangePassword);
    }

    [Fact]
    public async Task An_admin_reset_that_lands_during_a_sign_in_wins_and_that_sign_in_gets_no_usable_session()
    {
        using var gate = _hasher.Arm(AdminApp.Password);
        using var attacker = new AdminSession(_app.Factory);

        var signIn = attacker.SignInAsync(AdminApp.EditorEmail, AdminApp.Password);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var temporary = await ResetEditorPasswordAsync();
        gate.Release();
        (await signIn).Dispose();

        Assert.True(await StoredPasswordIsAsync(temporary));
        Assert.False(await StoredPasswordIsAsync(AdminApp.Password));
        using var afterwards = await attacker.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, afterwards.StatusCode);
        Assert.StartsWith("http://localhost/admin/login", AdminSession.Location(afterwards), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deactivation_form_opened_before_the_users_own_password_changes_still_saves()
    {
        var formVersion = (await _app.GetUserAsync(_editorId)).Version;
        using (var editor = await _app.SignedInAsync(AdminApp.EditorEmail))
        {
            for (var i = 0; i < 3; i++)
            {
                var from = i == 0 ? AdminApp.Password : $"own passphrase {i - 1}";
                using var changed = await editor.PostFormAsync("/admin/account/password", new Dictionary<string, string>
                {
                    ["Input.CurrentPassword"] = from,
                    ["Input.NewPassword"] = $"own passphrase {i}",
                    ["Input.ConfirmPassword"] = $"own passphrase {i}",
                });
                Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
            }
        }

        using var response = await _admin.PostFormAsync($"/admin/users/{_editorId}", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = AdminApp.EditorEmail,
            ["Input.Role"] = "Editor",
            ["Input.IsActive"] = "false",
            ["Input.Version"] = formVersion.ToString(CultureInfo.InvariantCulture),
        });

        Assert.Equal("/admin/users", AdminSession.Location(response));
        Assert.False((await _app.GetUserAsync(_editorId)).IsActive);
    }

    [GeneratedRegex("""data-testid="temporary-password">([^<]+)<""")]
    private static partial Regex TemporaryPasswordRegex();
}
