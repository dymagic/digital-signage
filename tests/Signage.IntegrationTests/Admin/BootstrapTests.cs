using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>R-51: SIGNAGE__BOOTSTRAP__ADMINEMAIL / ADMINPASSWORD.</summary>
public class BootstrapTests(PostgresFixture postgres)
{
    private static Dictionary<string, string?> Bootstrap(string? email, string? password) => new()
    {
        ["Signage:Bootstrap:AdminEmail"] = email,
        ["Signage:Bootstrap:AdminPassword"] = password,
    };

    [Fact]
    public async Task On_an_empty_database_the_bootstrap_admin_is_created_and_must_change_the_password()
    {
        await using var app = await AdminApp.StartAsync(postgres, Bootstrap("boot@example.org", "bootstrap password 1"));

        var user = await app.Factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync());
        Assert.Equal("boot@example.org", user.Email);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.True(user.MustChangePassword);

        using var session = new AdminSession(app.Factory);
        await session.SignInSuccessfullyAsync("boot@example.org", "bootstrap password 1");
        using var home = await session.GetAsync("/admin");
        Assert.Equal("/admin/account/password", AdminSession.Location(home));
    }

    [Fact]
    public async Task When_users_exist_the_bootstrap_values_are_ignored()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = new SignageWebFactory(connectionString, Bootstrap("boot@example.org", "bootstrap password 1")))
        {
            _ = first.Server;
        }

        await using var second = new SignageWebFactory(connectionString, Bootstrap("other@example.org", "another password 2"));
        _ = second.Server;

        var emails = await second.WithDbAsync(db => db.Users.Select(u => u.Email).ToListAsync());
        Assert.Equal(["boot@example.org"], emails);
    }

    [Theory]
    [InlineData("boot@example.org", null)]
    [InlineData(null, "bootstrap password 1")]
    [InlineData("boot@example.org", "short")]
    [InlineData("not an email", "bootstrap password 1")]
    public async Task Incomplete_or_invalid_bootstrap_values_stop_start_up(string? email, string? password)
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync(), Bootstrap(email, password));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Server);

        Assert.Contains("SIGNAGE__BOOTSTRAP__", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap password 1", ex.Message, StringComparison.Ordinal);
    }
}
