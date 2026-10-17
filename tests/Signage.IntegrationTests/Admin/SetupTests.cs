using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;
using Signage.Web.Features.Users;

namespace Signage.IntegrationTests.Admin;

/// <summary>R-51 first run.</summary>
public sealed class SetupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;

    public async ValueTask InitializeAsync() => _app = await AdminApp.StartAsync(postgres);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private static Dictionary<string, string> Form(string password = AdminApp.Password, string? confirm = null) => new()
    {
        ["Input.Email"] = "first@example.org",
        ["Input.DisplayName"] = "First Admin",
        ["Input.Password"] = password,
        ["Input.ConfirmPassword"] = confirm ?? password,
    };

    [Fact]
    public async Task With_no_users_sign_in_leads_to_setup()
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.GetAsync("/admin/login");

        Assert.Equal("/admin/setup", AdminSession.Location(response));
    }

    [Fact]
    public async Task Setup_creates_a_signed_in_admin_and_then_disappears()
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.PostFormAsync("/admin/setup", Form());

        Assert.Equal("/admin", AdminSession.Location(response));
        var user = await _app.Factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync());
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.MustChangePassword);
        Assert.NotEqual(AdminApp.Password, user.PasswordHash);
        using (var home = await session.GetAsync("/admin"))
        {
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        }

        using var anonymous = new AdminSession(_app.Factory);
        using var get = await anonymous.GetAsync("/admin/setup");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        // A replayed or crafted POST can't create a second admin either.
        var token = await anonymous.GetAntiforgeryTokenAsync("/admin/login");
        var form = Form();
        form["Input.Email"] = "intruder@example.org";
        form["__RequestVerificationToken"] = token;
        using var post = await anonymous.Http.PostAsync(new Uri("/admin/setup", UriKind.Relative), new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.Users.CountAsync()));
    }

    [Theory]
    [InlineData("short", null)]
    [InlineData(AdminApp.Password, "something else")]
    public async Task Setup_rejects_weak_or_mismatched_passwords(string password, string? confirm)
    {
        using var session = new AdminSession(_app.Factory);

        using var response = await session.PostFormAsync("/admin/setup", Form(password, confirm));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await _app.Factory.WithDbAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Concurrent_first_admin_requests_create_exactly_one_user()
    {
        async Task<bool> Attempt(int i)
        {
            await using var scope = _app.Factory.Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
            var result = await accounts.CreateFirstAdminAsync($"admin{i}@example.org", "A", AdminApp.Password, false, TestContext.Current.CancellationToken);
            return result.Succeeded;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(Attempt));

        Assert.Single(results, r => r);
        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.Users.CountAsync()));
    }
}
