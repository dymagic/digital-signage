using System.Net;
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

    [Theory]
    [InlineData("/admin/users/create", "Input.Email=x%40example.org&Input.DisplayName=X&Input.Role=Admin")]
    [InlineData("/admin/settings", "Input.DefaultSlideDurationSeconds=5&Input.TimeZoneId=UTC&Input.Version=0")]
    public async Task Editors_cannot_post_to_admin_only_pages(string path, string body)
    {
        using var session = await _app.SignedInAsync(AdminApp.EditorEmail);
        var token = await session.GetAntiforgeryTokenAsync("/admin/account/password");
        using var content = new StringContent($"{body}&__RequestVerificationToken={Uri.EscapeDataString(token)}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await session.Http.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(response), StringComparison.Ordinal);
        Assert.Equal(2, await _app.Factory.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Users)));
    }

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
