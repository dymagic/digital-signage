using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>Global settings (admin only): default duration R-02, info overlay R-30, time zone R-03.</summary>
public sealed class SettingsPageTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private AdminSession _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        _admin = await _app.SignedInAsync(AdminApp.AdminEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _app.DisposeAsync();
    }

    private Task<SystemSettings> CurrentAsync() => _app.Factory.WithDbAsync(db => db.SystemSettings.AsNoTracking().SingleAsync());

    private async Task<HttpResponseMessage> SaveAsync(string duration, string timeZone, bool overlay, uint? version = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Input.DefaultSlideDurationSeconds"] = duration,
            ["Input.TimeZoneId"] = timeZone,
            ["Input.Version"] = (version ?? (await CurrentAsync()).Version).ToString(CultureInfo.InvariantCulture),
        };
        if (overlay)
        {
            form["Input.ShowInfoOverlay"] = "true";
        }
        return await _admin.PostFormAsync("/admin/settings", form);
    }

    [Fact]
    public async Task The_page_shows_the_current_values()
    {
        var html = await _admin.GetHtmlAsync("/admin/settings");

        Assert.Contains("value=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("<option selected=\"selected\">UTC</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option>Europe/Copenhagen</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admins_can_change_all_settings()
    {
        using var response = await SaveAsync("25", "Europe/Copenhagen", overlay: true);

        Assert.Equal("/admin/settings", AdminSession.Location(response));
        var settings = await CurrentAsync();
        Assert.Equal(25, settings.DefaultSlideDurationSeconds);
        Assert.Equal("Europe/Copenhagen", settings.TimeZoneId);
        Assert.True(settings.ShowInfoOverlay);
        Assert.Equal(_app.Time.GetUtcNow(), settings.UpdatedAt);
    }

    [Theory]
    [InlineData("0", "UTC")]
    [InlineData("3601", "UTC")]
    [InlineData("abc", "UTC")]
    [InlineData("10", "Mars/Olympus_Mons")]
    public async Task Invalid_values_are_rejected(string duration, string timeZone)
    {
        using var response = await SaveAsync(duration, timeZone, overlay: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await CurrentAsync();
        Assert.Equal(10, settings.DefaultSlideDurationSeconds);
        Assert.False(settings.ShowInfoOverlay);
    }

    [Fact]
    public async Task A_stale_form_reports_a_conflict()
    {
        var stale = (await CurrentAsync()).Version;
        (await SaveAsync("20", "UTC", overlay: false)).Dispose();

        using var response = await SaveAsync("30", "UTC", overlay: false, stale);

        Assert.Contains("Someone else changed this in the meantime.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(20, (await CurrentAsync()).DefaultSlideDurationSeconds);
    }
}
