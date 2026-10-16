using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Admin;

/// <summary>
/// Sign-ins run all the time, also by an attacker holding a leaked password. They must never undo a password
/// change or reset, and never get in the way of an admin locking the account down (R-52, ADR-0004).
/// </summary>
public sealed partial class ConcurrentSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AdminApp _app = null!;
    private Guid _editorId;
    private AdminSession _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _app = await AdminApp.StartAsync(postgres);
        await _app.CreateAdminAsync();
        _editorId = await _app.CreateUserAsync();
        _admin = await _app.SignedInAsync(AdminApp.AdminEmail);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>Signs in as the editor in a loop on 3 parallel clients until disposed.</summary>
    private sealed class SignInStorm : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task[] _loops;

        public SignInStorm(SignageWebFactory factory, string password)
        {
            _loops = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var session = new AdminSession(factory);
                    (await session.SignInAsync(AdminApp.EditorEmail, password)).Dispose();
                }
            })).ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await Task.WhenAll(_loops);
            _stop.Dispose();
        }
    }

    private async Task<bool> StoredPasswordIsAsync(string password)
    {
        var user = await _app.GetUserAsync(_editorId);
        var hasher = _app.Factory.Services.GetRequiredService<IPasswordHasher<User>>();
        return hasher.VerifyHashedPassword(user, user.PasswordHash!, password) != PasswordVerificationResult.Failed;
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
    public async Task An_admin_reset_wins_against_sign_ins_running_at_the_same_time()
    {
        string temporary;
        await using (new SignInStorm(_app.Factory, AdminApp.Password))
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
            for (var i = 0; ; i++)
            {
                using var reset = await _admin.PostFormAsync($"/admin/users/{_editorId}", new Dictionary<string, string>(), action: $"/admin/users/{_editorId}?handler=ResetPassword");
                Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
                temporary = WebUtility.HtmlDecode(TemporaryPasswordRegex().Match(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Groups[1].Value);
                if (i == 4)
                {
                    break;
                }
            }
        }

        Assert.True(await StoredPasswordIsAsync(temporary));
        Assert.False(await StoredPasswordIsAsync(AdminApp.Password));
    }

    [Fact]
    public async Task An_admin_can_deactivate_a_user_while_they_sign_in_repeatedly()
    {
        await using (new SignInStorm(_app.Factory, AdminApp.Password))
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
            var user = await _app.GetUserAsync(_editorId);
            using var response = await _admin.PostFormAsync($"/admin/users/{_editorId}", new Dictionary<string, string>
            {
                ["Input.DisplayName"] = user.DisplayName,
                ["Input.Role"] = "Editor",
                ["Input.IsActive"] = "false",
                ["Input.Version"] = user.Version.ToString(CultureInfo.InvariantCulture),
            });
            Assert.Equal("/admin/users", AdminSession.Location(response));
        }

        Assert.False((await _app.GetUserAsync(_editorId)).IsActive);
    }

    [Fact]
    public async Task A_password_change_is_never_undone_by_sign_ins_with_the_old_password()
    {
        using var editor = await _app.SignedInAsync(AdminApp.EditorEmail);
        var current = AdminApp.Password;
        for (var round = 0; round < 3; round++)
        {
            var next = $"replacement passphrase {round}";
            await using (new SignInStorm(_app.Factory, current))
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                using var changed = await editor.PostFormAsync("/admin/account/password", new Dictionary<string, string>
                {
                    ["Input.CurrentPassword"] = current,
                    ["Input.NewPassword"] = next,
                    ["Input.ConfirmPassword"] = next,
                });
                if (changed.StatusCode != HttpStatusCode.Redirect)
                {
                    // The storm locked the account before the change went through; that's the lockout working.
                    // Unlock and try the round again.
                    await _app.Factory.WithDbAsync(db => db.UserLockouts.ExecuteDeleteAsync());
                    round--;
                    continue;
                }
            }

            Assert.True(await StoredPasswordIsAsync(next), $"round {round}: the new password was overwritten");
            Assert.False(await StoredPasswordIsAsync(current));
            current = next;
            await _app.Factory.WithDbAsync(db => db.UserLockouts.ExecuteDeleteAsync());
        }
    }

    [GeneratedRegex("""data-testid="temporary-password">([^<]+)<""")]
    private static partial Regex TemporaryPasswordRegex();
}
