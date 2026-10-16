using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Signage.Web.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Registers PostgreSQL, the EF Core context and the database-backed data-protection key ring.</summary>
    public static IServiceCollection AddSignageData(this IServiceCollection services, string connectionString)
    {
        services.AddNpgsqlDataSource(ApplyConnectionDefaults(connectionString));
        services.AddDbContext<SignageDbContext>((sp, options) =>
        {
            ConfigureNpgsql(options, sp.GetRequiredService<NpgsqlDataSource>());
            options.AddInterceptors(new TimestampInterceptor(sp.GetRequiredService<TimeProvider>()));
        });

        // Keys live in the database so every replica can read every cookie and redeploys keep sessions (R-63).
        services.AddDataProtection()
            .SetApplicationName("Signage")
            .PersistKeysToDbContext<SignageDbContext>();

        services.AddTransient<DatabaseInitializer>();
        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource).UseSnakeCaseNamingConvention();

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>
    /// The chiseled runtime image has no Kerberos libraries. Npgsql would probe GSS encryption on every new
    /// connection and log an error, so it is disabled unless the connection string sets it explicitly.
    /// </summary>
    internal static string ApplyConnectionDefaults(string connectionString)
    {
        var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var setsGss = parsed.Keys.Cast<string>()
            .Select(k => k.Replace(" ", "", StringComparison.Ordinal))
            .Any(k => k.Equals("GssEncryptionMode", StringComparison.OrdinalIgnoreCase) || k.Equals("GssEncMode", StringComparison.OrdinalIgnoreCase));
        if (setsGss)
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable };
        return builder.ConnectionString;
    }
}
