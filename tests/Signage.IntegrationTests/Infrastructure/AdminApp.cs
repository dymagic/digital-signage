using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Signage.Web.Domain;
using Signage.Web.Features.Users;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>The app on its own fresh database, with fake time and helpers to create users directly.</summary>
public sealed class AdminApp : IAsyncDisposable
{
    public const string AdminEmail = "admin@example.org";
    public const string EditorEmail = "editor@example.org";
    public const string Password = "correct horse battery";

    private AdminApp(SignageWebFactory factory, FakeTimeProvider time)
    {
        Factory = factory;
        Time = time;
    }

    public SignageWebFactory Factory { get; }

    public FakeTimeProvider Time { get; }

    public static async Task<AdminApp> StartAsync(PostgresFixture postgres, IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
        var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync(), settings, time, configureServices);
        _ = factory.Server;
        return new AdminApp(factory, time);
    }

    /// <summary>Creates the first admin (no forced password change).</summary>
    public async Task<Guid> CreateAdminAsync(string email = AdminEmail)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
        if (!await accounts.AnyUsersAsync(TestContext.Current.CancellationToken))
        {
            var result = await accounts.CreateFirstAdminAsync(email, "Ada Admin", Password, mustChangePassword: false, TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, result.ErrorKey);
            return await Factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        }

        return await CreateUserAsync(email, UserRole.Admin);
    }

    /// <summary>Creates a user with <see cref="Password"/> who doesn't have to change it.</summary>
    public async Task<Guid> CreateUserAsync(string email = EditorEmail, UserRole role = UserRole.Editor)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
        var (result, temporary) = await accounts.CreateAsync(email, email, role, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.ErrorKey);
        var id = await Factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        var change = await accounts.ChangePasswordAsync(id, temporary!, Password, TestContext.Current.CancellationToken);
        Assert.True(change.Succeeded, change.ErrorKey);
        return id;
    }

    public async Task<AdminSession> SignedInAsync(string email)
    {
        var session = new AdminSession(Factory);
        await session.SignInSuccessfullyAsync(email, Password);
        return session;
    }

    public Task<User> GetUserAsync(Guid id) => Factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}
