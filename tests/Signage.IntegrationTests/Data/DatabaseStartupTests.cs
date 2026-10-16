using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Signage.IntegrationTests.Infrastructure;
using Signage.Web.Domain;

namespace Signage.IntegrationTests.Data;

public class DatabaseStartupTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> MigrationsOff = new() { ["Signage:MigrateOnStartup"] = "false" };

    [Fact]
    public async Task Startup_applies_all_migrations_to_an_empty_database_and_seeds_default_settings()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.Zero));
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync(), timeProvider: time);
        _ = factory.Server;

        await factory.WithDbAsync(async db =>
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var settings = await db.SystemSettings.SingleAsync();
            Assert.Equal(SystemSettings.SingletonId, settings.Id);
            Assert.Equal(10, settings.DefaultSlideDurationSeconds);
            Assert.False(settings.ShowInfoOverlay);
            Assert.Equal("UTC", settings.TimeZoneId);
            Assert.Equal(time.GetUtcNow(), settings.UpdatedAt);
        });
    }

    [Fact]
    public async Task Restarting_keeps_existing_settings_and_never_adds_a_second_row()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = new SignageWebFactory(connectionString))
        {
            await first.WithDbAsync(async db =>
            {
                var settings = await db.SystemSettings.SingleAsync();
                settings.DefaultSlideDurationSeconds = 25;
                settings.TimeZoneId = "Europe/Copenhagen";
                await db.SaveChangesAsync();
            });
        }

        await using var second = new SignageWebFactory(connectionString);
        _ = second.Server;

        await second.WithDbAsync(async db =>
        {
            var all = await db.SystemSettings.ToListAsync();
            var settings = Assert.Single(all);
            Assert.Equal(25, settings.DefaultSlideDurationSeconds);
            Assert.Equal("Europe/Copenhagen", settings.TimeZoneId);
        });
    }

    [Fact]
    public async Task Two_replicas_starting_at_once_on_an_empty_database_both_succeed()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var a = new SignageWebFactory(connectionString);
        await using var b = new SignageWebFactory(connectionString);

        await Task.WhenAll(Task.Run(() => a.Server), Task.Run(() => b.Server));

        Assert.Equal(1, await a.WithDbAsync(db => db.SystemSettings.CountAsync()));
    }

    [Fact]
    public async Task With_migrations_switched_off_an_up_to_date_database_starts_normally()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var migrator = new SignageWebFactory(connectionString))
        {
            _ = migrator.Server;
        }

        await using var factory = new SignageWebFactory(connectionString, MigrationsOff);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/readyz", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task With_migrations_switched_off_an_empty_database_fails_startup_with_a_clear_message()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync(), MigrationsOff);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Server);

        Assert.Contains("MigrateOnStartup", ex.Message, StringComparison.Ordinal);
        Assert.Contains("pending migration", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_migration_can_be_reverted_to_an_empty_schema_and_applied_again()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());

        await factory.WithDbAsync(async db =>
        {
            await db.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);

            Assert.Equal(db.Database.GetMigrations().Count(), (await db.Database.GetPendingMigrationsAsync()).Count());
            var tables = await db.Database.SqlQuery<string>(
                $"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'").ToListAsync();
            Assert.Empty(tables);
            var collations = await db.Database.SqlQuery<string>(
                $"SELECT collname AS \"Value\" FROM pg_collation WHERE collname = 'case_insensitive'").ToListAsync();
            Assert.Empty(collations);

            // And the schema can be built again from scratch.
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        });
    }

    [Fact]
    public async Task Data_protection_keys_are_shared_through_the_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var replicaA = new SignageWebFactory(connectionString);
        await using var replicaB = new SignageWebFactory(connectionString);

        var protectedValue = replicaA.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("hello");
        var unprotected = replicaB.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedValue);

        Assert.Equal("hello", unprotected);
        Assert.NotEqual(0, await replicaA.WithDbAsync(db => db.DataProtectionKeys.CountAsync()));
    }
}
