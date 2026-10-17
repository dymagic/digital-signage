using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;
using Signage.Web.Features.Events;
using Signage.Web.Features.Pairing;

namespace Signage.IntegrationTests.Player;

/// <summary>WP-06: pairing (R-40), the pre-configured screen address (R-41), revoke and re-pair (R-42), R-53.</summary>
public sealed partial class PairingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly RecordingScreenNotifier _notifier = new();
    private AdminApp _app = null!;
    private AdminSession _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres, configureServices: s => s.Replace(ServiceDescriptor.Singleton<IScreenNotifier>(_notifier)));
        await _app.CreateAdminAsync();
        _admin = await _app.SignedInAsync(AdminApp.AdminEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> ClaimNewAsync(string code, string name = "Canteen TV") =>
        await _admin.PostFormAsync("/admin/screens/pair", new Dictionary<string, string> { ["Code"] = code, ["Target"] = "new", ["NewName"] = name });

    private async Task<HttpResponseMessage> ClaimExistingAsync(string code, Guid screenId, bool repairLink = false) =>
        await _admin.PostFormAsync(repairLink ? $"/admin/screens/pair?repair={screenId}" : "/admin/screens/pair",
            new Dictionary<string, string> { ["Code"] = code, ["Target"] = "existing", ["ScreenId"] = screenId.ToString() },
            action: repairLink ? $"/admin/screens/pair?repair={screenId}" : null);

    private async Task<(PlayerClient Device, Guid ScreenId)> PairNewDeviceAsync(string name = "Canteen TV")
    {
        var device = new PlayerClient(_app.Factory);
        var pairing = await device.StartPairingAsync();
        using var claim = await ClaimNewAsync(pairing.Code, name);
        Assert.Equal(HttpStatusCode.Redirect, claim.StatusCode);
        Assert.Equal("paired", await device.PollStatusAsync(pairing));
        var screenId = Guid.Parse(AdminSession.Location(claim)!.Split('/')[^1]);
        return (device, screenId);
    }

    private static string ScreenCookie(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("signage.screen=", StringComparison.Ordinal));

    [Fact]
    public async Task A_device_gets_a_formatted_code_valid_for_15_minutes_and_only_hashes_are_stored()
    {
        using var device = new PlayerClient(_app.Factory);

        var pairing = await device.StartPairingAsync();

        Assert.Matches("^[A-Z0-9]{3}-[A-Z0-9]{3}$", pairing.Code);
        Assert.Equal(_app.Time.GetUtcNow() + TimeSpan.FromMinutes(15), pairing.ExpiresAt);
        var stored = await _app.Factory.WithDbAsync(db => db.PairingRequests.AsNoTracking().SingleAsync());
        Assert.Equal(pairing.Code.Replace("-", "", StringComparison.Ordinal), stored.Code);
        Assert.Equal(ScreenTokens.Hash(pairing.DeviceSecret), stored.DeviceSecretHash);
        Assert.Equal("pending", await device.PollStatusAsync(pairing));
    }

    [Fact]
    public async Task After_an_admin_claims_the_code_the_device_receives_the_token_once_as_an_http_only_cookie()
    {
        using var device = new PlayerClient(_app.Factory);
        var pairing = await device.StartPairingAsync();

        using var claim = await ClaimNewAsync(pairing.Code.ToLowerInvariant(), "Canteen TV");
        var screenId = Guid.Parse(AdminSession.Location(claim)!.Split('/')[^1]);

        using var first = await device.PollAsync(pairing);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            Assert.Equal("paired", json.RootElement.GetProperty("status").GetString());
            Assert.Equal("Canteen TV", json.RootElement.GetProperty("screen").GetProperty("name").GetString());
        }

        var cookie = ScreenCookie(first);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/player", cookie, StringComparison.OrdinalIgnoreCase);
        var token = cookie.Split(';')[0]["signage.screen=".Length..];
        var screen = await _app.Factory.WithDbAsync(db => db.Screens.AsNoTracking().SingleAsync(s => s.Id == screenId));
        Assert.Equal(ScreenTokens.Hash(token), screen.TokenHash);
        Assert.Equal(_app.Time.GetUtcNow(), screen.PairedAt);
        Assert.Contains(screenId, _notifier.NotifiedScreens);

        using var second = await device.PollAsync(pairing);
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        Assert.False(second.Headers.Contains("Set-Cookie"));

        using var manifest = await device.GetManifestAsync();
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Contains("Canteen TV", await manifest.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Polling_with_the_wrong_device_secret_reveals_nothing()
    {
        using var device = new PlayerClient(_app.Factory);
        var pairing = await device.StartPairingAsync();
        (await ClaimNewAsync(pairing.Code)).Dispose();

        using var response = await device.PollAsync(pairing, secret: ScreenTokens.NewSecret());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("paired", await device.PollStatusAsync(pairing)); // the real device still gets it
    }

    [Fact]
    public async Task Codes_expire_after_15_minutes_and_can_no_longer_be_claimed()
    {
        using var device = new PlayerClient(_app.Factory);
        var pairing = await device.StartPairingAsync();

        _app.Time.Advance(TimeSpan.FromMinutes(15));

        using var poll = await device.PollAsync(pairing);
        Assert.Equal(HttpStatusCode.Gone, poll.StatusCode);
        using var claim = await ClaimNewAsync(pairing.Code);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Contains("No screen is showing that code right now.", WebUtility.HtmlDecode(await claim.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(0, await _app.Factory.WithDbAsync(db => db.Screens.CountAsync()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOPE")]
    [InlineData("000-000")]
    public async Task Invalid_codes_are_refused(string code)
    {
        using var claim = await ClaimNewAsync(code);

        Assert.Contains("No screen is showing that code right now.", WebUtility.HtmlDecode(await claim.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_can_be_claimed_only_once()
    {
        using var device = new PlayerClient(_app.Factory);
        var pairing = await device.StartPairingAsync();
        (await ClaimNewAsync(pairing.Code, "First")).Dispose();

        using var again = await ClaimNewAsync(pairing.Code, "Second");

        Assert.Contains("No screen is showing that code right now.", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        Assert.Equal(["First"], await _app.Factory.WithDbAsync(db => db.Screens.Select(s => s.Name).ToListAsync()));
    }

    [Fact]
    public async Task A_code_can_be_claimed_for_an_existing_unpaired_screen_but_not_for_a_paired_one_without_re_pair()
    {
        var (_, pairedId) = await PairNewDeviceAsync("Paired TV");
        var unpaired = new Screen { Name = "Unpaired TV" };
        await _app.Factory.WithDbAsync(async db =>
        {
            db.Screens.Add(unpaired);
            await db.SaveChangesAsync();
        });

        using var device = new PlayerClient(_app.Factory);
        var pairing = await device.StartPairingAsync();
        using (var refused = await ClaimExistingAsync(pairing.Code, pairedId))
        {
            Assert.Contains("That screen is already paired.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        }

        using var ok = await ClaimExistingAsync(pairing.Code, unpaired.Id);
        Assert.Equal($"/admin/screens/{unpaired.Id}", AdminSession.Location(ok));
        Assert.Equal("paired", await device.PollStatusAsync(pairing));
    }

    [Fact]
    public async Task Re_pairing_replaces_the_old_device()
    {
        var (oldDevice, screenId) = await PairNewDeviceAsync();
        using var newDevice = new PlayerClient(_app.Factory);
        var pairing = await newDevice.StartPairingAsync();

        using var claim = await ClaimExistingAsync(pairing.Code, screenId, repairLink: true);
        Assert.Equal($"/admin/screens/{screenId}", AdminSession.Location(claim));
        Assert.Equal("paired", await newDevice.PollStatusAsync(pairing));

        using var oldManifest = await oldDevice.GetManifestAsync();
        using var newManifest = await newDevice.GetManifestAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, oldManifest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newManifest.StatusCode);
        oldDevice.Dispose();
    }

    [Fact]
    public async Task Revoking_ends_access_at_once_and_the_401_removes_the_cookie()
    {
        var (device, screenId) = await PairNewDeviceAsync();
        _notifier.Clear();

        using var revoke = await _admin.PostFormAsync($"/admin/screens/{screenId}", new Dictionary<string, string>(), action: $"/admin/screens/{screenId}?handler=Revoke");

        Assert.Equal($"/admin/screens/{screenId}", AdminSession.Location(revoke));
        Assert.Null((await _app.Factory.WithDbAsync(db => db.Screens.AsNoTracking().SingleAsync())).TokenHash);
        Assert.Equal([screenId], _notifier.NotifiedScreens);
        using var manifest = await device.GetManifestAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, manifest.StatusCode);
        var deletion = ScreenCookie(manifest);
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
        device.Dispose();
    }

    [Fact]
    public async Task Requests_without_or_with_an_unknown_token_are_unauthorized()
    {
        using var none = new PlayerClient(_app.Factory);
        using var forged = new PlayerClient(_app.Factory);
        forged.Http.DefaultRequestHeaders.Add("Cookie", $"signage.screen={ScreenTokens.NewSecret()}");

        using var noCookie = await none.GetManifestAsync();
        using var badCookie = await forged.GetManifestAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badCookie.StatusCode);
    }

    [Fact]
    public async Task A_screen_address_is_shown_once_exchanged_for_the_cookie_and_replaces_earlier_pairing()
    {
        var (paired, screenId) = await PairNewDeviceAsync();

        using var created = await _admin.PostFormAsync($"/admin/screens/{screenId}", new Dictionary<string, string>(), action: $"/admin/screens/{screenId}?handler=Address");
        Assert.True(created.Headers.CacheControl?.NoStore);
        var address = WebUtility.HtmlDecode(AddressRegex().Match(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Groups[1].Value);
        Assert.StartsWith("http://localhost/screen?token=", address, StringComparison.Ordinal);
        var token = Uri.UnescapeDataString(address.Split("token=")[1]);

        using var kiosk = new PlayerClient(_app.Factory);
        using (var exchange = await kiosk.ExchangeAsync(token))
        {
            Assert.Equal(HttpStatusCode.NoContent, exchange.StatusCode);
            Assert.Contains("httponly", ScreenCookie(exchange), StringComparison.OrdinalIgnoreCase);
        }

        using var kioskManifest = await kiosk.GetManifestAsync();
        using var oldManifest = await paired.GetManifestAsync();
        Assert.Equal(HttpStatusCode.OK, kioskManifest.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldManifest.StatusCode);
        Assert.DoesNotContain(token, await _admin.GetHtmlAsync($"/admin/screens/{screenId}"), StringComparison.Ordinal);
        paired.Dispose();
    }

    [Fact]
    public async Task An_unknown_token_cannot_be_exchanged()
    {
        using var device = new PlayerClient(_app.Factory);

        using var response = await device.ExchangeAsync(ScreenTokens.NewSecret());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Starting_pairing_is_rate_limited_per_client()
    {
        using var device = new PlayerClient(_app.Factory);
        for (var i = 0; i < 10; i++)
        {
            await device.StartPairingAsync();
        }

        using var response = await device.Http.PostAsync(new Uri("/api/player/pairing", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Old_pairing_requests_are_cleaned_up()
    {
        using var device = new PlayerClient(_app.Factory);
        await device.StartPairingAsync();

        _app.Time.Advance(PairingService.CodeLifetime + PairingService.Retention + TimeSpan.FromMinutes(1));
        await device.StartPairingAsync();

        Assert.Equal(1, await _app.Factory.WithDbAsync(db => db.PairingRequests.CountAsync()));
    }

    [Fact]
    public async Task Editors_cannot_pair_revoke_or_create_screen_addresses()
    {
        await _app.CreateUserAsync();
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        var (device, screenId) = await PairNewDeviceAsync();
        var before = await _app.Factory.WithDbAsync(db => db.Screens.Select(s => s.TokenHash).SingleAsync());

        using var pairPage = await editor.GetAsync("/admin/screens/pair");
        using var revoke = await editor.PostFormAsync($"/admin/screens/{screenId}", new Dictionary<string, string>(), action: $"/admin/screens/{screenId}?handler=Revoke");
        using var address = await editor.PostFormAsync($"/admin/screens/{screenId}", new Dictionary<string, string>(), action: $"/admin/screens/{screenId}?handler=Address");

        foreach (var response in new[] { pairPage, revoke, address })
        {
            Assert.StartsWith("http://localhost/admin/forbidden", AdminSession.Location(response), StringComparison.Ordinal);
        }
        Assert.Equal(before, await _app.Factory.WithDbAsync(db => db.Screens.Select(s => s.TokenHash).SingleAsync()));
        device.Dispose();
    }

    [Fact]
    public async Task The_player_page_is_served_without_caching()
    {
        using var device = new PlayerClient(_app.Factory);

        using var page = await device.Http.GetAsync(new Uri("/screen", UriKind.Relative), TestContext.Current.CancellationToken);
        using var script = await device.Http.GetAsync(new Uri("/screen/player.js", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<main id=\"stage\"", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.True(page.Headers.CacheControl?.NoCache);
        Assert.True(script.Headers.CacheControl?.NoCache);
    }

    [GeneratedRegex("""data-testid="screen-address">([^<]+)<""")]
    private static partial Regex AddressRegex();
}
