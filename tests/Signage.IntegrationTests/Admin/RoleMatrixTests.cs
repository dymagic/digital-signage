using System.Net;
using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>PROJECT_BRIEF §3: which role may open which admin page. Extended by later packages as pages arrive.</summary>
public sealed class RoleMatrixTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private Guid _editorId;

    public static TheoryData<string, bool> Pages => new()
    {
        // path, editor allowed (admins are always allowed)
        { "/admin", true },
        { "/admin/account/password", true },
        { "/admin/users", false },
        { "/admin/users/create", false },
        { "/admin/users/{editor}", false },
        { "/admin/settings", false },
    };

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        _editorId = await _app.CreateUserAsync(AdminApp.EditorEmail, UserRole.Editor);
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Anonymous_visitors_are_sent_to_sign_in(string path, bool _)
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.GetAsync(Resolve(path));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/admin/login?ReturnUrl=", AdminSession.Location(response), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Admins_can_open_every_admin_page(string path, bool _)
    {
        using var session = await _app.SignedInAsync(AdminApp.AdminEmail);

        using var response = await session.GetAsync(Resolve(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Editors_can_open_only_content_pages(string path, bool editorAllowed)
    {
        using var session = await _app.SignedInAsync(AdminApp.EditorEmail);

        using var response = await session.GetAsync(Resolve(path));

        if (editorAllowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(response), StringComparison.Ordinal);
        }
    }

    private static async Task<HttpResponseMessage> EditorPostsAsync(AdminSession editor, string path, string body)
    {
        // A token from a page the editor may open; the antiforgery check passes, so only authorization can stop it.
        var token = await editor.GetAntiforgeryTokenAsync("/admin/account/password");
        using var content = new StringContent($"{body}&__RequestVerificationToken={Uri.EscapeDataString(token)}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        return await editor.Http.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);
    }

    private static void AssertForbidden(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editors_cannot_create_users()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);

        using var response = await EditorPostsAsync(editor, "/admin/users/create", "Input.Email=x%40example.org&Input.DisplayName=X&Input.Role=Admin");

        AssertForbidden(response);
        Assert.Equal(2, await _app.Factory.WithDbAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Editors_cannot_change_roles_or_deactivate_users_including_themselves()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        var before = await _app.Factory.WithDbAsync(db => db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync());
        var me = before.Single(u => u.Id == _editorId);
        var admin = before.Single(u => u.Id != _editorId);

        using var promoteSelf = await EditorPostsAsync(editor, $"/admin/users/{_editorId}",
            $"Input.DisplayName=Me&Input.Role=Admin&Input.IsActive=true&Input.Version={me.Version}");
        using var deactivateAdmin = await EditorPostsAsync(editor, $"/admin/users/{admin.Id}",
            $"Input.DisplayName=X&Input.Role=Editor&Input.IsActive=false&Input.Version={admin.Version}");

        AssertForbidden(promoteSelf);
        AssertForbidden(deactivateAdmin);
        var after = await _app.Factory.WithDbAsync(db => db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync());
        Assert.Equal(before.Select(Snapshot), after.Select(Snapshot));
    }

    [Fact]
    public async Task Editors_cannot_reset_anyones_password()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        var admin = await _app.Factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id != _editorId));

        using var response = await EditorPostsAsync(editor, $"/admin/users/{admin.Id}?handler=ResetPassword", "");

        AssertForbidden(response);
        var after = await _app.GetUserAsync(admin.Id);
        Assert.Equal(Snapshot(admin), Snapshot(after));
        await new AdminSession(_app.Factory).SignInSuccessfullyAsync(AdminApp.AdminEmail, AdminApp.Password);
    }

    [Fact]
    public async Task Editors_cannot_change_settings()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        var before = await _app.Factory.WithDbAsync(db => db.SystemSettings.AsNoTracking().SingleAsync());

        using var response = await EditorPostsAsync(editor, "/admin/settings",
            $"Input.DefaultSlideDurationSeconds=5&Input.TimeZoneId=Europe%2FCopenhagen&Input.ShowInfoOverlay=true&Input.Version={before.Version}");

        AssertForbidden(response);
        var after = await _app.Factory.WithDbAsync(db => db.SystemSettings.AsNoTracking().SingleAsync());
        Assert.Equal((before.DefaultSlideDurationSeconds, before.TimeZoneId, before.ShowInfoOverlay, before.Version),
            (after.DefaultSlideDurationSeconds, after.TimeZoneId, after.ShowInfoOverlay, after.Version));
    }

    private static (string, UserRole, bool, string?, string, bool) Snapshot(User u) =>
        (u.DisplayName, u.Role, u.IsActive, u.PasswordHash, u.SecurityStamp, u.MustChangePassword);

    [Fact]
    public async Task The_forbidden_page_answers_403()
    {
        using var session = await _app.SignedInAsync(AdminApp.EditorEmail);

        using var response = await session.GetAsync("/admin/forbidden");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Editors_do_not_see_admin_navigation()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        using var admin = await _app.SignedInAsync(AdminApp.AdminEmail);

        var editorHtml = await editor.GetHtmlAsync("/admin");
        var adminHtml = await admin.GetHtmlAsync("/admin");

        Assert.DoesNotContain("href=\"/admin/users\"", editorHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/users\"", adminHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/settings\"", adminHtml, StringComparison.Ordinal);
    }

    private string Resolve(string path) => path.Replace("{editor}", _editorId.ToString(), StringComparison.Ordinal);
}
