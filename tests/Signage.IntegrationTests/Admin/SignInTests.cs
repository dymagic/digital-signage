using System.Net;
using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Features.Users;

namespace Signage.IntegrationTests.Admin;

public sealed class SignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string GenericFailure = "Sign-in failed. Check your email and password, or try again later.";
    private AdminApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private Task<Signage.Web.Domain.UserLockout?> LockoutAsync() => _app.Factory.WithDbAsync(db =>
        db.UserLockouts.AsNoTracking().SingleOrDefaultAsync(l => db.Users.Any(u => u.Id == l.UserId && u.Email == AdminApp.AdminEmail)));

    [Fact]
    public async Task Correct_credentials_sign_in_and_return_to_the_requested_page()
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.PostFormAsync("/admin/login?ReturnUrl=%2Fadmin%2Fsettings",
            new Dictionary<string, string> { ["Input.Email"] = "  ADMIN@example.org ", ["Input.Password"] = AdminApp.Password, ["ReturnUrl"] = "/admin/settings" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/settings", AdminSession.Location(response));
        using var settings = await session.GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    public async Task External_return_urls_are_ignored(string returnUrl)
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.PostFormAsync("/admin/login",
            new Dictionary<string, string> { ["Input.Email"] = AdminApp.AdminEmail, ["Input.Password"] = AdminApp.Password, ["ReturnUrl"] = returnUrl });

        Assert.Equal("/admin", AdminSession.Location(response));
    }

    [Fact]
    public async Task Wrong_password_unknown_user_and_deactivated_user_all_get_the_same_message()
    {
        var editor = await _app.CreateUserAsync();
        await _app.Factory.WithDbAsync(db => db.Users.Where(u => u.Id == editor).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        using var session = new AdminSession(_app.Factory);

        foreach (var (email, password) in new[] { (AdminApp.AdminEmail, "wrong password!"), ("nobody@example.org", AdminApp.Password), (AdminApp.EditorEmail, AdminApp.Password) })
        {
            using var response = await session.SignInAsync(email, password);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(GenericFailure, System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_until_the_lockout_expires()
    {
        using var session = new AdminSession(_app.Factory);
        for (var i = 0; i < UserAccounts.MaxFailedAttempts; i++)
        {
            using var failed = await session.SignInAsync(AdminApp.AdminEmail, "wrong password!");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        using (var locked = await session.SignInAsync(AdminApp.AdminEmail, AdminApp.Password))
        {
            Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        }

        _app.Time.Advance(UserAccounts.LockoutDuration - TimeSpan.FromSeconds(1));
        using (var stillLocked = await session.SignInAsync(AdminApp.AdminEmail, AdminApp.Password))
        {
            Assert.Equal(HttpStatusCode.OK, stillLocked.StatusCode);
        }

        _app.Time.Advance(TimeSpan.FromSeconds(1));
        using var unlocked = await session.SignInAsync(AdminApp.AdminEmail, AdminApp.Password);
        Assert.Equal(HttpStatusCode.Redirect, unlocked.StatusCode);
        Assert.Null(await LockoutAsync());
    }

    [Fact]
    public async Task A_burst_of_parallel_guesses_gets_at_most_the_allowed_number_of_password_checks()
    {
        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            using var session = new AdminSession(_app.Factory);
            using var response = await session.SignInAsync(AdminApp.AdminEmail, "wrong password!");
            return response.StatusCode;
        });

        var statuses = await Task.WhenAll(attempts);

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
        // Every checked password claims one try first; once the lockout is set no further claim succeeds,
        // so the counter proves exactly MaxFailedAttempts passwords were checked.
        var lockout = await LockoutAsync();
        Assert.NotNull(lockout);
        Assert.Equal(UserAccounts.MaxFailedAttempts, lockout.AccessFailedCount);
        Assert.Equal(_app.Time.GetUtcNow() + UserAccounts.LockoutDuration, lockout.LockoutEnd);
        using var correct = await new AdminSession(_app.Factory).SignInAsync(AdminApp.AdminEmail, AdminApp.Password);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
    }

    [Fact]
    public async Task After_a_lockout_expires_the_count_starts_again()
    {
        using var session = new AdminSession(_app.Factory);
        for (var i = 0; i < UserAccounts.MaxFailedAttempts; i++)
        {
            (await session.SignInAsync(AdminApp.AdminEmail, "wrong password!")).Dispose();
        }

        _app.Time.Advance(UserAccounts.LockoutDuration);
        (await session.SignInAsync(AdminApp.AdminEmail, "wrong password!")).Dispose();

        // One failure after expiry must not lock again; the correct password still works.
        var lockout = await LockoutAsync();
        Assert.NotNull(lockout);
        Assert.Equal(1, lockout.AccessFailedCount);
        Assert.Null(lockout.LockoutEnd);
        await session.SignInSuccessfullyAsync(AdminApp.AdminEmail, AdminApp.Password);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failure_count()
    {
        using var session = new AdminSession(_app.Factory);
        for (var i = 0; i < UserAccounts.MaxFailedAttempts - 1; i++)
        {
            (await session.SignInAsync(AdminApp.AdminEmail, "wrong password!")).Dispose();
        }

        await session.SignInSuccessfullyAsync(AdminApp.AdminEmail, AdminApp.Password);
        using var another = new AdminSession(_app.Factory);
        (await another.SignInAsync(AdminApp.AdminEmail, "wrong password!")).Dispose();
        await another.SignInSuccessfullyAsync(AdminApp.AdminEmail, AdminApp.Password);
    }

    [Fact]
    public async Task Posting_without_an_antiforgery_token_is_rejected()
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.PostWithoutTokenAsync("/admin/login",
            new Dictionary<string, string> { ["Input.Email"] = AdminApp.AdminEmail, ["Input.Password"] = AdminApp.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_auth_cookie_is_http_only_and_same_site_lax_and_secure_over_https()
    {
        using var http = new AdminSession(_app.Factory);
        using var https = new AdminSession(_app.Factory, https: true);

        using var overHttp = await http.SignInAsync(AdminApp.AdminEmail, AdminApp.Password);
        using var overHttps = await https.SignInAsync(AdminApp.AdminEmail, AdminApp.Password);

        var plain = Assert.Single(overHttp.Headers.GetValues("Set-Cookie"), c => c.StartsWith(UsersServiceCollectionExtensions.CookieName, StringComparison.Ordinal));
        var secure = Assert.Single(overHttps.Headers.GetValues("Set-Cookie"), c => c.StartsWith(UsersServiceCollectionExtensions.CookieName, StringComparison.Ordinal));
        Assert.Contains("httponly", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", secure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_but_a_get_request_does_not()
    {
        using var session = await _app.SignedInAsync(AdminApp.AdminEmail);

        (await session.GetAsync("/admin/logout")).Dispose();
        using (var stillIn = await session.GetAsync("/admin"))
        {
            Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode);
        }

        using var logout = await session.PostFormAsync("/admin", new Dictionary<string, string>(), action: "/admin/logout");
        Assert.Equal("/admin/login", AdminSession.Location(logout));
        using var after = await session.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    [Fact]
    public async Task The_sign_out_form_in_the_layout_carries_exactly_one_antiforgery_token()
    {
        using var session = await _app.SignedInAsync(AdminApp.AdminEmail);
        var html = await session.GetHtmlAsync("/admin");

        var form = System.Text.RegularExpressions.Regex.Match(html, """<form method="post" action="/admin/logout".*?</form>""", System.Text.RegularExpressions.RegexOptions.Singleline).Value;

        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(form, "__RequestVerificationToken"));
    }

    [Fact]
    public async Task The_site_root_redirects_to_the_admin()
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.GetAsync("/");

        Assert.Equal("/admin", AdminSession.Location(response));
    }
}
