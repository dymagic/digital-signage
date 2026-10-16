using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Web.Domain;

namespace Signage.Web.Data;

/// <summary>
/// Runs at start-up: applies pending migrations (unless switched off, R-60) and makes sure the single
/// <see cref="SystemSettings"/> row exists. Both steps are safe when several replicas start at once:
/// EF Core serialises migrations with a database lock, and the settings insert is ON CONFLICT DO NOTHING.
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
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pending.Count > 0)
            {
                LogApplyingMigrations(pending.Count, pending);
            }
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            LogMigrationsDisabled();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pending.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The database schema is out of date ({pending.Count} pending migration(s)) and Signage:MigrateOnStartup is false. " +
                    "Apply the migrations first, or enable SIGNAGE__MIGRATEONSTARTUP.");
            }
        }

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO system_settings (id, default_slide_duration_seconds, show_info_overlay, time_zone_id, updated_at)
            VALUES ({SystemSettings.SingletonId}, {SystemSettings.DefaultSlideDuration}, FALSE, {SystemSettings.DefaultTimeZoneId}, {time.GetUtcNow()})
            ON CONFLICT (id) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} database migration(s): {Migrations}")]
    private partial void LogApplyingMigrations(int count, IReadOnlyList<string> migrations);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatic migrations are disabled (Signage:MigrateOnStartup=false); the schema must already be up to date.")]
    private partial void LogMigrationsDisabled();
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
