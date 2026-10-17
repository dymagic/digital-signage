using System.Net;
using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>An existing cookie stops working as soon as access changes (security stamp, R-52/R-53).</summary>
public sealed class SessionInvalidationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private Guid _editorId;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        _editorId = await _app.CreateUserAsync();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private async Task<HttpResponseMessage> EditEditorAsync(AdminSession admin, UserRole role, bool active)
    {
        var user = await _app.GetUserAsync(_editorId);
        return await admin.PostFormAsync($"/admin/users/{_editorId}", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = user.DisplayName,
            ["Input.Role"] = role.ToString(),
            ["Input.IsActive"] = active ? "true" : "false",
            ["Input.Version"] = user.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_session_and_blocks_sign_in()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var admin = await _app.SignedInAsync(AdminApp.AdminEmail);

        using (var saved = await EditEditorAsync(admin, UserRole.Editor, active: false))
        {
            Assert.Equal("/admin/users", AdminSession.Location(saved));
        }

        using var response = await editor.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/admin/login", AdminSession.Location(response), StringComparison.Ordinal);
        using var again = await new AdminSession(_app.Factory).SignInAsync(AdminApp.EditorEmail, AdminApp.Password);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Changing_a_role_ends_the_session_so_the_new_role_applies_on_next_sign_in()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var admin = await _app.SignedInAsync(AdminApp.AdminEmail);

        (await EditEditorAsync(admin, UserRole.Admin, active: true)).Dispose();

        using (var old = await editor.GetAsync("/admin"))
        {
            Assert.Equal(HttpStatusCode.Redirect, old.StatusCode);
        }

        using var promoted = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var users = await promoted.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
    }

    [Fact]
    public async Task Changing_only_the_display_name_keeps_the_session()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var admin = await _app.SignedInAsync(AdminApp.AdminEmail);
        var user = await _app.GetUserAsync(_editorId);

        (await admin.PostFormAsync($"/admin/users/{_editorId}", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Renamed Editor",
            ["Input.Role"] = "Editor",
            ["Input.IsActive"] = "true",
            ["Input.Version"] = user.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })).Dispose();

        var html = await editor.GetHtmlAsync("/admin");
        Assert.Contains("Renamed Editor", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_user_row_ends_the_session()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);

        await _app.Factory.WithDbAsync(db => db.Users.Where(u => u.Id == _editorId).ExecuteDeleteAsync());

        using var response = await editor.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_current_passwords_on_the_change_page_count_towards_lockout()
    {
        using var session = await _app.SignedInAsync(AdminApp.EditorEmail);

        for (var i = 0; i < Signage.Web.Features.Users.UserAccounts.MaxFailedAttempts; i++)
        {
            using var wrong = await session.PostFormAsync("/admin/account/password", new Dictionary<string, string>
            {
                ["Input.CurrentPassword"] = $"guess number {i}",
                ["Input.NewPassword"] = "a brand new passphrase",
                ["Input.ConfirmPassword"] = "a brand new passphrase",
            });
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        }

        // Now even the right current password is refused, and signing in is blocked too.
        using var right = await session.PostFormAsync("/admin/account/password", new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = AdminApp.Password,
            ["Input.NewPassword"] = "a brand new passphrase",
            ["Input.ConfirmPassword"] = "a brand new passphrase",
        });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        using var signIn = await new AdminSession(_app.Factory).SignInAsync(AdminApp.EditorEmail, AdminApp.Password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task Changing_your_own_password_ends_your_other_sessions_but_keeps_this_one()
    {
        using var laptop = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var tablet = await _app.SignedInAsync(AdminApp.EditorEmail);

        using var changed = await laptop.PostFormAsync("/admin/account/password", new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = AdminApp.Password,
            ["Input.NewPassword"] = "a brand new passphrase",
            ["Input.ConfirmPassword"] = "a brand new passphrase",
        });

        Assert.Equal("/admin", AdminSession.Location(changed));
        using (var stillIn = await laptop.GetAsync("/admin"))
        {
            Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode);
        }
        using var kickedOut = await tablet.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, kickedOut.StatusCode);
    }
}
