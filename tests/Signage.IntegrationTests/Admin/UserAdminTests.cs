using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;
using Signage.Web.Features.Users;

namespace Signage.IntegrationTests.Admin;

/// <summary>R-52: admins create users, set roles, deactivate and reset passwords; new users must change theirs.</summary>
public sealed partial class UserAdminTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private Guid _adminId;
    private AdminSession _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        _adminId = await _app.CreateAdminAsync();
        _admin = await _app.SignedInAsync(AdminApp.AdminEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<string> CreateViaPageAsync(string email, UserRole role = UserRole.Editor)
    {
        using var response = await _admin.PostFormAsync("/admin/users/create", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.DisplayName"] = "New Person",
            ["Input.Role"] = role.ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var match = TemporaryPasswordRegex().Match(html);
        Assert.True(match.Success, "temporary password not shown");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Fact]
    public async Task A_new_user_signs_in_with_the_temporary_password_and_must_change_it_first()
    {
        var temporary = await CreateViaPageAsync("new@example.org");
        using var user = new AdminSession(_app.Factory);
        await user.SignInSuccessfullyAsync("new@example.org", temporary);

        using (var home = await user.GetAsync("/admin"))
        {
            Assert.Equal("/admin/account/password", AdminSession.Location(home));
        }

        using (var changed = await user.PostFormAsync("/admin/account/password", new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = temporary,
            ["Input.NewPassword"] = "my own long passphrase",
            ["Input.ConfirmPassword"] = "my own long passphrase",
        }))
        {
            Assert.Equal("/admin", AdminSession.Location(changed));
        }

        using var after = await user.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var stored = await _app.Factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == "new@example.org"));
        Assert.False(stored.MustChangePassword);
        Assert.Equal(UserRole.Editor, stored.Role);
    }

    [Fact]
    public async Task Creating_a_user_with_an_existing_email_in_any_case_fails()
    {
        await CreateViaPageAsync("dup@example.org");

        using var response = await _admin.PostFormAsync("/admin/users/create", new Dictionary<string, string>
        {
            ["Input.Email"] = "DUP@Example.org",
            ["Input.DisplayName"] = "Again",
            ["Input.Role"] = "Editor",
        });

        Assert.Contains("A user with this email already exists.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.Users.CountAsync(u => u.NormalizedEmail == "DUP@EXAMPLE.ORG")));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@example.org")]
    [InlineData("Name <name@example.org>")]
    public async Task Invalid_emails_are_rejected(string email)
    {
        using var response = await _admin.PostFormAsync("/admin/users/create", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.DisplayName"] = "X",
            ["Input.Role"] = "Editor",
        });

        Assert.Contains("Enter a valid email address.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Resetting_a_password_ends_sessions_and_forces_a_change()
    {
        var editorId = await _app.CreateUserAsync();
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);

        using var reset = await _admin.PostFormAsync($"/admin/users/{editorId}", new Dictionary<string, string>(), action: $"/admin/users/{editorId}?handler=ResetPassword");
        var temporary = WebUtility.HtmlDecode(TemporaryPasswordRegex().Match(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Groups[1].Value);

        Assert.False(string.IsNullOrEmpty(temporary));
        using (var old = await editor.GetAsync("/admin"))
        {
            Assert.Equal(HttpStatusCode.Redirect, old.StatusCode);
        }
        using var oldPassword = await new AdminSession(_app.Factory).SignInAsync(AdminApp.EditorEmail, AdminApp.Password);
        Assert.Equal(HttpStatusCode.OK, oldPassword.StatusCode);
        using var fresh = new AdminSession(_app.Factory);
        await fresh.SignInSuccessfullyAsync(AdminApp.EditorEmail, temporary);
        using var forced = await fresh.GetAsync("/admin");
        Assert.Equal("/admin/account/password", AdminSession.Location(forced));
    }

    [Fact]
    public async Task Resetting_a_password_also_clears_a_lockout()
    {
        var editorId = await _app.CreateUserAsync();
        await _app.Factory.WithDbAsync(db => db.Users.Where(u => u.Id == editorId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, _app.Time.GetUtcNow().AddMinutes(10))));

        using var reset = await _admin.PostFormAsync($"/admin/users/{editorId}", new Dictionary<string, string>(), action: $"/admin/users/{editorId}?handler=ResetPassword");
        var temporary = WebUtility.HtmlDecode(TemporaryPasswordRegex().Match(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Groups[1].Value);

        await new AdminSession(_app.Factory).SignInSuccessfullyAsync(AdminApp.EditorEmail, temporary);
    }

    [Fact]
    public async Task Admins_cannot_change_their_own_role_or_deactivate_themselves()
    {
        var me = await _app.GetUserAsync(_adminId);

        using var response = await _admin.PostFormAsync($"/admin/users/{_adminId}", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = me.DisplayName,
            ["Input.Role"] = "Editor",
            ["Input.IsActive"] = "false",
            ["Input.Version"] = me.Version.ToString(CultureInfo.InvariantCulture),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await _app.GetUserAsync(_adminId);
        Assert.Equal(UserRole.Admin, after.Role);
        Assert.True(after.IsActive);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_demoted_or_deactivated()
    {
        await using var scope = _app.Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
        var me = await _app.GetUserAsync(_adminId);

        // Acting as someone else (e.g. a future maintenance path), so the self-guard doesn't apply.
        var demote = await accounts.UpdateAsync(_adminId, me.DisplayName, UserRole.Editor, true, me.Version, Guid.NewGuid(), TestContext.Current.CancellationToken);
        var deactivate = await accounts.UpdateAsync(_adminId, me.DisplayName, UserRole.Admin, false, me.Version, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(AccountErrors.LastAdmin, demote.ErrorKey);
        Assert.Equal(AccountErrors.LastAdmin, deactivate.ErrorKey);
    }

    [Fact]
    public async Task With_two_admins_one_can_demote_the_other()
    {
        var secondId = await _app.CreateUserAsync("second@example.org", UserRole.Admin);
        var second = await _app.GetUserAsync(secondId);

        using var response = await _admin.PostFormAsync($"/admin/users/{secondId}", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = second.DisplayName,
            ["Input.Role"] = "Editor",
            ["Input.IsActive"] = "true",
            ["Input.Version"] = second.Version.ToString(CultureInfo.InvariantCulture),
        });

        Assert.Equal("/admin/users", AdminSession.Location(response));
        Assert.Equal(UserRole.Editor, (await _app.GetUserAsync(secondId)).Role);
    }

    [Fact]
    public async Task A_stale_edit_form_reports_a_conflict_instead_of_overwriting()
    {
        var editorId = await _app.CreateUserAsync();
        var stale = await _app.GetUserAsync(editorId);
        await _app.Factory.WithDbAsync(db => db.Users.Where(u => u.Id == editorId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "Changed elsewhere")));

        using var response = await _admin.PostFormAsync($"/admin/users/{editorId}", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Mine",
            ["Input.Role"] = "Editor",
            ["Input.IsActive"] = "true",
            ["Input.Version"] = stale.Version.ToString(CultureInfo.InvariantCulture),
        });

        Assert.Contains("Someone else changed this in the meantime.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal("Changed elsewhere", (await _app.GetUserAsync(editorId)).DisplayName);
    }

    [Fact]
    public async Task Editing_an_unknown_user_is_a_404()
    {
        using var response = await _admin.GetAsync($"/admin/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task User_names_are_html_encoded_in_the_list()
    {
        await _app.Factory.WithDbAsync(db => db.Users.Where(u => u.Id == _adminId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "<script>alert(1)</script>")));

        var html = await _admin.GetHtmlAsync("/admin/users");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("""data-testid="temporary-password">([^<]+)<""")]
    private static partial Regex TemporaryPasswordRegex();
}
