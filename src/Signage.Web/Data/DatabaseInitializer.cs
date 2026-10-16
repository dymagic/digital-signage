using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Signage.Web.Domain;

namespace Signage.Web.Data;

/// <summary>
/// Runs at start-up: applies pending migrations (unless switched off, R-60) and makes sure the single
/// <see cref="SystemSettings"/> row exists. Both steps are safe when several replicas start at once:
/// EF Core serialises migrations with a database lock, and the settings insert is ON CONFLICT DO NOTHING.
/// If the database is unreachable, start-up fails and the orchestrator restarts the container.
/// </summary>
internal sealed partial class DatabaseInitializer(
    SignageDbContext db,
    IOptions<SignageOptions> options,
    TimeProvider time,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (options.Value.MigrateOnStartup)
        {
            // Creating the history table up front stops EF from logging a "relation does not exist" error
            // when it probes a brand-new database.
            await db.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        var pending = await GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (options.Value.MigrateOnStartup)
        {
            if (pending.Count > 0)
            {
                LogApplyingMigrations(pending.Count, pending);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database schema is out of date ({pending.Count} pending migration(s)) and Signage:MigrateOnStartup is false. " +
                "Apply the migrations first, or enable SIGNAGE__MIGRATEONSTARTUP.");
        }

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO system_settings (id, default_slide_duration_seconds, show_info_overlay, time_zone_id, updated_at)
            VALUES ({SystemSettings.SingletonId}, {SystemSettings.DefaultSlideDuration}, FALSE, {SystemSettings.DefaultTimeZoneId}, {time.GetUtcNow()})
            ON CONFLICT (id) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Like <c>GetPendingMigrationsAsync</c>, but checks for the history table first so a brand-new
    /// database doesn't produce a logged "relation does not exist" error.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        var all = db.Database.GetMigrations().ToList();
        var history = db.GetService<IHistoryRepository>();
        if (!await history.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return all;
        }

        var applied = (await history.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => m.MigrationId)
            .ToHashSet(StringComparer.Ordinal);
        return all.Where(m => !applied.Contains(m)).ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} database migration(s): {Migrations}")]
    private partial void LogApplyingMigrations(int count, IReadOnlyList<string> migrations);
}

internal static class DatabaseInitializerExtensions
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
